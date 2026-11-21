// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.4: the Mach thread control helpers on arm64. A suspended thread has one register
// changed and, once resumed, sees the new value; a thread that has ended gives an error, from
// its join until after its port has died, and so does a request that a port's death destroys;
// a thread cannot suspend itself (docs/design/signals.md section 8).

#include <mach/mach.h>
#include <mach/mig_errors.h>
#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

#include "paneport/host.h"
#include "pp_check.h"
#include "signals/thread_control.h"
#include "test_support.h"

#define NEW_VALUE UINT64_C(0x5EED1234ABCD)

// The threads that ended_thread_fails ends, one after another.
#define ENDING_ROUNDS 500U

// The requests that destroyed_request_fails_as_ended sends: suspend, resume, get_state, and
// the read that set_state makes before it writes.
#define DESTROYED_REQUESTS 4U

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

// The Mach result of the last failed call, which its message gives in parentheses at the end.
static long long reported_result(void) {
    const char *parenthesis = strrchr(pp_last_error(), '(');
    PP_CHECK(parenthesis != NULL);
    char *end = NULL;
    const long long result = strtoll(parenthesis + 1, &end, 10);
    PP_CHECK(end != parenthesis + 1 && *end == ')');
    return result;
}

// Checks that a call failed as an ended thread's, and that the Mach result was the expected one.
static void check_ended_by(pp_status_t status, long long expected) {
    check_ended(status);
    PP_CHECK_EQUAL(reported_result(), expected);
}

static int64_t elapsed_milliseconds(const struct timespec *start) {
    struct timespec now;
    PP_CHECK_EQUAL(clock_gettime(CLOCK_MONOTONIC, &now), 0);
    return (((int64_t)now.tv_sec - (int64_t)start->tv_sec) * 1000) +
           (((int64_t)now.tv_nsec - (int64_t)start->tv_nsec) / 1000000);
}

// Whether the port behind the name has been destroyed, which leaves a dead name.
static bool port_died(uint32_t port) {
    mach_port_type_t type = 0U;
    PP_CHECK_EQUAL(mach_port_type(mach_task_self(), (mach_port_name_t)port, &type), KERN_SUCCESS);
    return (type & MACH_PORT_TYPE_DEAD_NAME) != 0U;
}

// What the calls on threads that were ending found, by the Mach result.
typedef struct ending_counts_t {
    uint32_t running;    // KERN_SUCCESS: the thread had not yet left libpthread
    uint32_t terminated; // KERN_TERMINATED
    uint32_t no_thread;  // KERN_INVALID_ARGUMENT: the port no longer leads to the thread
    uint32_t port_death; // MIG_SERVER_DIED: the port died while the request was on its way
    uint32_t dead_name;  // MACH_SEND_INVALID_DEST
    uint32_t other;
} ending_counts_t;

static void count_ended(ending_counts_t *counts, long long result) {
    switch (result) {
    case KERN_TERMINATED:
        counts->terminated++;
        break;
    case KERN_INVALID_ARGUMENT:
        counts->no_thread++;
        break;
    case MIG_SERVER_DIED:
        counts->port_death++;
        break;
    case MACH_SEND_INVALID_DEST:
        counts->dead_name++;
        break;
    default:
        counts->other++;
        break;
    }
}

// Suspends the thread behind the port again and again until the port dies. pthread_join can
// return before libpthread hands the thread to the kernel to end, so a call may still find it
// running, and then resumes it at once; every other call must fail as an ended thread's,
// whichever step of the kernel's teardown it meets.
static void call_until_port_dies(uint32_t port, ending_counts_t *counts) {
    struct timespec start;
    PP_CHECK_EQUAL(clock_gettime(CLOCK_MONOTONIC, &start), 0);
    while (!port_died(port)) {
        PP_CHECK(elapsed_milliseconds(&start) < 60000);
        const pp_status_t status = pp_thread_control_suspend(port);
        if (status == PP_OK) {
            counts->running++;
            const pp_status_t resumed = pp_thread_control_resume(port);
            if (resumed != PP_OK) {
                check_ended(resumed);
            }
            (void)sched_yield();
        } else {
            check_ended(status);
            count_ended(counts, reported_result());
        }
    }
}

// The port of a thread that has ended stays a valid name, and every call on it fails: from the
// thread's join, through the kernel's teardown of the thread, to the port's death and after
// it, when the name is a dead name. The rounds give the calls many chances to meet the port's
// death in the middle of a request, as one CI run did (docs/design/signals.md section 8).
static void ended_thread_fails(void) {
    ending_counts_t counts;
    memset(&counts, 0, sizeof counts);
    for (uint32_t round = 0U; round < ENDING_ROUNDS; round++) {
        _Atomic(bool) end = false;
        const pthread_t thread = pp_start_thread(wait_then_end, &end);
        uint32_t port = 0U;
        PP_CHECK_EQUAL(pp_thread_control_open(thread, &port), PP_OK);
        atomic_store(&end, true);
        pp_join_thread(thread);
        call_until_port_dies(port, &counts);

        check_ended_by(pp_thread_control_suspend(port), MACH_SEND_INVALID_DEST);
        pp_thread_state_t state;
        memset(&state, 0, sizeof state);
        check_ended_by(pp_thread_control_get_state(port, &state), MACH_SEND_INVALID_DEST);
        state.struct_size = (uint32_t)sizeof state;
        check_ended_by(pp_thread_control_set_state(port, &state), MACH_SEND_INVALID_DEST);
        check_ended_by(pp_thread_control_resume(port), MACH_SEND_INVALID_DEST);
        PP_CHECK_EQUAL(pp_thread_control_close(port), PP_OK);
    }
    (void)printf("%u rounds; calls that found the thread running: %u, terminated: %u, without "
                 "its thread: %u, at the port's death: %u, on a dead name: %u, other: %u\n",
                 ENDING_ROUNDS, counts.running, counts.terminated, counts.no_thread,
                 counts.port_death, counts.dead_name, counts.other);
}

// A port of the test's own stands in for a thread's port that dies while a request is on its
// way. Its server receives each request and destroys it unanswered, and the request's reply
// right goes with it, so the kernel sends the caller's reply port a send-once notification,
// as it does when a request finds its port dead; MIG's stub then reports MIG_SERVER_DIED.
typedef struct destroying_server_t {
    mach_port_t port;
    _Atomic(uint32_t) destroyed;
} destroying_server_t;

typedef struct received_request_t {
    mach_msg_header_t header;
    uint8_t rest[1024];
} received_request_t;

static void *destroy_requests(void *argument) {
    destroying_server_t *server = argument;
    // The receive gives up after a minute, so that a request that never comes fails the test.
    const mach_msg_option_t options =
        (mach_msg_option_t)((unsigned)MACH_RCV_MSG | (unsigned)MACH_RCV_TIMEOUT);
    for (uint32_t index = 0U; index < DESTROYED_REQUESTS; index++) {
        received_request_t request;
        memset(&request, 0, sizeof request);
        PP_CHECK_EQUAL(mach_msg(&request.header, options, 0U, (mach_msg_size_t)sizeof request,
                                server->port, 60000U, MACH_PORT_NULL),
                       MACH_MSG_SUCCESS);
        mach_msg_destroy(&request.header);
        (void)atomic_fetch_add(&server->destroyed, 1U);
    }
    return NULL;
}

// Every call that sends a request reports a request destroyed unanswered as an ended thread.
static void destroyed_request_fails_as_ended(void) {
    const mach_port_t task = mach_task_self();
    destroying_server_t server;
    memset(&server, 0, sizeof server);
    PP_CHECK_EQUAL(mach_port_allocate(task, MACH_PORT_RIGHT_RECEIVE, &server.port), KERN_SUCCESS);
    PP_CHECK_EQUAL(mach_port_insert_right(task, server.port, server.port, MACH_MSG_TYPE_MAKE_SEND),
                   KERN_SUCCESS);
    const pthread_t thread = pp_start_thread(destroy_requests, &server);

    const uint32_t port = (uint32_t)server.port;
    check_ended_by(pp_thread_control_suspend(port), MIG_SERVER_DIED);
    check_ended_by(pp_thread_control_resume(port), MIG_SERVER_DIED);
    pp_thread_state_t state;
    memset(&state, 0, sizeof state);
    check_ended_by(pp_thread_control_get_state(port, &state), MIG_SERVER_DIED);
    state.struct_size = (uint32_t)sizeof state;
    check_ended_by(pp_thread_control_set_state(port, &state), MIG_SERVER_DIED);

    pp_join_thread(thread);
    PP_CHECK_EQUAL(atomic_load(&server.destroyed), DESTROYED_REQUESTS);
    // Destroying the receive right leaves the send right as a dead name.
    PP_CHECK_EQUAL(mach_port_mod_refs(task, server.port, MACH_PORT_RIGHT_RECEIVE, -1),
                   KERN_SUCCESS);
    PP_CHECK_EQUAL(mach_port_deallocate(task, server.port), KERN_SUCCESS);
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
        {"destroyed_request_fails_as_ended", destroyed_request_fails_as_ended},
        {"suspending_itself_refused", suspending_itself_refused},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
