// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.3: the event thread, the callbacks, pp_init and pp_shutdown.

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdint.h>
#include <string.h>

#include "event_thread.h"
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

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
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
