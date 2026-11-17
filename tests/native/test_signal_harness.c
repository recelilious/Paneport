// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.6: the native part of the signal gate's stress harness, the runner that the device page
// uses, here with the stand-in for the runtime's handler. For 60 seconds, marked threads fault
// on their pages, unmarked threads fault in the reservation, and a thread that stands for the
// managed null-reference loop faults in the first page, as a null reference does, where the
// stand-in resolves it and no record is due. Every planted fault must be handled on its route,
// and none may be unexpected (docs/design/signals.md section 9).

#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <time.h>

#include "paneport/host.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "signal_test_support.h"
#include "test_support.h"

#define RUN_SECONDS 60

// The documented singleton of this program (C-0010 section 3): the stand-in, which the
// runtime's handler reaches without an argument.
static pp_stand_in_t s_stand_in;

static void stand_in_handler(int signal_number, siginfo_t *info, void *context) {
    pp_stand_in_handle(&s_stand_in, signal_number, info, context);
}

typedef struct runtime_loop_t {
    _Atomic(bool) stop;
    _Atomic(uint64_t) faults;
} runtime_loop_t;

// Stands for the managed null-reference loop: loads through a null reference, at a small
// address, which the runtime resolves. Like the device's, they lie below Mono's 4 KiB limit, so
// the dispatcher hands them on without writing a record first (signals.md section 5).
static void *fault_for_runtime(void *argument) {
    runtime_loop_t *loop = argument;
    while (!atomic_load(&loop->stop)) {
        (void)atomic_fetch_add(&loop->faults, 1U);
        pp_signal_plant_read(pp_test_address(16U));
    }
    return NULL;
}

static void read_counters(pp_test_signal_counters_t *counters) {
    memset(counters, 0, sizeof *counters);
    counters->struct_size = (uint32_t)sizeof *counters;
    PP_CHECK_EQUAL(pp_test_signal_harness_read(counters), PP_OK);
    PP_CHECK_EQUAL(counters->struct_size, sizeof(pp_test_signal_counters_t));
}

static void runs_sixty_seconds(void) {
    runtime_loop_t loop;
    memset(&loop, 0, sizeof loop);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_stand_in_own(&s_stand_in, NULL, 4096U);
    pp_start_host(NULL);

    const pp_test_signal_harness_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_test_signal_harness_config_t),
        .marked_threads = 2U,
        .unmarked_threads = 2U,
        .pause_microseconds = 0U,
    };
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_OK);
    const pthread_t runtime = pp_start_thread(fault_for_runtime, &loop);

    pp_test_signal_counters_t counters;
    for (int second = 0; second < RUN_SECONDS; second++) {
        pp_sleep_milliseconds(1000);
        read_counters(&counters);
        PP_CHECK_EQUAL(counters.running, 1U);
        PP_CHECK_EQUAL(counters.unexpected_faults, 0U);
        PP_CHECK_EQUAL(pp_test_signals_check_installed(NULL), PP_OK);
    }

    atomic_store(&loop.stop, true);
    pp_join_thread(runtime);
    PP_CHECK_EQUAL(pp_test_signal_harness_stop(), PP_OK);
    read_counters(&counters);
    (void)printf("by mark %llu, by address %llu, runtime %llu\n",
                 (unsigned long long)counters.guest_faults_handled_by_mark,
                 (unsigned long long)counters.guest_faults_handled_by_address,
                 (unsigned long long)counters.runtime_faults);
    PP_CHECK_EQUAL(counters.running, 0U);
    PP_CHECK(counters.guest_faults_raised_by_mark > 0U);
    PP_CHECK_EQUAL(counters.guest_faults_handled_by_mark, counters.guest_faults_raised_by_mark);
    PP_CHECK(counters.guest_faults_raised_by_address > 0U);
    PP_CHECK_EQUAL(counters.guest_faults_handled_by_address,
                   counters.guest_faults_raised_by_address);
    PP_CHECK_EQUAL(counters.unexpected_faults, 0U);
    PP_CHECK_EQUAL(counters.runtime_faults, atomic_load(&loop.faults));
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGSEGV), atomic_load(&loop.faults));
    PP_CHECK_EQUAL(pp_test_signals_check_installed(NULL), PP_OK);
}

static uint32_t other_handler(pp_guest_fault_t *fault, void *user_data) {
    (void)fault;
    (void)user_data;
    return PP_GUEST_FAULT_DECLINED;
}

static void start_refused_before_init(void) {
    const pp_test_signal_harness_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_test_signal_harness_config_t),
        .marked_threads = 1U,
        .unmarked_threads = 1U,
        .pause_microseconds = 1000U,
    };
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_test_signal_harness_stop(), PP_OK);
}

// Bad configurations, a second start and a start while another guest fault handler is set are
// refused; stopping twice is fine.
static void start_checks_its_conditions(void) {
    pp_start_host(NULL);
    pp_test_signal_harness_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_test_signal_harness_config_t),
        .marked_threads = 1U,
        .unmarked_threads = 1U,
        .pause_microseconds = 1000U,
    };
    PP_CHECK_EQUAL(pp_test_signal_harness_start(NULL), PP_E_INVALID_ARGUMENT);
    config.struct_size = 3U;
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_E_INVALID_ARGUMENT);
    config.struct_size = (uint32_t)sizeof(pp_test_signal_harness_config_t);
    config.marked_threads = 17U;
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_E_INVALID_ARGUMENT);
    config.marked_threads = 1U;
    config.pause_microseconds = 1000001U;
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_E_INVALID_ARGUMENT);
    config.pause_microseconds = 1000U;
    PP_CHECK_EQUAL(pp_test_signal_harness_read(NULL), PP_E_INVALID_ARGUMENT);

    PP_CHECK_EQUAL(pp_signals_set_guest_handler(other_handler, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_E_BUSY);
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(NULL, NULL), PP_OK);

    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_OK);
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_E_BUSY);
    pp_sleep_milliseconds(100);
    PP_CHECK_EQUAL(pp_test_signal_harness_stop(), PP_OK);
    PP_CHECK_EQUAL(pp_test_signal_harness_stop(), PP_OK);
    PP_CHECK(!pp_signals_has_guest_handler());

    pp_test_signal_counters_t counters;
    read_counters(&counters);
    PP_CHECK_EQUAL(counters.running, 0U);
    PP_CHECK_EQUAL(counters.guest_faults_handled_by_mark, counters.guest_faults_raised_by_mark);
    PP_CHECK_EQUAL(counters.guest_faults_handled_by_address,
                   counters.guest_faults_raised_by_address);
}

// A caller that knows fewer members passes a smaller struct_size: the host reads and writes only
// the members inside it, keeps the caller's struct_size, and gives a configuration's other
// members their defaults (docs/design/native-host.md section 2).
static void short_structs_follow_struct_size(void) {
    pp_start_host(NULL);
    pp_test_signal_counters_t counters;
    memset(&counters, 0xA5, sizeof counters);
    counters.struct_size = 3U;
    PP_CHECK_EQUAL(pp_test_signal_harness_read(&counters), PP_E_INVALID_ARGUMENT);
    counters.struct_size = 16U;
    PP_CHECK_EQUAL(pp_test_signal_harness_read(&counters), PP_OK);
    PP_CHECK_EQUAL(counters.struct_size, 16U);
    PP_CHECK_EQUAL(counters.running, 0U);
    PP_CHECK_EQUAL(counters.guest_faults_raised_by_mark, 0U);
    const unsigned char *bytes = (const unsigned char *)&counters;
    for (size_t index = 16U; index < sizeof counters; index++) {
        PP_CHECK_EQUAL(bytes[index], 0xA5U);
    }

    // Eight bytes carry the marked threads; the unmarked threads and the pause, whose value
    // here would be refused, lie past the struct_size and take their defaults.
    const pp_test_signal_harness_config_t config = {
        .struct_size = 8U,
        .marked_threads = 2U,
        .unmarked_threads = 99U,
        .pause_microseconds = 5000000U,
    };
    PP_CHECK_EQUAL(pp_test_signal_harness_start(&config), PP_OK);
    pp_sleep_milliseconds(200);
    PP_CHECK_EQUAL(pp_test_signal_harness_stop(), PP_OK);
    read_counters(&counters);
    PP_CHECK(counters.guest_faults_raised_by_mark > 0U);
    PP_CHECK(counters.guest_faults_raised_by_address > 0U);
    PP_CHECK_EQUAL(counters.unexpected_faults, 0U);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"runs_sixty_seconds", runs_sixty_seconds},
        {"start_refused_before_init", start_refused_before_init},
        {"start_checks_its_conditions", start_checks_its_conditions},
        {"short_structs_follow_struct_size", short_structs_follow_struct_size},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
