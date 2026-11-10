// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "event_thread.h"

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdio.h>
#include <string.h>

#include "event_queue.h"
#include "platform.h"

// The event thread, its queues and its callbacks: a process-wide singleton (C-0010
// section 3), which pp_init starts and pp_shutdown stops.
static struct {
    pp_queue_t events;
    pp_queue_t logs;
    pthread_t thread;

    // The event thread's own identity, which it stores when it starts.
    _Atomic(pthread_t) self;
    _Atomic(bool) self_known;

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
    };
    s_thread.log_callback(&line, s_thread.log_user_data);
}

static void make_notice(pp_log_entry_t *entry, const char *text) {
    entry->level = PP_LOG_WARNING;
    entry->thread_id = pp_platform_thread_id();
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
        notice.thread_id = pp_platform_thread_id();
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

// Logs the first overflow of each queue once. Later refusals are only counted, so that a
// flood does not turn into a flood of notices.
static void report_overflow(void) {
    pp_log_entry_t notice;
    if (!s_thread.events_overflow_reported && pp_queue_overflow_count(&s_thread.events) > 0U) {
        s_thread.events_overflow_reported = true;
        make_notice(&notice, "The host event queue was full, so events were dropped. Later "
                             "drops are counted, not logged.");
        receive_line(&notice);
    }
    if (!s_thread.logs_overflow_reported && pp_queue_overflow_count(&s_thread.logs) > 0U) {
        s_thread.logs_overflow_reported = true;
        make_notice(&notice, "The host log queue was full, so log lines were dropped. Later "
                             "drops are counted, not logged.");
        receive_line(&notice);
    }
}

static void drain(void) {
    pp_event_t event;
    pp_log_entry_t entry;
    bool delivered = true;
    while (delivered) {
        delivered = false;
        if (pp_queue_pop(&s_thread.events, &event)) {
            receive_event(&event);
            delivered = true;
        }
        if (pp_queue_pop(&s_thread.logs, &entry)) {
            receive_line(&entry);
            delivered = true;
        }
        report_overflow();
    }
}

static void *thread_main(void *argument) {
    (void)argument;
    atomic_store_explicit(&s_thread.self, pthread_self(), memory_order_relaxed);
    atomic_store_explicit(&s_thread.self_known, true, memory_order_release);
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
        pp_queue_init(&s_thread.events, PP_EVENT_QUEUE_CAPACITY, (uint32_t)sizeof(pp_event_t));
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

    if (pthread_create(&s_thread.thread, NULL, thread_main, NULL) != 0) {
        pp_queue_destroy(&s_thread.logs);
        pp_queue_destroy(&s_thread.events);
        return PP_E_SYSTEM;
    }

    atomic_store(&s_thread.accepting, true);
    return PP_OK;
}

void pp_event_thread_stop(void) {
    atomic_store(&s_thread.accepting, false);
    atomic_store(&s_thread.stop_requested, true);
    wake_thread();
    (void)pthread_join(s_thread.thread, NULL);
    atomic_store_explicit(&s_thread.self_known, false, memory_order_release);

    (void)pthread_mutex_lock(&s_thread.callback_mutex);
    s_thread.event_callback = NULL;
    s_thread.event_user_data = NULL;
    s_thread.log_callback = NULL;
    s_thread.log_user_data = NULL;
    s_thread.pending_count = 0U;
    s_thread.pending_dropped = 0U;
    (void)pthread_mutex_unlock(&s_thread.callback_mutex);

    // A producer that saw accepting still true may be inside its push.
    while (atomic_load(&s_thread.posting) != 0U) {
        (void)sched_yield();
    }
    pp_queue_destroy(&s_thread.logs);
    pp_queue_destroy(&s_thread.events);
}

static pp_status_t post(pp_queue_t *queue, const void *entry) {
    (void)atomic_fetch_add(&s_thread.posting, 1U);
    pp_status_t status = PP_OK;
    if (!atomic_load(&s_thread.accepting)) {
        status = PP_E_NOT_INITIALIZED;
    } else if (!pp_queue_push(queue, entry)) {
        status = PP_E_BUSY;
    } else {
        wake_if_sleeping();
    }
    (void)atomic_fetch_sub(&s_thread.posting, 1U);
    return status;
}

pp_status_t pp_event_thread_post_event(const pp_event_t *event) {
    return post(&s_thread.events, event);
}

pp_status_t pp_event_thread_post_log(const pp_log_entry_t *entry) {
    return post(&s_thread.logs, entry);
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

bool pp_event_thread_is_current(void) {
    return atomic_load_explicit(&s_thread.self_known, memory_order_acquire) &&
           pthread_equal(pthread_self(),
                         atomic_load_explicit(&s_thread.self, memory_order_relaxed)) != 0;
}
