// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "threads_check.h"

#include <inttypes.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define PP_THREADS_WORKERS 4U
#define PP_THREADS_ITERATIONS 100000U
#define PP_THREADS_ROUND_TRIPS 1000U
#define PP_THREADS_WAIT_MS 30000U
#define PP_THREADS_REDIRECT_EXIT_CODE 42U

// The value that the suspended worker receives in its second register argument once its
// context is changed. On x86-64 it fills the whole register, so that a truncated context shows.
#if defined(__x86_64__)
#define PP_THREADS_MARKER ((ULONG_PTR)UINT64_C(0x50414e45504f5254))
#define PP_THREADS_CONVENTION
#else
#define PP_THREADS_MARKER ((ULONG_PTR)0x50504f52U)
// fastcall passes the first two arguments in ECX and EDX, as x86-64 passes them in RCX and
// RDX, so the redirected worker needs nothing written to its stack.
#define PP_THREADS_CONVENTION __attribute__((fastcall))
#endif

struct counters {
    volatile LONG increments;
    volatile LONG64 sum;
    CRITICAL_SECTION lock;
    unsigned long guarded;
};

struct ping_pong {
    HANDLE ping;
    HANDLE pong;
    unsigned answered;
    bool waits_ok;
};

struct suspend_state {
    volatile LONG running;
    volatile LONG spins;
    volatile LONG redirected;
    volatile ULONG_PTR marker;
};

static DWORD WINAPI interlocked_worker(void *parameter) {
    struct counters *counters = parameter;
    for (ULONG index = 0; index < PP_THREADS_ITERATIONS; index++) {
        InterlockedIncrement(&counters->increments);
        InterlockedExchangeAdd64(&counters->sum, (LONG64)index);
    }
    return 0;
}

static DWORD WINAPI locked_worker(void *parameter) {
    struct counters *counters = parameter;
    for (ULONG index = 0; index < PP_THREADS_ITERATIONS; index++) {
        EnterCriticalSection(&counters->lock);
        counters->guarded++;
        LeaveCriticalSection(&counters->lock);
    }
    return 0;
}

static DWORD WINAPI pong_worker(void *parameter) {
    struct ping_pong *game = parameter;
    for (unsigned round = 0; round < PP_THREADS_ROUND_TRIPS; round++) {
        if (WaitForSingleObject(game->ping, PP_THREADS_WAIT_MS) != WAIT_OBJECT_0) {
            game->waits_ok = false;
            return 1;
        }
        game->answered++;
        if (SetEvent(game->pong) == FALSE) {
            game->waits_ok = false;
            return 1;
        }
    }
    return 0;
}

static DWORD WINAPI spinner(void *parameter) {
    struct suspend_state *state = parameter;
    InterlockedExchange(&state->running, 1);
    for (;;) {
        state->spins++;
    }
}

// Where the suspended worker continues once its context is changed. It never returns: no
// caller pushed a return address for it.
__attribute__((noinline, noreturn)) static void PP_THREADS_CONVENTION
redirect_target(struct suspend_state *state, ULONG_PTR marker) {
    state->marker = marker;
    InterlockedExchange(&state->redirected, 1);
    ExitThread(PP_THREADS_REDIRECT_EXIT_CODE);
}

// Starts count threads on one function and waits for all of them.
static bool run_workers(LPTHREAD_START_ROUTINE function, void *parameter, unsigned count) {
    HANDLE threads[PP_THREADS_WORKERS];
    unsigned started = 0;
    for (; started < count; started++) {
        threads[started] = CreateThread(NULL, 0, function, parameter, 0, NULL);
        if (threads[started] == NULL) {
            break;
        }
    }
    bool passed =
        started == count && WaitForMultipleObjects(started, threads, TRUE, PP_THREADS_WAIT_MS) <
                                WAIT_OBJECT_0 + started;
    for (unsigned index = 0; index < started; index++) {
        CloseHandle(threads[index]);
    }
    return passed;
}

static bool check_interlocked(struct pp_test_run *run, const char *area, bool detailed) {
    struct counters counters;
    memset(&counters, 0, sizeof counters);
    bool ran = pp_test_check(run, run_workers(interlocked_worker, &counters, PP_THREADS_WORKERS),
                             "four threads update counters with interlocked functions");
    LONG64 expected_sum = (LONG64)PP_THREADS_WORKERS *
                          ((LONG64)PP_THREADS_ITERATIONS * (PP_THREADS_ITERATIONS - 1U) / 2);
    bool exact =
        pp_test_check(run,
                      counters.increments == (LONG)(PP_THREADS_WORKERS * PP_THREADS_ITERATIONS) &&
                          counters.sum == expected_sum,
                      "no interlocked update is lost");
    if (detailed) {
        pp_test_line(run, "%s: interlocked: %ld increments and a sum of %" PRId64 ": %s", area,
                     (long)counters.increments, (int64_t)counters.sum,
                     ran && exact ? "pass" : "fail");
    }
    return ran && exact;
}

static bool check_critical_section(struct pp_test_run *run, const char *area, bool detailed) {
    struct counters counters;
    memset(&counters, 0, sizeof counters);
    InitializeCriticalSection(&counters.lock);
    bool ran = pp_test_check(run, run_workers(locked_worker, &counters, PP_THREADS_WORKERS),
                             "four threads take turns in a critical section");
    DeleteCriticalSection(&counters.lock);
    bool exact = pp_test_check(run, counters.guarded == PP_THREADS_WORKERS * PP_THREADS_ITERATIONS,
                               "the critical section loses no update");
    if (detailed) {
        pp_test_line(run, "%s: critical section: %lu increments: %s", area, counters.guarded,
                     ran && exact ? "pass" : "fail");
    }
    return ran && exact;
}

static bool check_manual_reset_event(struct pp_test_run *run) {
    HANDLE event = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!pp_test_check(run, event != NULL, "CreateEvent makes a manual-reset event")) {
        return false;
    }
    bool passed = WaitForSingleObject(event, 0) == WAIT_TIMEOUT;
    passed = SetEvent(event) != FALSE && passed;
    passed = WaitForSingleObject(event, 0) == WAIT_OBJECT_0 && passed;
    passed = WaitForSingleObject(event, 0) == WAIT_OBJECT_0 && passed;
    passed = ResetEvent(event) != FALSE && passed;
    passed = WaitForSingleObject(event, 0) == WAIT_TIMEOUT && passed;
    CloseHandle(event);
    return pp_test_check(run, passed, "a manual-reset event stays set until it is reset");
}

static bool check_events(struct pp_test_run *run, const char *area, bool detailed) {
    struct ping_pong game;
    memset(&game, 0, sizeof game);
    game.waits_ok = true;
    game.ping = CreateEventW(NULL, FALSE, FALSE, NULL);
    game.pong = CreateEventW(NULL, FALSE, FALSE, NULL);
    bool passed = pp_test_check(run, game.ping != NULL && game.pong != NULL,
                                "CreateEvent makes two auto-reset events");
    unsigned round_trips = 0;
    if (passed) {
        HANDLE thread = CreateThread(NULL, 0, pong_worker, &game, 0, NULL);
        passed = pp_test_check(run, thread != NULL, "CreateThread starts the answering thread");
        for (unsigned round = 0; passed && round < PP_THREADS_ROUND_TRIPS; round++) {
            if (SetEvent(game.ping) == FALSE ||
                WaitForSingleObject(game.pong, PP_THREADS_WAIT_MS) != WAIT_OBJECT_0) {
                break;
            }
            round_trips++;
        }
        if (thread != NULL) {
            passed = WaitForSingleObject(thread, PP_THREADS_WAIT_MS) == WAIT_OBJECT_0 && passed;
            CloseHandle(thread);
        }
        passed = pp_test_check(run,
                               passed && game.waits_ok && round_trips == PP_THREADS_ROUND_TRIPS &&
                                   game.answered == PP_THREADS_ROUND_TRIPS,
                               "two threads pass two auto-reset events back and forth") &&
                 passed;
    }
    if (game.ping != NULL) {
        CloseHandle(game.ping);
    }
    if (game.pong != NULL) {
        CloseHandle(game.pong);
    }
    passed = check_manual_reset_event(run) && passed;
    if (detailed) {
        pp_test_line(run, "%s: events: %u round trips and a manual-reset event: %s", area,
                     round_trips, passed ? "pass" : "fail");
    }
    return passed;
}

// Points the context of a suspended thread at redirect_target, with the state and the marker as
// its two arguments, on a stack below the one the thread was using, aligned as at a function's
// entry: 16 bytes, less the return address that a call would have pushed.
static void redirect_context(CONTEXT *context, struct suspend_state *state) {
#if defined(__x86_64__)
    DWORD64 stack = ((context->Rsp - 256U) & ~(DWORD64)15U) - 8U;
    context->Rsp = stack;
    context->Rip = (DWORD64)(ULONG_PTR)redirect_target;
    context->Rcx = (DWORD64)(ULONG_PTR)state;
    context->Rdx = (DWORD64)PP_THREADS_MARKER;
#else
    DWORD stack = ((context->Esp - 256U) & ~(DWORD)15U) - 4U;
    context->Esp = stack;
    context->Eip = (DWORD)(ULONG_PTR)redirect_target;
    context->Ecx = (DWORD)(ULONG_PTR)state;
    context->Edx = (DWORD)PP_THREADS_MARKER;
#endif
}

static bool wait_for(volatile LONG *value, LONG at_least) {
    for (unsigned waited = 0; waited < PP_THREADS_WAIT_MS; waited++) {
        if (*value >= at_least) {
            return true;
        }
        Sleep(1);
    }
    return false;
}

static bool check_suspend_and_context(struct pp_test_run *run, const char *area, bool detailed) {
    struct suspend_state state;
    memset((void *)&state, 0, sizeof state);
    HANDLE thread = CreateThread(NULL, 0, spinner, &state, 0, NULL);
    if (!pp_test_check(run, thread != NULL, "CreateThread starts the spinning thread")) {
        return false;
    }
    bool passed = pp_test_check(run, wait_for(&state.running, 1) && wait_for(&state.spins, 1),
                                "the spinning thread runs");
    passed = pp_test_check(run, SuspendThread(thread) == 0U, "SuspendThread suspends it") && passed;

    CONTEXT context;
    memset(&context, 0, sizeof context);
    context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER;
    passed = pp_test_check(run, GetThreadContext(thread, &context) != FALSE,
                           "GetThreadContext reads the suspended thread") &&
             passed;
    LONG before = state.spins;
    Sleep(50);
    passed =
        pp_test_check(run, state.spins == before, "the suspended thread stays still") && passed;

    if (passed) {
        redirect_context(&context, &state);
        passed = pp_test_check(run, SetThreadContext(thread, &context) != FALSE,
                               "SetThreadContext changes the suspended thread");
    }
    passed = pp_test_check(run, ResumeThread(thread) == 1U, "ResumeThread resumes it") && passed;

    DWORD exit_code = 0;
    bool ended = WaitForSingleObject(thread, PP_THREADS_WAIT_MS) == WAIT_OBJECT_0;
    passed = pp_test_check(run,
                           ended && GetExitCodeThread(thread, &exit_code) != FALSE &&
                               exit_code == PP_THREADS_REDIRECT_EXIT_CODE,
                           "the resumed thread runs where its new context points") &&
             passed;
    passed = pp_test_check(run, state.redirected == 1 && state.marker == PP_THREADS_MARKER,
                           "the resumed thread receives its new register arguments") &&
             passed;
    if (!ended) {
        TerminateThread(thread, 1);
    }
    CloseHandle(thread);
    if (detailed) {
        pp_test_line(run, "%s: suspend, context change and resume: %s", area,
                     passed ? "pass" : "fail");
    }
    return passed;
}

bool pp_threads_report(struct pp_test_run *run, const char *area, bool detailed) {
    bool passed = check_interlocked(run, area, detailed);
    passed = check_critical_section(run, area, detailed) && passed;
    passed = check_events(run, area, detailed) && passed;
    passed = check_suspend_and_context(run, area, detailed) && passed;
    if (!detailed) {
        pp_test_line(run, "%s: interlocked, critical section, events and suspend with context: %s",
                     area, passed ? "pass" : "fail");
    }
    return passed;
}
