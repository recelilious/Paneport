// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.4: the Mach thread control helpers on arm64. A suspended thread has one register
// changed and, once resumed, sees the new value; a thread that has ended gives an error; a
// thread cannot suspend itself (docs/design/signals.md section 8).

#include <pthread.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "paneport/host.h"
#include "pp_check.h"
#include "signals/thread_control.h"
#include "test_support.h"

#define NEW_VALUE UINT64_C(0x5EED1234ABCD)

typedef struct spin_test_t {
    _Atomic(uint32_t) spinning;
    uint64_t result;
} spin_test_t;

// Sets x9 to 0, tells the test that it spins, spins until x9 is no longer 0, and returns x9.
// Only another thread can end the loop, by writing the register.
static void *spin_on_register(void *argument) {
    spin_test_t *test = argument;
    uint64_t value = 0U;
    __asm__ volatile("mov x9, #0\n"
                     "mov w10, #1\n"
                     "stlr w10, [%1]\n"
                     "1:\n"
                     "cbz x9, 1b\n"
                     "mov %0, x9\n"
                     : "=r"(value)
                     : "r"(&test->spinning)
                     : "x9", "x10", "memory");
    test->result = value;
    return NULL;
}

static void register_change_seen_after_resume(void) {
    spin_test_t test;
    memset(&test, 0, sizeof test);
    const pthread_t thread = pp_start_thread(spin_on_register, &test);
    for (int round = 0; round < 60000 && atomic_load(&test.spinning) == 0U; round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK_EQUAL(atomic_load(&test.spinning), 1U);

    uint32_t port = 0U;
    PP_CHECK_EQUAL(pp_thread_control_open(thread, &port), PP_OK);
    PP_CHECK_EQUAL(pp_thread_control_suspend(port), PP_OK);
    pp_thread_state_t state;
    memset(&state, 0, sizeof state);
    PP_CHECK_EQUAL(pp_thread_control_get_state(port, &state), PP_OK);
    PP_CHECK_EQUAL(state.struct_size, sizeof(pp_thread_state_t));
    PP_CHECK_EQUAL(state.x[9], 0U);
    PP_CHECK(state.pc != 0U && state.sp != 0U);

    state.x[9] = NEW_VALUE;
    PP_CHECK_EQUAL(pp_thread_control_set_state(port, &state), PP_OK);
    pp_thread_state_t again;
    PP_CHECK_EQUAL(pp_thread_control_get_state(port, &again), PP_OK);
    PP_CHECK_EQUAL(again.x[9], NEW_VALUE);
    PP_CHECK_EQUAL(again.pc, state.pc);
    PP_CHECK_EQUAL(again.sp, state.sp);

    PP_CHECK_EQUAL(pp_thread_control_resume(port), PP_OK);
    pp_join_thread(thread);
    PP_CHECK_EQUAL(test.result, NEW_VALUE);
    PP_CHECK_EQUAL(pp_thread_control_close(port), PP_OK);
}

static void *wait_then_end(void *argument) {
    pp_wait_for_flag(argument);
    return NULL;
}

static void check_ended(pp_status_t status) {
    if (status != PP_E_INVALID_ARGUMENT) {
        (void)printf("found %d: %s\n", (int)status, pp_last_error());
    }
    PP_CHECK_EQUAL(status, PP_E_INVALID_ARGUMENT);
}

// The port of a thread that has ended stays a valid name, and every call on it fails.
static void ended_thread_fails(void) {
    _Atomic(bool) end = false;
    const pthread_t thread = pp_start_thread(wait_then_end, &end);
    uint32_t port = 0U;
    PP_CHECK_EQUAL(pp_thread_control_open(thread, &port), PP_OK);
    atomic_store(&end, true);
    pp_join_thread(thread);

    check_ended(pp_thread_control_suspend(port));
    pp_thread_state_t state;
    memset(&state, 0, sizeof state);
    check_ended(pp_thread_control_get_state(port, &state));
    state.struct_size = (uint32_t)sizeof state;
    check_ended(pp_thread_control_set_state(port, &state));
    check_ended(pp_thread_control_resume(port));
    PP_CHECK_EQUAL(pp_thread_control_close(port), PP_OK);
}

static void suspending_itself_refused(void) {
    uint32_t port = 0U;
    PP_CHECK_EQUAL(pp_thread_control_open(pthread_self(), &port), PP_OK);
    PP_CHECK_EQUAL(pp_thread_control_suspend(port), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_thread_control_get_state(port, NULL), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_thread_control_set_state(port, NULL), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_thread_control_open(pthread_self(), NULL), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_thread_control_close(port), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"register_change_seen_after_resume", register_change_seen_after_resume},
        {"ended_thread_fails", ended_thread_fails},
        {"suspending_itself_refused", suspending_itself_refused},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
