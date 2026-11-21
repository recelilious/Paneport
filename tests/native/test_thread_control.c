// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.4: the Mach thread control helpers on arm64. A suspended thread has one register
// changed and, once resumed, sees the new value; each call on a thread that has ended gives an
// error, from the thread's join until after its port has died, and so does each call whose
// request is destroyed unanswered, as a port's death destroys one; a thread cannot suspend
// itself (docs/design/signals.md sections 8 and 10).

#include <crt_externs.h>
#include <errno.h>
#include <mach/mach.h>
#include <mach/mig_errors.h>
#include <pthread.h>
#include <sched.h>
#include <signal.h>
#include <spawn.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <time.h>
#include <unistd.h>

#include "paneport/host.h"
#include "pp_check.h"
#include "signals/thread_control.h"
#include "test_support.h"

#define NEW_VALUE UINT64_C(0x5EED1234ABCD)

// The threads that ended_thread_fails ends, one after another.
#define ENDING_ROUNDS 500U

// The message ID of the request that stops a destroying server. MIG's requests of thread_act
// have the IDs 3600 and up.
#define STOP_REQUEST_ID 0x53544F50

// The setting with which CI says that its machine has System Integrity Protection disabled.
#define SIP_DISABLED_SETTING "PANEPORT_EXPECT_SIP_DISABLED=1"

// The helpers that send a request on a thread's port, in the order that the cases call them.
enum { CALL_SUSPEND, CALL_GET_STATE, CALL_SET_STATE, CALL_RESUME, CALL_COUNT };

// The documented singleton of this program (C-0010 section 3): the path that the program
// started with, which a case needs to start a child.
static const char *s_program;

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

static const char *call_name(uint32_t call) {
    switch (call) {
    case CALL_SUSPEND:
        return "pp_thread_control_suspend";
    case CALL_GET_STATE:
        return "pp_thread_control_get_state";
    case CALL_SET_STATE:
        return "pp_thread_control_set_state";
    default:
        return "pp_thread_control_resume";
    }
}

// Makes one of the calls on a port. The state is empty: set_state sends its write only when
// the read before it succeeds, and the cases call it only where that read fails.
static pp_status_t make_call(uint32_t call, uint32_t port) {
    pp_thread_state_t state;
    memset(&state, 0, sizeof state);
    state.struct_size = (uint32_t)sizeof state;
    switch (call) {
    case CALL_SUSPEND:
        return pp_thread_control_suspend(port);
    case CALL_GET_STATE:
        return pp_thread_control_get_state(port, &state);
    case CALL_SET_STATE:
        return pp_thread_control_set_state(port, &state);
    default:
        return pp_thread_control_resume(port);
    }
}

// Checks that a call failed as an ended thread's; when it did not, prints the call and what it
// found.
static void check_ended(const char *call, pp_status_t status) {
    if (status == PP_OK) {
        (void)fprintf(stderr, "%s succeeded.\n", call);
    } else if (status != PP_E_INVALID_ARGUMENT) {
        (void)fprintf(stderr, "%s found %d: %s\n", call, (int)status, pp_last_error());
    }
    PP_CHECK_EQUAL(status, PP_E_INVALID_ARGUMENT);
}

// The Mach result of a call's failure, which its message gives in parentheses at the end.
static long long reported_result(const char *call) {
    const char *message = pp_last_error();
    const char *parenthesis = strrchr(message, '(');
    char *end = NULL;
    long long result = 0;
    if (parenthesis != NULL) {
        result = strtoll(parenthesis + 1, &end, 10);
    }
    if (end == NULL || end == parenthesis + 1 || *end != ')') {
        (void)fprintf(stderr, "%s gave no Mach result: %s\n", call, message);
        pp_check_failed(__FILE__, __LINE__, "the message ends with a Mach result");
    }
    return result;
}

// Checks that a call failed as an ended thread's with the expected Mach result; when it did
// not, prints the call and its message.
static void check_ended_by(const char *call, pp_status_t status, long long expected) {
    check_ended(call, status);
    const long long result = reported_result(call);
    if (result != expected) {
        (void)fprintf(stderr, "%s found %lld, not %lld: %s\n", call, result, expected,
                      pp_last_error());
    }
    PP_CHECK_EQUAL(result, expected);
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

// What the calls of one helper on threads that were ending found, by the Mach result.
typedef struct ending_counts_t {
    uint32_t running;    // KERN_SUCCESS: the thread had not yet left libpthread
    uint32_t terminated; // KERN_TERMINATED
    uint32_t no_thread;  // KERN_INVALID_ARGUMENT: the port no longer leads to the thread
    uint32_t port_death; // MIG_SERVER_DIED: the port died while the request was on its way
    uint32_t dead_name;  // MACH_SEND_INVALID_DEST
    uint32_t other;
} ending_counts_t;

// Checks that a call on a thread that is ending failed as an ended thread's, and counts its
// Mach result under the call.
static void count_ended(ending_counts_t *counts, uint32_t call, pp_status_t status) {
    const char *name = call_name(call);
    check_ended(name, status);
    ending_counts_t *count = &counts[call];
    switch (reported_result(name)) {
    case KERN_TERMINATED:
        count->terminated++;
        break;
    case KERN_INVALID_ARGUMENT:
        count->no_thread++;
        break;
    case MIG_SERVER_DIED:
        count->port_death++;
        break;
    case MACH_SEND_INVALID_DEST:
        count->dead_name++;
        break;
    default:
        count->other++;
        break;
    }
}

// Calls on the port of a thread that is ending until the port dies, a suspend first each
// time. pthread_join can return before libpthread hands the thread to the kernel to end, so a
// suspend may still find the thread running, which is then resumed at once. A suspend that
// fails as an ended thread's means that the thread never runs again, so get_state, set_state
// and resume follow it, in an order that turns with each round: each of the four calls can
// meet each step of the kernel's teardown, and set_state never meets a running thread. Every
// call that fails must fail as an ended thread's.
static void call_until_port_dies(uint32_t port, uint32_t round, ending_counts_t *counts) {
    struct timespec start;
    PP_CHECK_EQUAL(clock_gettime(CLOCK_MONOTONIC, &start), 0);
    while (!port_died(port)) {
        PP_CHECK(elapsed_milliseconds(&start) < 60000);
        const pp_status_t suspended = pp_thread_control_suspend(port);
        if (suspended == PP_OK) {
            counts[CALL_SUSPEND].running++;
            const pp_status_t resumed = pp_thread_control_resume(port);
            if (resumed == PP_OK) {
                counts[CALL_RESUME].running++;
            } else {
                count_ended(counts, CALL_RESUME, resumed);
            }
            (void)sched_yield();
        } else {
            count_ended(counts, CALL_SUSPEND, suspended);
            for (uint32_t index = 0U; index < 3U; index++) {
                const uint32_t call = (uint32_t)CALL_GET_STATE + ((round + index) % 3U);
                count_ended(counts, call, make_call(call, port));
            }
        }
    }
}

// The port of a thread that has ended stays a valid name, and every call on it fails: from the
// thread's join, through the kernel's teardown of the thread, to the port's death and after
// it, when the name is a dead name. The rounds give each call many chances to meet each step
// of the teardown, the port's death in the middle of a request among them, as one CI run did
// (docs/design/signals.md section 8).
static void ended_thread_fails(void) {
    ending_counts_t counts[CALL_COUNT];
    memset(counts, 0, sizeof counts);
    for (uint32_t round = 0U; round < ENDING_ROUNDS; round++) {
        _Atomic(bool) end = false;
        const pthread_t thread = pp_start_thread(wait_then_end, &end);
        uint32_t port = 0U;
        PP_CHECK_EQUAL(pp_thread_control_open(thread, &port), PP_OK);
        atomic_store(&end, true);
        pp_join_thread(thread);
        call_until_port_dies(port, round, counts);
        for (uint32_t call = 0U; call < CALL_COUNT; call++) {
            check_ended_by(call_name(call), make_call(call, port), MACH_SEND_INVALID_DEST);
        }
        PP_CHECK_EQUAL(pp_thread_control_close(port), PP_OK);
    }
    (void)printf("%u rounds; what the calls found before the port died:\n", ENDING_ROUNDS);
    for (uint32_t call = 0U; call < CALL_COUNT; call++) {
        const ending_counts_t *count = &counts[call];
        (void)printf("%s: running %u, terminated %u, without its thread %u, at the port's "
                     "death %u, on a dead name %u, other %u\n",
                     call_name(call), count->running, count->terminated, count->no_thread,
                     count->port_death, count->dead_name, count->other);
    }
}

// A port of the test's own stands in for a thread's port that dies while a request is on its
// way. Its server destroys each request it receives, unanswered, and the request's reply right
// goes with it, so the kernel sends the caller's reply port a send-once notification, as it
// does when a request finds its port dead; MIG's stub then reports MIG_SERVER_DIED. The server
// counts the requests, and ends at the stop request.
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
    // A receive gives up after a minute, so that a call that never returns, or a stop request
    // that never comes, fails the test instead of leaving it to CTest's timeout.
    const mach_msg_option_t options =
        (mach_msg_option_t)((unsigned)MACH_RCV_MSG | (unsigned)MACH_RCV_TIMEOUT);
    for (;;) {
        received_request_t request;
        memset(&request, 0, sizeof request);
        PP_CHECK_EQUAL(mach_msg(&request.header, options, 0U, (mach_msg_size_t)sizeof request,
                                server->port, 60000U, MACH_PORT_NULL),
                       MACH_MSG_SUCCESS);
        if (request.header.msgh_id == STOP_REQUEST_ID) {
            return NULL;
        }
        // Counted first: destroying the request answers the call, so a call that has returned
        // has been counted.
        (void)atomic_fetch_add(&server->destroyed, 1U);
        mach_msg_destroy(&request.header);
    }
}

// Starts a destroying server on a new port.
static pthread_t start_server(destroying_server_t *server) {
    const mach_port_t task = mach_task_self();
    memset(server, 0, sizeof *server);
    PP_CHECK_EQUAL(mach_port_allocate(task, MACH_PORT_RIGHT_RECEIVE, &server->port), KERN_SUCCESS);
    PP_CHECK_EQUAL(
        mach_port_insert_right(task, server->port, server->port, MACH_MSG_TYPE_MAKE_SEND),
        KERN_SUCCESS);
    return pp_start_thread(destroy_requests, server);
}

// Sends the server its stop request, a plain message, waits for it to end and frees its port.
static void stop_server(destroying_server_t *server, pthread_t thread) {
    mach_msg_header_t stop;
    memset(&stop, 0, sizeof stop);
    stop.msgh_bits = (mach_msg_bits_t)MACH_MSG_TYPE_COPY_SEND;
    stop.msgh_size = (mach_msg_size_t)sizeof stop;
    stop.msgh_remote_port = server->port;
    stop.msgh_id = STOP_REQUEST_ID;
    PP_CHECK_EQUAL(mach_msg(&stop, MACH_SEND_MSG, (mach_msg_size_t)sizeof stop, 0U, MACH_PORT_NULL,
                            MACH_MSG_TIMEOUT_NONE, MACH_PORT_NULL),
                   MACH_MSG_SUCCESS);
    pp_join_thread(thread);
    const mach_port_t task = mach_task_self();
    // Destroying the receive right leaves the send right as a dead name.
    PP_CHECK_EQUAL(mach_port_mod_refs(task, server->port, MACH_PORT_RIGHT_RECEIVE, -1),
                   KERN_SUCCESS);
    PP_CHECK_EQUAL(mach_port_deallocate(task, server->port), KERN_SUCCESS);
}

// Checks that the server has destroyed the expected number of requests so far, one for each
// call made.
static void check_destroyed(const destroying_server_t *server, const char *call,
                            uint32_t expected) {
    const uint32_t destroyed = atomic_load(&server->destroyed);
    if (destroyed != expected) {
        (void)fprintf(stderr, "After %s, %u requests had been destroyed, not %u.\n", call,
                      destroyed, expected);
    }
    PP_CHECK_EQUAL(destroyed, expected);
}

// Runs "<program> child <case>" and returns its wait status, with what it printed in output,
// cut to its size. The child's output stays out of the case's own, whose first line may have
// to be a skip.
static int run_child(const char *child_case, char *output, size_t size) {
    int pipe_ends[2] = {-1, -1};
    PP_CHECK_EQUAL(pipe(pipe_ends), 0);
    posix_spawn_file_actions_t actions;
    PP_CHECK_EQUAL(posix_spawn_file_actions_init(&actions), 0);
    PP_CHECK_EQUAL(posix_spawn_file_actions_adddup2(&actions, pipe_ends[1], STDOUT_FILENO), 0);
    PP_CHECK_EQUAL(posix_spawn_file_actions_adddup2(&actions, pipe_ends[1], STDERR_FILENO), 0);
    PP_CHECK_EQUAL(posix_spawn_file_actions_addclose(&actions, pipe_ends[0]), 0);
    PP_CHECK_EQUAL(posix_spawn_file_actions_addclose(&actions, pipe_ends[1]), 0);
    char *arguments[] = {(char *)s_program, (char *)"child", (char *)child_case, NULL};
    pid_t child = 0;
    // Apple declares environ for programs only through _NSGetEnviron.
    PP_CHECK_EQUAL(posix_spawn(&child, s_program, &actions, NULL, arguments, *_NSGetEnviron()), 0);
    PP_CHECK_EQUAL(posix_spawn_file_actions_destroy(&actions), 0);
    PP_CHECK_EQUAL(close(pipe_ends[1]), 0);
    size_t used = 0U;
    for (;;) {
        char chunk[512];
        const ssize_t got = read(pipe_ends[0], chunk, sizeof chunk);
        if (got == 0) {
            break;
        }
        if (got < 0) {
            PP_CHECK_EQUAL(errno, EINTR);
        } else {
            const size_t room = size - 1U - used;
            const size_t kept = (size_t)got < room ? (size_t)got : room;
            memcpy(output + used, chunk, kept);
            used += kept;
        }
    }
    output[used] = '\0';
    PP_CHECK_EQUAL(close(pipe_ends[0]), 0);
    int status = 0;
    PP_CHECK_EQUAL(waitpid(child, &status, 0), child);
    return status;
}

// The child of kernel_takes_calls_to_own_port: one call of thread_suspend itself, not of the
// helper, on a port of the child's own whose server destroys the request.
static void child_calls_own_port(void) {
    destroying_server_t server;
    const pthread_t thread = start_server(&server);
    const kern_return_t result = thread_suspend((thread_act_t)server.port);
    stop_server(&server, thread);
    const uint32_t destroyed = atomic_load(&server.destroyed);
    if (destroyed != 1U) {
        (void)fprintf(stderr, "thread_suspend returned %d, and %u requests were destroyed.\n",
                      (int)result, destroyed);
    }
    PP_CHECK_EQUAL(destroyed, 1U);
}

// Whether the environment says that the machine has System Integrity Protection disabled. The
// case reads the environment itself before it starts a thread, because POSIX does not require
// getenv to be thread-safe, and clang-tidy's concurrency-mt-unsafe refuses it.
static bool sip_said_disabled(void) {
    for (char **entry = *_NSGetEnviron(); *entry != NULL; entry++) {
        if (strcmp(*entry, SIP_DISABLED_SETTING) == 0) {
            return true;
        }
    }
    return false;
}

// Whether the kernel takes the helpers' calls to a port of the test's own. MIG's stubs of these
// calls send their requests as kernel-object calls (MACH64_SEND_KOBJECT_CALL), and XNU lets one
// reach a port that is not a kernel object only while System Integrity Protection is disabled;
// elsewhere, and always on iOS, a fatal guard exception ends the caller by SIGKILL
// (docs/design/signals.md section 10). A child makes one such call first. Where the kernel
// ends it, the case is skipped, with the reason as the first line of its output, unless
// PANEPORT_EXPECT_SIP_DISABLED is 1, as the native job of CI sets it because its images have
// SIP disabled: the case then fails, so that an image with SIP enabled is noticed.
static bool kernel_takes_calls_to_own_port(void) {
    const bool sip_disabled = sip_said_disabled();
    char output[4096];
    const int status = run_child("calls_own_port", output, sizeof output);
    if (WIFEXITED(status) && WEXITSTATUS(status) == EXIT_SUCCESS) {
        return true;
    }
    if (!WIFSIGNALED(status) || WTERMSIG(status) != SIGKILL) {
        (void)fprintf(stderr, "The child ended with the wait status %d, after this output:\n%s",
                      status, output);
        pp_check_failed(__FILE__, __LINE__, "the child ended by SIGKILL");
    }
    if (sip_disabled) {
        (void)fprintf(stderr, "The kernel ended the child at its call, as it does with SIP "
                              "enabled, but the environment sets " SIP_DISABLED_SETTING ".\n");
        pp_check_failed(__FILE__, __LINE__, "a kernel that takes the call with SIP disabled");
    }
    (void)puts("Skipped: the kernel takes calls to a port of the test's own only with System "
               "Integrity Protection disabled.");
    return false;
}

// Each call that sends a request reports a request destroyed unanswered as an ended thread,
// and sends exactly one: set_state sends only its read, which fails.
static void destroyed_request_fails_as_ended(void) {
    if (!kernel_takes_calls_to_own_port()) {
        return;
    }
    destroying_server_t server;
    const pthread_t thread = start_server(&server);
    const uint32_t port = (uint32_t)server.port;
    for (uint32_t call = 0U; call < CALL_COUNT; call++) {
        check_ended_by(call_name(call), make_call(call, port), MIG_SERVER_DIED);
        check_destroyed(&server, call_name(call), call + 1U);
    }
    stop_server(&server, thread);
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
    s_program = argv[0];
    if (argc == 3 && strcmp(argv[1], "child") == 0 && strcmp(argv[2], "calls_own_port") == 0) {
        child_calls_own_port();
        return EXIT_SUCCESS;
    }
    static const pp_test_case_t cases[] = {
        {"register_change_seen_after_resume", register_change_seen_after_resume},
        {"ended_thread_fails", ended_thread_fails},
        {"destroyed_request_fails_as_ended", destroyed_request_fails_as_ended},
        {"suspending_itself_refused", suspending_itself_refused},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
