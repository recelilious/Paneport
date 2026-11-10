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

static int s_marker;
static _Atomic(uint64_t) s_received;
static uint32_t s_next[PRODUCERS]; // Touched by the event thread only.

static void count_event(const pp_event_t *event, void *user_data) {
    PP_CHECK(user_data == &s_marker);
    PP_CHECK(pp_event_thread_is_current());
    PP_CHECK_EQUAL(event->struct_size, sizeof(pp_event_t));
    PP_CHECK(event->kind < (uint32_t)PRODUCERS);
    PP_CHECK_EQUAL(event->code, s_next[event->kind]);
    s_next[event->kind]++;
    (void)atomic_fetch_add(&s_received, 1U);
}

static void *post_events(void *argument) {
    const uint32_t producer = pp_thread_number(argument);
    for (int64_t sequence = 0; sequence < EVENTS_PER_PRODUCER; sequence++) {
        pp_status_t status = pp_test_post_event(producer, sequence);
        while (status == PP_E_BUSY) {
            (void)sched_yield();
            status = pp_test_post_event(producer, sequence);
        }
        PP_CHECK_EQUAL(status, PP_OK);
    }
    return NULL;
}

// Events posted from many threads reach the callback on the event thread only, each once and
// in order per thread. The queue holds fewer events than each thread posts.
static void events_reach_event_thread(void) {
    pp_start_host(NULL);
    PP_CHECK(!pp_event_thread_is_current());
    PP_CHECK_EQUAL(pp_set_event_callback(count_event, &s_marker), PP_OK);

    pthread_t producers[PRODUCERS];
    for (uint32_t producer = 0; producer < (uint32_t)PRODUCERS; producer++) {
        producers[producer] = pp_start_numbered_thread(post_events, producer);
    }
    for (size_t producer = 0; producer < (size_t)PRODUCERS; producer++) {
        pp_join_thread(producers[producer]);
    }
    pp_wait_for_count(&s_received, (uint64_t)PRODUCERS * (uint64_t)EVENTS_PER_PRODUCER);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static _Atomic(bool) s_shutdown_returned;
static _Atomic(bool) s_stop_posting;
static _Atomic(uint64_t) s_late_calls;

static void watch_event(const pp_event_t *event, void *user_data) {
    (void)event;
    (void)user_data;
    if (atomic_load(&s_shutdown_returned)) {
        (void)atomic_fetch_add(&s_late_calls, 1U);
    }
}

static void watch_line(const pp_log_line_t *line, void *user_data) {
    (void)line;
    (void)user_data;
    if (atomic_load(&s_shutdown_returned)) {
        (void)atomic_fetch_add(&s_late_calls, 1U);
    }
}

static void *post_until_stopped(void *argument) {
    (void)argument;
    while (!atomic_load(&s_stop_posting)) {
        (void)pp_test_post_event(1U, 1);
        (void)pp_test_log(PP_LOG_DEBUG, "a line while the host stops");
    }
    return NULL;
}

// No callback runs once pp_shutdown has returned, while other threads keep posting.
static void none_after_shutdown(void) {
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(watch_event, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_set_log_callback(watch_line, NULL), PP_OK);

    pthread_t producers[4];
    for (size_t index = 0; index < 4U; index++) {
        producers[index] = pp_start_thread(post_until_stopped, NULL);
    }
    pp_sleep_milliseconds(20);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    atomic_store(&s_shutdown_returned, true);
    pp_sleep_milliseconds(20);
    atomic_store(&s_stop_posting, true);
    for (size_t index = 0; index < 4U; index++) {
        pp_join_thread(producers[index]);
    }

    PP_CHECK_EQUAL(atomic_load(&s_late_calls), 0U);
    PP_CHECK_EQUAL(pp_test_post_event(1U, 1), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "after shutdown"), PP_E_NOT_INITIALIZED);
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

static void *call_from_other_thread(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pp_set_event_callback(watch_event, NULL), PP_E_WRONG_THREAD);
    PP_CHECK(strstr(pp_last_error(), "main thread") != NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(watch_line, NULL), PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_WRONG_THREAD);
    PP_CHECK(strstr(pp_last_error(), "pp_shutdown") != NULL);
    const pp_init_config_t config = {.struct_size = (uint32_t)sizeof(pp_init_config_t)};
    PP_CHECK_EQUAL(pp_init(&config), PP_E_ALREADY_INITIALIZED);
    return NULL;
}

// A [main] function called on another thread fails with its error, and changes nothing.
static void wrong_thread(void) {
    pp_start_host(NULL);
    pp_join_thread(pp_start_thread(call_from_other_thread, NULL));
    PP_CHECK_TEXT(pp_last_error(), "");
    PP_CHECK_EQUAL(pp_set_event_callback(watch_event, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static _Atomic(bool) s_first_running;
static _Atomic(bool) s_release_first;
static _Atomic(uint64_t) s_first_calls;
static _Atomic(uint64_t) s_second_calls;

static void first_callback(const pp_event_t *event, void *user_data) {
    (void)event;
    (void)user_data;
    (void)atomic_fetch_add(&s_first_calls, 1U);
    atomic_store(&s_first_running, true);
    while (!atomic_load(&s_release_first)) {
        pp_sleep_milliseconds(1);
    }
    atomic_store(&s_first_running, false);
}

static void second_callback(const pp_event_t *event, void *user_data) {
    (void)event;
    (void)user_data;
    (void)atomic_fetch_add(&s_second_calls, 1U);
}

static void *release_first_later(void *argument) {
    (void)argument;
    pp_sleep_milliseconds(50);
    atomic_store(&s_release_first, true);
    return NULL;
}

// Once pp_set_event_callback returns, the callback it replaced is not running and is never
// called again.
static void replaced_callback_finished(void) {
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(first_callback, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_test_post_event(7U, 0), PP_OK);
    for (int round = 0; round < 60000 && !atomic_load(&s_first_running); round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK(atomic_load(&s_first_running));

    const pthread_t releaser = pp_start_thread(release_first_later, NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(second_callback, NULL), PP_OK);
    PP_CHECK(!atomic_load(&s_first_running));
    pp_join_thread(releaser);

    PP_CHECK_EQUAL(pp_test_post_event(7U, 1), PP_OK);
    pp_wait_for_count(&s_second_calls, 1U);
    PP_CHECK_EQUAL(atomic_load(&s_first_calls), 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"events_reach_event_thread", events_reach_event_thread},
        {"none_after_shutdown", none_after_shutdown},
        {"second_init_fails", second_init_fails},
        {"wrong_thread", wrong_thread},
        {"replaced_callback_finished", replaced_callback_finished},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
