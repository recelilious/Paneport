// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "event_thread.h"

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>
#include <time.h>

#include "event_queue.h"
#include "platform.h"
#include "thread_role.h"

// How long the thread waits, before it delivers an event, for a line that a producer claimed
// below the event's lines_end but has not published yet (drain).
#define PP_LINE_WAIT_NANOSECONDS (UINT64_C(100) * 1000U * 1000U)

// An event as it waits in its queue: the event, and the log queue's tail as its post read it.
// Every line whose post returned before the event's post lies below lines_end.
typedef struct queued_event_t {
    pp_event_t event;
    uint64_t lines_end;
} queued_event_t;

// The event thread, its queues and its callbacks: a process-wide singleton (C-0010
// section 3), which pp_init starts and pp_shutdown stops.
static struct {
    pp_queue_t events;
    pp_queue_t logs;
    pthread_t thread;

    // Posts are accepted while this is true. posting counts the posts in progress, so that
    // stopping waits for a producer that saw it true before it turned false.
    _Atomic(bool) accepting;
    _Atomic(uint32_t) posting;

    // The thread delivers what is queued and ends once stop_requested is true. A new log
    // callback sets flush_requested, so that the buffered lines arrive without waiting for
    // the next line.
    _Atomic(bool) stop_requested;
    _Atomic(bool) flush_requested;

    // 1 while the thread is about to sleep or sleeps; a producer then wakes it.
    _Atomic(int) sleeping;
    pthread_mutex_t wake_mutex;
    pthread_cond_t wake_condition;
    bool wake_pending; // Guarded by wake_mutex.

    // Held while a callback runs and while one is replaced, so that a replaced callback is
    // never running once the replacing call returns.
    pthread_mutex_t callback_mutex;
    pp_event_callback_t event_callback; // Guarded by callback_mutex, as are the three below.
    void *event_user_data;
    pp_log_callback_t log_callback;
    void *log_user_data;

    // Used by the event thread only, under callback_mutex: the earliest lines that arrived
    // without a log callback, how many more were dropped, and whether each queue's first
    // overflow has been logged.
    pp_log_entry_t pending[PP_PENDING_LOG_LINES];
    uint32_t pending_count;
    uint64_t pending_dropped;
    bool events_overflow_reported;
    bool logs_overflow_reported;

    // Used by the event thread only: the position of the log queue whose unpublished line kept
    // an event waiting until the wait ended, so that it does not hold the next one too.
    bool line_wait_ended;
    uint64_t line_wait_position;
} s_thread = {
    .wake_mutex = PTHREAD_MUTEX_INITIALIZER,
    .wake_condition = PTHREAD_COND_INITIALIZER,
    .callback_mutex = PTHREAD_MUTEX_INITIALIZER,
};

static void wake_thread(void) {
    (void)pthread_mutex_lock(&s_thread.wake_mutex);
    s_thread.wake_pending = true;
    (void)pthread_cond_signal(&s_thread.wake_condition);
    (void)pthread_mutex_unlock(&s_thread.wake_mutex);
}

// Called by a producer after its push. The fence pairs with the one in wait_for_work: either
// the thread sees the new entry before it sleeps, or the producer sees that it sleeps.
static void wake_if_sleeping(void) {
    atomic_thread_fence(memory_order_seq_cst);
    if (atomic_exchange_explicit(&s_thread.sleeping, 0, memory_order_seq_cst) != 0) {
        wake_thread();
    }
}

static bool has_work(void) {
    if (!pp_queue_is_empty(&s_thread.events) || !pp_queue_is_empty(&s_thread.logs)) {
        return true;
    }
    return atomic_load(&s_thread.stop_requested) || atomic_load(&s_thread.flush_requested);
}

static void wait_for_work(void) {
    atomic_store_explicit(&s_thread.sleeping, 1, memory_order_seq_cst);
    atomic_thread_fence(memory_order_seq_cst);
    if (has_work()) {
        atomic_store_explicit(&s_thread.sleeping, 0, memory_order_seq_cst);
        return;
    }

    (void)pthread_mutex_lock(&s_thread.wake_mutex);
    while (!s_thread.wake_pending) {
        (void)pthread_cond_wait(&s_thread.wake_condition, &s_thread.wake_mutex);
    }
    s_thread.wake_pending = false;
    (void)pthread_mutex_unlock(&s_thread.wake_mutex);
}

// The helpers below run on the event thread with callback_mutex held.

static void deliver_line(const pp_log_entry_t *entry) {
    const pp_log_line_t line = {
        .struct_size = (uint32_t)sizeof(pp_log_line_t),
        .level = entry->level,
        .thread_id = entry->thread_id,
        .text = entry->text,
        .source = entry->source,
        .process = entry->process,
    };
    s_thread.log_callback(&line, s_thread.log_user_data);
}

static void make_notice(pp_log_entry_t *entry, const char *text) {
    entry->level = PP_LOG_WARNING;
    entry->source = PP_LOG_SOURCE_HOST;
    entry->thread_id = pp_platform_thread_id();
    entry->process = 0U;
    (void)snprintf(entry->text, sizeof entry->text, "%s", text);
}

// Delivers the lines that arrived before a log callback was set, oldest first, followed by
// a notice when the buffer could not hold them all.
static void flush_pending(void) {
    if (s_thread.log_callback == NULL) {
        return;
    }

    for (uint32_t index = 0; index < s_thread.pending_count; index++) {
        deliver_line(&s_thread.pending[index]);
    }

    if (s_thread.pending_dropped > 0U) {
        pp_log_entry_t notice;
        notice.level = PP_LOG_WARNING;
        notice.source = PP_LOG_SOURCE_HOST;
        notice.thread_id = pp_platform_thread_id();
        notice.process = 0U;
        (void)snprintf(notice.text, sizeof notice.text,
                       "%llu host log lines that came before a log callback was set were "
                       "dropped, because the buffer for them holds %u.",
                       (unsigned long long)s_thread.pending_dropped, PP_PENDING_LOG_LINES);
        deliver_line(&notice);
    }

    s_thread.pending_count = 0U;
    s_thread.pending_dropped = 0U;
}

static void handle_line(const pp_log_entry_t *entry) {
    if (s_thread.log_callback == NULL) {
        if (s_thread.pending_count < PP_PENDING_LOG_LINES) {
            s_thread.pending[s_thread.pending_count] = *entry;
            s_thread.pending_count++;
        } else {
            s_thread.pending_dropped++;
        }
        return;
    }

    flush_pending();
    deliver_line(entry);
}

// The helpers below run on the event thread and take callback_mutex themselves.

static void receive_line(const pp_log_entry_t *entry) {
    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    handle_line(entry);
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);
}

static void receive_event(const pp_event_t *event) {
    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    if (s_thread.event_callback != NULL) {
        s_thread.event_callback(event, s_thread.event_user_data);
    }
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);
}

// Logs the first overflow of one queue once; later refusals are only counted, so that a flood
// does not turn into a flood of notices. While no log callback is set and the buffer for early
// lines is full, the notice would be dropped with the lines, so it waits: the flag stays clear,
// and the first drain after a callback is set delivers it.
static void report_overflow_of(const pp_queue_t *queue, bool *reported, const char *text) {
    if (*reported || pp_queue_overflow_count(queue) == 0U) {
        return;
    }

    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    if (s_thread.log_callback != NULL || s_thread.pending_count < PP_PENDING_LOG_LINES) {
        *reported = true;
        pp_log_entry_t notice;
        make_notice(&notice, text);
        handle_line(&notice);
    }
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);
}

static void report_overflow(void) {
    report_overflow_of(&s_thread.events, &s_thread.events_overflow_reported,
                       "The host event queue was full, so events were dropped. Later drops are "
                       "counted, not logged.");
    report_overflow_of(&s_thread.logs, &s_thread.logs_overflow_reported,
                       "The host log queue was full, so log lines were dropped. Later drops are "
                       "counted, not logged.");
}

static uint64_t monotonic_nanoseconds(void) {
    struct timespec now;
    (void)clock_gettime(CLOCK_MONOTONIC, &now);
    return ((uint64_t)now.tv_sec * UINT64_C(1000000000)) + (uint64_t)now.tv_nsec;
}

#ifdef PANEPORT_TEST_BUILD
// How many waits for an unpublished line the drain began, which the test builds report.
static _Atomic(uint64_t) s_line_waits;

uint64_t pp_event_thread_line_waits(void) { return atomic_load(&s_line_waits); }
#endif

// Counts a wait of the drain for the test builds. Nothing in the shipped builds.
static void count_line_wait(void) {
#ifdef PANEPORT_TEST_BUILD
    (void)atomic_fetch_add(&s_line_waits, 1U);
#endif
}

// Delivers the log lines below lines_end, which an event carries: every line whose post
// returned before the event's post lies there, so it arrives before the event, and lines
// posted later do not hold the event back. A slot below lines_end that a producer claimed but
// has not published yet holds the event back for at most PP_LINE_WAIT_NANOSECONDS. A producer
// is inside a push for a few instructions, unless the system preempts it there, which can last
// any time (thread control never holds a guest thread there, single-process-wine.md section 7);
// so past the wait the event goes first, before the slot's line and every line behind it, since
// the log queue delivers its lines in the order of their positions: lines that other producers
// posted, and whose posts returned, before the event's post follow it too. The same slot does not
// hold another event.
static void deliver_lines_before(uint64_t lines_end) {
    pp_log_entry_t entry;
    bool waiting = false;
    uint64_t deadline = 0U;
    while (pp_queue_head(&s_thread.logs) < lines_end) {
        if (pp_queue_pop(&s_thread.logs, &entry)) {
            receive_line(&entry);
            waiting = false;
            continue;
        }
        const uint64_t position = pp_queue_head(&s_thread.logs);
        if (s_thread.line_wait_ended && s_thread.line_wait_position == position) {
            return;
        }
        const uint64_t now = monotonic_nanoseconds();
        if (!waiting) {
            waiting = true;
            deadline = now + PP_LINE_WAIT_NANOSECONDS;
            count_line_wait();
        } else if (now >= deadline) {
            s_thread.line_wait_ended = true;
            s_thread.line_wait_position = position;
            return;
        }
        (void)sched_yield();
    }
}

// Delivers events and log lines in turn, each event after the lines posted before it, but for the
// exception of deliver_lines_before: the lines of a Windows program come before its
// process-exited event (paneport/session.h).
static void drain(void) {
    bool delivered = true;
    while (delivered) {
        delivered = false;
        queued_event_t queued;
        if (pp_queue_pop(&s_thread.events, &queued)) {
            deliver_lines_before(queued.lines_end);
            receive_event(&queued.event);
            delivered = true;
        }
        pp_log_entry_t entry;
        if (pp_queue_pop(&s_thread.logs, &entry)) {
            receive_line(&entry);
            delivered = true;
        }
        report_overflow();
    }
}

static void *thread_main(void *argument) {
    (void)argument;
    pp_thread_role_set_event_thread();
    pp_platform_name_thread("paneport.host-events");

    for (;;) {
        drain();
        if (atomic_exchange(&s_thread.flush_requested, false)) {
            (void)pthread_mutex_lock(&s_thread.callback_mutex);
            flush_pending();
            (void)pthread_mutex_unlock(&s_thread.callback_mutex);
        }
        if (atomic_load(&s_thread.stop_requested)) {
            drain();
            break;
        }
        wait_for_work();
    }
    return NULL;
}

pp_status_t pp_event_thread_start(void) {
    pp_status_t status =
        pp_queue_init(&s_thread.events, PP_EVENT_QUEUE_CAPACITY, (uint32_t)sizeof(queued_event_t));
    if (status != PP_OK) {
        return status;
    }
    status = pp_queue_init(&s_thread.logs, PP_LOG_QUEUE_CAPACITY, (uint32_t)sizeof(pp_log_entry_t));
    if (status != PP_OK) {
        pp_queue_destroy(&s_thread.events);
        return status;
    }

    atomic_store(&s_thread.stop_requested, false);
    atomic_store(&s_thread.flush_requested, false);
    atomic_store(&s_thread.sleeping, 0);
    s_thread.wake_pending = false;
    s_thread.pending_count = 0U;
    s_thread.pending_dropped = 0U;
    s_thread.events_overflow_reported = false;
    s_thread.logs_overflow_reported = false;
    s_thread.line_wait_ended = false;
    s_thread.line_wait_position = 0U;

    if (pthread_create(&s_thread.thread, NULL, thread_main, NULL) != 0) {
        pp_queue_destroy(&s_thread.logs);
        pp_queue_destroy(&s_thread.events);
        return PP_E_SYSTEM;
    }

    atomic_store(&s_thread.accepting, true);
    return PP_OK;
}

void pp_event_thread_stop(void) {
    // Refuse new posts, then wait for the posts in progress before the thread stops. A producer
    // that saw accepting still true may be inside its push; once posting is 0, every post that
    // returned PP_OK has published its entry, so the final drain delivers it, and no slot is left
    // claimed but unpublished in front of others. A post that starts now sees accepting false
    // and never touches a queue.
    atomic_store(&s_thread.accepting, false);
    while (atomic_load(&s_thread.posting) != 0U) {
        (void)sched_yield();
    }

    atomic_store(&s_thread.stop_requested, true);
    wake_thread();
    (void)pthread_join(s_thread.thread, NULL);
    pp_thread_role_clear_event_thread();

    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    s_thread.event_callback = NULL;
    s_thread.event_user_data = NULL;
    s_thread.log_callback = NULL;
    s_thread.log_user_data = NULL;
    s_thread.pending_count = 0U;
    s_thread.pending_dropped = 0U;
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);

    pp_queue_destroy(&s_thread.logs);
    pp_queue_destroy(&s_thread.events);
}

#ifdef PANEPORT_TEST_BUILD
// The hook of the test builds, and its argument: set once by a test, before the posts it holds.
static _Atomic(pp_post_hook_t) s_post_hook;
static _Atomic(void *) s_post_hook_argument;

void pp_event_thread_set_post_hook(pp_post_hook_t hook, void *argument) {
    atomic_store(&s_post_hook_argument, argument);
    atomic_store(&s_post_hook, hook);
}
#endif

// Lets a test hold a post between its check that the thread runs and its push. Nothing in the
// shipped builds.
static void run_post_hook(void) {
#ifdef PANEPORT_TEST_BUILD
    const pp_post_hook_t hook = atomic_load(&s_post_hook);
    if (hook != NULL) {
        hook(atomic_load(&s_post_hook_argument));
    }
#endif
}

// Queues an entry. For an event, lines_end names its member that receives the log queue's tail,
// which is read once the thread accepts the post, while both queues exist.
static pp_status_t post(pp_queue_t *queue, const void *entry, uint64_t *lines_end) {
    (void)atomic_fetch_add(&s_thread.posting, 1U);
    pp_status_t status = PP_OK;
    if (!atomic_load(&s_thread.accepting)) {
        status = PP_E_NOT_INITIALIZED;
    } else {
        run_post_hook();
        if (lines_end != NULL) {
            // Every line whose post returned before this post claimed a position below the tail.
            *lines_end = pp_queue_tail(&s_thread.logs);
        }
        if (!pp_queue_push(queue, entry)) {
            status = PP_E_BUSY;
        } else {
            wake_if_sleeping();
        }
    }
    (void)atomic_fetch_sub(&s_thread.posting, 1U);
    return status;
}

pp_status_t pp_event_thread_post_event(const pp_event_t *event) {
    queued_event_t queued = {.event = *event, .lines_end = 0U};
    return post(&s_thread.events, &queued, &queued.lines_end);
}

pp_status_t pp_event_thread_post_log(const pp_log_entry_t *entry) {
    return post(&s_thread.logs, entry, NULL);
}

pp_status_t pp_event_thread_post_log_waiting(const pp_log_entry_t *entry) {
    const struct timespec pause = {.tv_sec = 0, .tv_nsec = 1000000L};
    for (;;) {
        (void)atomic_fetch_add(&s_thread.posting, 1U);
        pp_status_t status = PP_OK;
        bool posted = false;
        if (!atomic_load(&s_thread.accepting)) {
            status = PP_E_NOT_INITIALIZED;
        } else {
            run_post_hook();
            posted = pp_queue_try_push(&s_thread.logs, entry);
            // Wake the thread either way: a full queue needs draining too.
            wake_if_sleeping();
        }
        (void)atomic_fetch_sub(&s_thread.posting, 1U);
        if (posted || status != PP_OK) {
            return status;
        }
        (void)nanosleep(&pause, NULL);
    }
}

void pp_event_thread_set_event_callback(pp_event_callback_t callback, void *user_data) {
    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    s_thread.event_callback = callback;
    s_thread.event_user_data = user_data;
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);
}

void pp_event_thread_set_log_callback(pp_log_callback_t callback, void *user_data) {
    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    s_thread.log_callback = callback;
    s_thread.log_user_data = user_data;
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);
    if (callback != NULL) {
        atomic_store(&s_thread.flush_requested, true);
        wake_thread();
    }
}

bool pp_event_thread_is_current(void) { return pp_thread_role_is_event_thread(); }
