// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.3: the event thread, the callbacks, pp_init and pp_shutdown; and for M4.3 the order of
// lines and events, during a flood of lines and with a producer held inside a push.

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdint.h>
#include <string.h>
#include <time.h>

#include "event_queue.h"
#include "event_thread.h"
#include "log.h"
#include "paneport/host.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "test_support.h"

enum { PRODUCERS = 8, EVENTS_PER_PRODUCER = 2000 };

// What the event callback counts. next is touched by the event thread only.
typedef struct counted_events_t {
    _Atomic(uint64_t) received;
    uint32_t next[PRODUCERS];
} counted_events_t;

static void count_event(const pp_event_t *event, void *user_data) {
    counted_events_t *counted = user_data;
    PP_CHECK(pp_event_thread_is_current());
    PP_CHECK_EQUAL(event->struct_size, sizeof(pp_event_t));
    PP_CHECK(event->kind < (uint32_t)PRODUCERS);
    PP_CHECK_EQUAL(event->code, counted->next[event->kind]);
    counted->next[event->kind]++;
    (void)atomic_fetch_add(&counted->received, 1U);
}

// Posts one event, waiting while the queue is full.
static void post_event(uint32_t kind, int64_t code) {
    pp_status_t status = pp_test_post_event(kind, code);
    while (status == PP_E_BUSY) {
        (void)sched_yield();
        status = pp_test_post_event(kind, code);
    }
    PP_CHECK_EQUAL(status, PP_OK);
}

static void *post_events(void *argument) {
    const pp_thread_argument_t *thread = argument;
    for (int64_t sequence = 0; sequence < EVENTS_PER_PRODUCER; sequence++) {
        post_event(thread->number, sequence);
    }
    return NULL;
}

// Events posted from many threads reach the callback on the event thread only, each once and
// in order per thread. The queue holds fewer events than each thread posts.
static void events_reach_event_thread(void) {
    counted_events_t counted = {.next = {0}};
    atomic_init(&counted.received, 0U);
    pp_start_host(NULL);
    PP_CHECK(!pp_event_thread_is_current());
    PP_CHECK_EQUAL(pp_set_event_callback(count_event, &counted), PP_OK);

    pthread_t threads[PRODUCERS];
    pp_thread_argument_t arguments[PRODUCERS];
    pp_start_threads(threads, arguments, (size_t)PRODUCERS, post_events, NULL);
    pp_join_threads(threads, (size_t)PRODUCERS);
    pp_wait_for_count(&counted.received, (uint64_t)PRODUCERS * (uint64_t)EVENTS_PER_PRODUCER);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// What none_after_shutdown watches.
typedef struct shutdown_watch_t {
    _Atomic(bool) shutdown_returned;
    _Atomic(bool) stop_posting;
    _Atomic(uint64_t) late_calls;
} shutdown_watch_t;

static void watch_event(const pp_event_t *event, void *user_data) {
    shutdown_watch_t *watch = user_data;
    (void)event;
    if (atomic_load(&watch->shutdown_returned)) {
        (void)atomic_fetch_add(&watch->late_calls, 1U);
    }
}

static void watch_line(const pp_log_line_t *line, void *user_data) {
    shutdown_watch_t *watch = user_data;
    (void)line;
    if (atomic_load(&watch->shutdown_returned)) {
        (void)atomic_fetch_add(&watch->late_calls, 1U);
    }
}

static void *post_until_stopped(void *argument) {
    const pp_thread_argument_t *thread = argument;
    shutdown_watch_t *watch = thread->context;
    while (!atomic_load(&watch->stop_posting)) {
        (void)pp_test_post_event(1U, 1);
        (void)pp_test_log(PP_LOG_DEBUG, "a line while the host stops");
    }
    return NULL;
}

// No callback runs once pp_shutdown has returned, while other threads keep posting.
static void none_after_shutdown(void) {
    shutdown_watch_t watch;
    atomic_init(&watch.shutdown_returned, false);
    atomic_init(&watch.stop_posting, false);
    atomic_init(&watch.late_calls, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(watch_event, &watch), PP_OK);
    PP_CHECK_EQUAL(pp_set_log_callback(watch_line, &watch), PP_OK);

    pthread_t threads[4];
    pp_thread_argument_t arguments[4];
    pp_start_threads(threads, arguments, 4U, post_until_stopped, &watch);
    pp_sleep_milliseconds(20);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    atomic_store(&watch.shutdown_returned, true);
    pp_sleep_milliseconds(20);
    atomic_store(&watch.stop_posting, true);
    pp_join_threads(threads, 4U);

    PP_CHECK_EQUAL(atomic_load(&watch.late_calls), 0U);
    PP_CHECK_EQUAL(pp_test_post_event(1U, 1), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "after shutdown"), PP_E_NOT_INITIALIZED);
}

// What the post held by the hook saw, and what the event callback delivered.
typedef struct held_post_t {
    _Atomic(bool) in_hook;
    _Atomic(bool) shutdown_called;
    _Atomic(int) post_status;
    _Atomic(uint64_t) delivered;
} held_post_t;

// Holds the first post between its check that the host runs and its push, until pp_shutdown
// has had time to stop the event thread, as a preempted producer would be held.
static void hold_first_post(void *argument) {
    held_post_t *held = argument;
    if (atomic_exchange(&held->in_hook, true)) {
        return;
    }
    pp_wait_for_flag(&held->shutdown_called);
    pp_sleep_milliseconds(100);
}

static void count_held_event(const pp_event_t *event, void *user_data) {
    held_post_t *held = user_data;
    if (event->kind == 9U) {
        (void)atomic_fetch_add(&held->delivered, 1U);
    }
}

static void *post_held_event(void *argument) {
    const pp_thread_argument_t *thread = argument;
    held_post_t *held = thread->context;
    atomic_store(&held->post_status, pp_test_post_event(9U, 0));
    return NULL;
}

// A post that passed its check before pp_shutdown began still has its event delivered before
// pp_shutdown returns: the host waits for posts in progress before it stops the thread.
static void post_in_progress_is_delivered(void) {
    held_post_t held;
    atomic_init(&held.in_hook, false);
    atomic_init(&held.shutdown_called, false);
    atomic_init(&held.post_status, 1);
    atomic_init(&held.delivered, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(count_held_event, &held), PP_OK);
    pp_event_thread_set_post_hook(hold_first_post, &held);

    pthread_t thread;
    pp_thread_argument_t argument;
    pp_start_threads(&thread, &argument, 1U, post_held_event, &held);
    pp_wait_for_flag(&held.in_hook);
    atomic_store(&held.shutdown_called, true);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    pp_join_thread(thread);

    PP_CHECK_EQUAL(atomic_load(&held.post_status), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&held.delivered), 1U);
    pp_event_thread_set_post_hook(NULL, NULL);
}

// What event_overflow_logged_once counts: the events of kind 2, the overflow notices, and
// whether the event callback blocks on an event of kind 1.
typedef struct event_flood_t {
    _Atomic(bool) blocked;
    _Atomic(bool) release;
    _Atomic(uint64_t) flood_events;
    _Atomic(uint64_t) notices;
} event_flood_t;

static void block_on_kind_one(const pp_event_t *event, void *user_data) {
    event_flood_t *flood = user_data;
    if (event->kind == 2U) {
        (void)atomic_fetch_add(&flood->flood_events, 1U);
    } else if (event->kind == 1U) {
        atomic_store(&flood->blocked, true);
        pp_wait_for_flag(&flood->release);
        atomic_store(&flood->blocked, false);
    }
}

static void count_event_notices(const pp_log_line_t *line, void *user_data) {
    event_flood_t *flood = user_data;
    if (strstr(line->text, "event queue was full") != NULL) {
        (void)atomic_fetch_add(&flood->notices, 1U);
    }
}

// Posts more events than the queue holds while the event callback blocks, and returns how
// many the queue took.
static uint64_t flood_events_while_blocked(event_flood_t *flood) {
    atomic_store(&flood->release, false);
    PP_CHECK_EQUAL(pp_test_post_event(1U, 0), PP_OK);
    pp_wait_for_flag(&flood->blocked);

    uint64_t accepted = 0U;
    uint64_t refused = 0U;
    for (int64_t index = 0; index < 400; index++) {
        const pp_status_t status = pp_test_post_event(2U, index);
        if (status == PP_OK) {
            accepted++;
        } else {
            PP_CHECK_EQUAL(status, PP_E_BUSY);
            refused++;
        }
    }
    PP_CHECK(refused > 0U);
    atomic_store(&flood->release, true);
    return accepted;
}

// A full event queue refuses events, and its first overflow is logged once, never again.
static void event_overflow_logged_once(void) {
    event_flood_t flood;
    atomic_init(&flood.blocked, false);
    atomic_init(&flood.release, false);
    atomic_init(&flood.flood_events, 0U);
    atomic_init(&flood.notices, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(count_event_notices, &flood), PP_OK);
    PP_CHECK_EQUAL(pp_set_event_callback(block_on_kind_one, &flood), PP_OK);

    uint64_t accepted = flood_events_while_blocked(&flood);
    pp_wait_for_count(&flood.flood_events, accepted);
    pp_wait_for_count(&flood.notices, 1U);

    accepted += flood_events_while_blocked(&flood);
    pp_wait_for_count(&flood.flood_events, accepted);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&flood.notices), 1U);
}

// pp_init runs once per process, also after pp_shutdown; a failed call does not count.
static void second_init_fails(void) {
    const pp_init_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_init_config_t),
        .crash_record_folder = NULL,
    };
    PP_CHECK_EQUAL(pp_set_log_callback(NULL, NULL), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_init(NULL), PP_E_INVALID_ARGUMENT);
    const pp_init_config_t too_small = {.struct_size = 2U, .crash_record_folder = NULL};
    PP_CHECK_EQUAL(pp_init(&too_small), PP_E_INVALID_ARGUMENT);
    const pp_init_config_t no_folder = {
        .struct_size = (uint32_t)sizeof(pp_init_config_t),
        .crash_record_folder = "/nonexistent-paneport-folder",
    };
    PP_CHECK_EQUAL(pp_init(&no_folder), PP_E_INVALID_ARGUMENT);
    PP_CHECK(strstr(pp_last_error(), "not a folder") != NULL);
    const pp_init_config_t relative_folder = {
        .struct_size = (uint32_t)sizeof(pp_init_config_t),
        .crash_record_folder = ".",
    };
    PP_CHECK_EQUAL(pp_init(&relative_folder), PP_E_INVALID_ARGUMENT);
    PP_CHECK(strstr(pp_last_error(), "absolute path") != NULL);

    // A config that ends before crash_record_folder means no folder.
    const pp_init_config_t only_size = {.struct_size = 4U, .crash_record_folder = "/ignored"};
    PP_CHECK_EQUAL(pp_init(&only_size), PP_OK);
    PP_CHECK_EQUAL(pp_init(&config), PP_E_ALREADY_INITIALIZED);
    PP_CHECK(strstr(pp_last_error(), "once per process") != NULL);
    PP_CHECK_EQUAL(pp_test_write_crash_record("no folder"), PP_E_NOT_INITIALIZED);

    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    PP_CHECK_EQUAL(pp_init(&config), PP_E_ALREADY_INITIALIZED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_set_event_callback(NULL, NULL), PP_E_NOT_INITIALIZED);
}

static void ignore_event(const pp_event_t *event, void *user_data) {
    (void)event;
    (void)user_data;
}

static void ignore_line(const pp_log_line_t *line, void *user_data) {
    (void)line;
    (void)user_data;
}

static void *call_from_other_thread(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pp_set_event_callback(ignore_event, NULL), PP_E_WRONG_THREAD);
    PP_CHECK(strstr(pp_last_error(), "main thread") != NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(ignore_line, NULL), PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_WRONG_THREAD);
    PP_CHECK(strstr(pp_last_error(), "pp_shutdown") != NULL);
    const pp_init_config_t config = {.struct_size = (uint32_t)sizeof(pp_init_config_t)};
    PP_CHECK_EQUAL(pp_init(&config), PP_E_ALREADY_INITIALIZED);
    return NULL;
}

// A [main] function called on another thread fails with its error, and changes nothing. This
// is the rule of test builds, where the first caller of pp_init stands in for the main thread;
// test_main_thread.c checks the rule that ships.
static void wrong_thread(void) {
    pp_start_host(NULL);
    pp_join_thread(pp_start_thread(call_from_other_thread, NULL));
    PP_CHECK_TEXT(pp_last_error(), "");
    PP_CHECK_EQUAL(pp_set_event_callback(ignore_event, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// What replaced_callback_finished watches.
typedef struct replacement_t {
    _Atomic(bool) first_running;
    _Atomic(bool) release_first;
    _Atomic(uint64_t) first_calls;
    _Atomic(uint64_t) second_calls;
} replacement_t;

static void first_callback(const pp_event_t *event, void *user_data) {
    replacement_t *replacement = user_data;
    (void)event;
    (void)atomic_fetch_add(&replacement->first_calls, 1U);
    atomic_store(&replacement->first_running, true);
    pp_wait_for_flag(&replacement->release_first);
    atomic_store(&replacement->first_running, false);
}

static void second_callback(const pp_event_t *event, void *user_data) {
    replacement_t *replacement = user_data;
    (void)event;
    (void)atomic_fetch_add(&replacement->second_calls, 1U);
}

static void *release_first_later(void *argument) {
    const pp_thread_argument_t *thread = argument;
    replacement_t *replacement = thread->context;
    pp_sleep_milliseconds(50);
    atomic_store(&replacement->release_first, true);
    return NULL;
}

// Once pp_set_event_callback returns, the callback it replaced is not running and is never
// called again.
static void replaced_callback_finished(void) {
    replacement_t replacement;
    atomic_init(&replacement.first_running, false);
    atomic_init(&replacement.release_first, false);
    atomic_init(&replacement.first_calls, 0U);
    atomic_init(&replacement.second_calls, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(first_callback, &replacement), PP_OK);
    PP_CHECK_EQUAL(pp_test_post_event(7U, 0), PP_OK);
    pp_wait_for_flag(&replacement.first_running);

    pthread_t releaser;
    pp_thread_argument_t argument;
    pp_start_threads(&releaser, &argument, 1U, release_first_later, &replacement);
    PP_CHECK_EQUAL(pp_set_event_callback(second_callback, &replacement), PP_OK);
    PP_CHECK(!atomic_load(&replacement.first_running));
    pp_join_thread(releaser);

    PP_CHECK_EQUAL(pp_test_post_event(7U, 1), PP_OK);
    pp_wait_for_count(&replacement.second_calls, 1U);
    PP_CHECK_EQUAL(atomic_load(&replacement.first_calls), 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// What the callbacks of lines_before_an_event_arrive_first saw.
typedef struct ordered_t {
    _Atomic(uint64_t) lines;
    _Atomic(uint64_t) lines_at_event;
    _Atomic(uint64_t) events;
} ordered_t;

static void count_ordered_line(const pp_log_line_t *line, void *user_data) {
    ordered_t *ordered = user_data;
    if (strncmp(line->text, "ordered ", 8U) == 0) {
        (void)atomic_fetch_add(&ordered->lines, 1U);
    }
}

static void count_ordered_event(const pp_event_t *event, void *user_data) {
    ordered_t *ordered = user_data;
    if (event->kind == 9U) {
        atomic_store(&ordered->lines_at_event, atomic_load(&ordered->lines));
        (void)atomic_fetch_add(&ordered->events, 1U);
    }
}

// Lines posted before an event arrive before it, also while the event thread is busy: a Windows
// program's output comes before its process-exited event (paneport/session.h). Each round
// posts its lines and its event from one thread, and the event must find them all delivered.
static void lines_before_an_event_arrive_first(void) {
    ordered_t ordered;
    atomic_init(&ordered.lines, 0U);
    atomic_init(&ordered.lines_at_event, 0U);
    atomic_init(&ordered.events, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(count_ordered_line, &ordered), PP_OK);
    PP_CHECK_EQUAL(pp_set_event_callback(count_ordered_event, &ordered), PP_OK);
    for (uint64_t round = 1U; round <= 200U; round++) {
        for (uint32_t line = 0U; line < 10U; line++) {
            pp_status_t status = pp_test_log(PP_LOG_INFO, "ordered line");
            while (status == PP_E_BUSY) {
                (void)sched_yield();
                status = pp_test_log(PP_LOG_INFO, "ordered line");
            }
            PP_CHECK_EQUAL(status, PP_OK);
        }
        post_event(9U, (int64_t)round);
        pp_wait_for_count(&ordered.events, round);
        PP_CHECK_EQUAL(atomic_load(&ordered.lines_at_event), round * 10U);
    }
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// In a quiet host an event that follows a line, as a process's exit follows its last line, does
// not wait: the line lies below the log queue's tail that the event's post read, and no slot
// below it is claimed and unpublished, so the drain delivers the line and then the event, and its
// bounded wait for an unpublished line never begins, which the count of the test builds shows.
static void event_after_a_line_does_not_wait(void) {
    ordered_t ordered;
    atomic_init(&ordered.lines, 0U);
    atomic_init(&ordered.lines_at_event, 0U);
    atomic_init(&ordered.events, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(count_ordered_line, &ordered), PP_OK);
    PP_CHECK_EQUAL(pp_set_event_callback(count_ordered_event, &ordered), PP_OK);
    for (uint64_t round = 1U; round <= 20U; round++) {
        PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "ordered line"), PP_OK);
        PP_CHECK_EQUAL(pp_test_post_event(9U, (int64_t)round), PP_OK);
        pp_wait_for_count(&ordered.events, round);
        PP_CHECK_EQUAL(atomic_load(&ordered.lines_at_event), round);
        PP_CHECK_EQUAL(pp_event_thread_line_waits(), 0U);
    }
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static uint64_t now_milliseconds(void) {
    struct timespec now;
    (void)clock_gettime(CLOCK_MONOTONIC, &now);
    return ((uint64_t)now.tv_sec * 1000U) + ((uint64_t)now.tv_nsec / 1000000U);
}

// Waits up to milliseconds for a counter to reach a value, and returns whether it did.
static bool wait_for_count_within(_Atomic(uint64_t) *counter, uint64_t expected,
                                  uint64_t milliseconds) {
    const uint64_t until = now_milliseconds() + milliseconds;
    while (atomic_load(counter) < expected && now_milliseconds() < until) {
        pp_sleep_milliseconds(1);
    }
    return atomic_load(counter) >= expected;
}

// What the flood cases watch. A producer posts lines as the reader of a program's output does,
// waiting for room, until stop is set, and the log callback takes 20 us a line, so the log queue
// stays full while the producer runs.
typedef struct flood_t {
    _Atomic(bool) stop;
    _Atomic(uint64_t) posted;
    _Atomic(uint64_t) events;
} flood_t;

static void slow_line(const pp_log_line_t *line, void *user_data) {
    (void)line;
    (void)user_data;
    const struct timespec pause = {.tv_sec = 0, .tv_nsec = 20000L};
    (void)nanosleep(&pause, NULL);
}

static void count_flood_event(const pp_event_t *event, void *user_data) {
    flood_t *flood = user_data;
    if (event->kind == 21U) {
        (void)atomic_fetch_add(&flood->events, 1U);
    }
}

static void *flood_lines(void *argument) {
    const pp_thread_argument_t *thread = argument;
    flood_t *flood = thread->context;
    static const char k_text[] = "a line of the flood";
    pp_log_entry_t entry;
    pp_log_text(&entry, PP_LOG_INFO, PP_LOG_SOURCE_GUEST_OUTPUT, 1U, k_text, sizeof k_text - 1U);
    while (!atomic_load(&flood->stop)) {
        PP_CHECK_EQUAL(pp_event_thread_post_log_waiting(&entry), PP_OK);
        (void)atomic_fetch_add(&flood->posted, 1U);
    }
    return NULL;
}

// Starts the host and the producer, and returns once the log queue is full.
static pthread_t start_flood(flood_t *flood, pp_thread_argument_t *argument) {
    atomic_init(&flood->stop, false);
    atomic_init(&flood->posted, 0U);
    atomic_init(&flood->events, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(slow_line, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_set_event_callback(count_flood_event, flood), PP_OK);
    pthread_t thread;
    pp_start_threads(&thread, argument, 1U, flood_lines, flood);
    // The queue holds 512 lines, which the callback takes about 10 ms for.
    PP_CHECK(wait_for_count_within(&flood->posted, 2000U, 60000U));
    return thread;
}

static void stop_flood(flood_t *flood, pthread_t thread) {
    atomic_store(&flood->stop, true);
    pp_join_thread(thread);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// An event posted while a producer floods the log queue arrives while the flood goes on, once
// the lines posted before it have: the lines posted after it do not hold it back.
static void event_during_a_flood_arrives_soon(void) {
    flood_t flood;
    pp_thread_argument_t argument;
    const pthread_t thread = start_flood(&flood, &argument);
    const uint64_t posted = now_milliseconds();
    PP_CHECK_EQUAL(pp_test_post_event(21U, 0), PP_OK);
    PP_CHECK(wait_for_count_within(&flood.events, 1U, 10000U));
    PP_CHECK(now_milliseconds() - posted < 5000U);
    stop_flood(&flood, thread);
}

// Events posted every 2 ms during a flood, five times as fast as the 512 lines of a full queue
// arrive, are all taken and all arrive while the flood goes on: none waits for the lines posted
// after it, so the event queue never fills. An event that waited for a full queue each would
// fill it within the 600.
static void no_event_dropped_during_a_flood(void) {
    flood_t flood;
    pp_thread_argument_t argument;
    const pthread_t thread = start_flood(&flood, &argument);
    for (int64_t index = 0; index < 600; index++) {
        PP_CHECK_EQUAL(pp_test_post_event(21U, index), PP_OK);
        pp_sleep_milliseconds(2);
    }
    PP_CHECK(wait_for_count_within(&flood.events, 600U, 20000U));
    stop_flood(&flood, thread);
}

// What the cases of a held producer watch. The push hook holds the first push after armed is
// set between its claim and its publication, for hold_milliseconds, or until release when that
// is 0. The callbacks number the held line, the later line and the first event of kind 31 in
// the order they arrive.
typedef struct held_push_t {
    _Atomic(bool) armed;
    _Atomic(bool) in_hook;
    _Atomic(bool) release;
    long hold_milliseconds;
    _Atomic(int) held_status;
    _Atomic(uint64_t) arrivals;
    _Atomic(uint64_t) held_line;
    _Atomic(uint64_t) later_line;
    _Atomic(uint64_t) first_event;
    _Atomic(uint64_t) events;
} held_push_t;

static void hold_armed_push(void *argument) {
    held_push_t *held = argument;
    if (!atomic_exchange(&held->armed, false)) {
        return;
    }
    atomic_store(&held->in_hook, true);
    if (held->hold_milliseconds > 0) {
        pp_sleep_milliseconds(held->hold_milliseconds);
    } else {
        pp_wait_for_flag(&held->release);
    }
}

static void number_held_line(const pp_log_line_t *line, void *user_data) {
    held_push_t *held = user_data;
    if (strcmp(line->text, "held line") == 0) {
        atomic_store(&held->held_line, atomic_fetch_add(&held->arrivals, 1U) + 1U);
    } else if (strcmp(line->text, "later line") == 0) {
        atomic_store(&held->later_line, atomic_fetch_add(&held->arrivals, 1U) + 1U);
    }
}

static void number_held_event(const pp_event_t *event, void *user_data) {
    held_push_t *held = user_data;
    if (event->kind == 31U) {
        if (atomic_fetch_add(&held->events, 1U) == 0U) {
            atomic_store(&held->first_event, atomic_fetch_add(&held->arrivals, 1U) + 1U);
        }
    }
}

static void *post_held_line(void *argument) {
    const pp_thread_argument_t *thread = argument;
    held_push_t *held = thread->context;
    atomic_store(&held->held_status, pp_test_log(PP_LOG_INFO, "held line"));
    return NULL;
}

// Starts the host and a producer whose line the hook holds inside its push, then posts a later
// line and an event from this thread, both behind the held slot.
static pthread_t start_held_push(held_push_t *held, pp_thread_argument_t *argument,
                                 long hold_milliseconds) {
    atomic_init(&held->armed, false);
    atomic_init(&held->in_hook, false);
    atomic_init(&held->release, false);
    held->hold_milliseconds = hold_milliseconds;
    atomic_init(&held->held_status, 1);
    atomic_init(&held->arrivals, 0U);
    atomic_init(&held->held_line, 0U);
    atomic_init(&held->later_line, 0U);
    atomic_init(&held->first_event, 0U);
    atomic_init(&held->events, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(number_held_line, held), PP_OK);
    PP_CHECK_EQUAL(pp_set_event_callback(number_held_event, held), PP_OK);
    pp_queue_set_push_hook(hold_armed_push, held);
    atomic_store(&held->armed, true);
    pthread_t thread;
    pp_start_threads(&thread, argument, 1U, post_held_line, held);
    pp_wait_for_flag(&held->in_hook);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "later line"), PP_OK);
    PP_CHECK_EQUAL(pp_test_post_event(31U, 0), PP_OK);
    return thread;
}

static void finish_held_push(held_push_t *held, pthread_t thread) {
    pp_join_thread(thread);
    PP_CHECK_EQUAL(atomic_load(&held->held_status), PP_OK);
    pp_queue_set_push_hook(NULL, NULL);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A producer held for a moment between its claim and its publication, as a preempted one is,
// holds the event back: its line, and a line posted after it but before the event, both arrive
// before the event.
static void held_producer_keeps_the_order(void) {
    held_push_t held;
    pp_thread_argument_t argument;
    const pthread_t thread = start_held_push(&held, &argument, 10);
    pp_wait_for_count(&held.events, 1U);
    PP_CHECK_EQUAL(atomic_load(&held.held_line), 1U);
    PP_CHECK_EQUAL(atomic_load(&held.later_line), 2U);
    PP_CHECK_EQUAL(atomic_load(&held.first_event), 3U);
    finish_held_push(&held, thread);
}

// A producer that stays inside its push, as a suspended guest thread can, holds the event back
// for a bounded time only, and the same slot never holds a later event: 20 more events arrive
// within a second, where a wait for each would take two. Once the producer goes on, both lines
// arrive, in their order.
static void held_producer_holds_an_event_once(void) {
    held_push_t held;
    pp_thread_argument_t argument;
    const pthread_t thread = start_held_push(&held, &argument, 0);
    PP_CHECK(wait_for_count_within(&held.events, 1U, 10000U));
    PP_CHECK_EQUAL(atomic_load(&held.first_event), 1U);
    PP_CHECK_EQUAL(atomic_load(&held.held_line), 0U);
    PP_CHECK_EQUAL(atomic_load(&held.later_line), 0U);
    const uint64_t posted = now_milliseconds();
    for (int64_t index = 1; index <= 20; index++) {
        PP_CHECK_EQUAL(pp_test_post_event(31U, index), PP_OK);
    }
    PP_CHECK(wait_for_count_within(&held.events, 21U, 10000U));
    PP_CHECK(now_milliseconds() - posted < 1000U);
    PP_CHECK_EQUAL(atomic_load(&held.held_line), 0U);

    atomic_store(&held.release, true);
    pp_wait_for_count(&held.arrivals, 3U);
    PP_CHECK_EQUAL(atomic_load(&held.held_line), 2U);
    PP_CHECK_EQUAL(atomic_load(&held.later_line), 3U);
    finish_held_push(&held, thread);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"lines_before_an_event_arrive_first", lines_before_an_event_arrive_first},
        {"event_after_a_line_does_not_wait", event_after_a_line_does_not_wait},
        {"event_during_a_flood_arrives_soon", event_during_a_flood_arrives_soon},
        {"no_event_dropped_during_a_flood", no_event_dropped_during_a_flood},
        {"held_producer_keeps_the_order", held_producer_keeps_the_order},
        {"held_producer_holds_an_event_once", held_producer_holds_an_event_once},
        {"events_reach_event_thread", events_reach_event_thread},
        {"none_after_shutdown", none_after_shutdown},
        {"post_in_progress_is_delivered", post_in_progress_is_delivered},
        {"event_overflow_logged_once", event_overflow_logged_once},
        {"second_init_fails", second_init_fails},
        {"wrong_thread", wrong_thread},
        {"replaced_callback_finished", replaced_callback_finished},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
