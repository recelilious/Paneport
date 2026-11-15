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
#define PP_THREADS_MARKER UINT64_C(0x50414e45504f5254)
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
    bool passed = started == count;
    if (started > 0U && WaitForMultipleObjects(started, threads, TRUE, PP_THREADS_WAIT_MS) >=
                            WAIT_OBJECT_0 + started) {
        passed = false;
    }
    for (unsigned index = 0; index < started; index++) {
        CloseHandle(threads[index]);
    }
    return passed;
}

static bool check_interlocked(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    struct counters counters;
    memset(&counters, 0, sizeof counters);
    pp_test_check(run, run_workers(interlocked_worker, &counters, PP_THREADS_WORKERS),
                  "four threads update counters with interlocked functions");
    LONG64 expected_sum = (LONG64)PP_THREADS_WORKERS *
                          (((LONG64)PP_THREADS_ITERATIONS * (PP_THREADS_ITERATIONS - 1U)) / 2);
    pp_test_check(run, counters.increments == (LONG)(PP_THREADS_WORKERS * PP_THREADS_ITERATIONS),
                  "no interlocked increment is lost");
    pp_test_check(run, counters.sum == expected_sum, "no interlocked addition is lost");
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run, "%s: interlocked: %ld increments and a sum of %" PRId64 ": %s", area,
                     (long)counters.increments, (int64_t)counters.sum, pp_test_verdict(passed));
    }
    return passed;
}

static bool check_critical_section(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    struct counters counters;
    memset(&counters, 0, sizeof counters);
    InitializeCriticalSection(&counters.lock);
    pp_test_check(run, run_workers(locked_worker, &counters, PP_THREADS_WORKERS),
                  "four threads take turns in a critical section");
    DeleteCriticalSection(&counters.lock);
    pp_test_check(run, counters.guarded == PP_THREADS_WORKERS * PP_THREADS_ITERATIONS,
                  "the critical section loses no update");
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run, "%s: critical section: %lu increments: %s", area, counters.guarded,
                     pp_test_verdict(passed));
    }
    return passed;
}

static void check_manual_reset_event(struct pp_test_run *run) {
    HANDLE event = CreateEventW(NULL, TRUE, FALSE, NULL);
    if (!pp_test_check(run, event != NULL, "CreateEvent makes a manual-reset event")) {
        return;
    }
    pp_test_check(run, WaitForSingleObject(event, 0) == WAIT_TIMEOUT,
                  "a new manual-reset event is not set");
    pp_test_check(run, SetEvent(event) != FALSE, "SetEvent sets the manual-reset event");
    pp_test_check(run, WaitForSingleObject(event, 0) == WAIT_OBJECT_0,
                  "a wait sees the manual-reset event set");
    pp_test_check(run, WaitForSingleObject(event, 0) == WAIT_OBJECT_0,
                  "the manual-reset event stays set after a wait");
    pp_test_check(run, ResetEvent(event) != FALSE, "ResetEvent resets the manual-reset event");
    pp_test_check(run, WaitForSingleObject(event, 0) == WAIT_TIMEOUT,
                  "a wait sees the manual-reset event reset");
    CloseHandle(event);
}

// Two threads pass two auto-reset events back and forth, and return how many round trips the
// main thread completed.
static unsigned play_ping_pong(struct pp_test_run *run, struct ping_pong *game) {
    HANDLE thread = CreateThread(NULL, 0, pong_worker, game, 0, NULL);
    if (!pp_test_check(run, thread != NULL, "CreateThread starts the answering thread")) {
        return 0;
    }
    unsigned round_trips = 0;
    for (unsigned round = 0; round < PP_THREADS_ROUND_TRIPS; round++) {
        if (SetEvent(game->ping) == FALSE ||
            WaitForSingleObject(game->pong, PP_THREADS_WAIT_MS) != WAIT_OBJECT_0) {
            break;
        }
        round_trips++;
    }
    pp_test_check(run, WaitForSingleObject(thread, PP_THREADS_WAIT_MS) == WAIT_OBJECT_0,
                  "the answering thread ends");
    CloseHandle(thread);
    return round_trips;
}

static bool check_events(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    struct ping_pong game;
    memset(&game, 0, sizeof game);
    game.waits_ok = true;
    game.ping = CreateEventW(NULL, FALSE, FALSE, NULL);
    game.pong = CreateEventW(NULL, FALSE, FALSE, NULL);
    unsigned round_trips = 0;
    if (pp_test_check(run, game.ping != NULL, "CreateEvent makes the first auto-reset event") &&
        pp_test_check(run, game.pong != NULL, "CreateEvent makes the second auto-reset event")) {
        round_trips = play_ping_pong(run, &game);
        pp_test_check(run, game.waits_ok, "the answering thread's waits succeed");
        pp_test_check(run, round_trips == PP_THREADS_ROUND_TRIPS,
                      "every round trip of the auto-reset events completes");
        pp_test_check(run, game.answered == PP_THREADS_ROUND_TRIPS,
                      "the answering thread answers every round");
    }
    if (game.ping != NULL) {
        CloseHandle(game.ping);
    }
    if (game.pong != NULL) {
        CloseHandle(game.pong);
    }
    check_manual_reset_event(run);
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run, "%s: events: %u round trips and a manual-reset event: %s", area,
                     round_trips, pp_test_verdict(passed));
    }
    return passed;
}

// Points the context of a suspended thread at redirect_target, with the state and the marker as
// its two arguments, on a stack below the one the thread was using, aligned as at a function's
// entry: 16 bytes, less the return address that a call would have pushed.
static void redirect_context(CONTEXT *context, struct suspend_state *state) {
#if defined(__x86_64__)
    context->Rsp = ((context->Rsp - 256U) & ~(DWORD64)15U) - 8U;
    context->Rip = (ULONG_PTR)redirect_target;
    context->Rcx = (ULONG_PTR)state;
    context->Rdx = PP_THREADS_MARKER;
#else
    context->Esp = ((context->Esp - 256U) & ~(DWORD)15U) - 4U;
    context->Eip = (ULONG_PTR)redirect_target;
    context->Ecx = (ULONG_PTR)state;
    context->Edx = PP_THREADS_MARKER;
#endif
}

static bool wait_for(const volatile LONG *value, LONG at_least) {
    for (unsigned waited = 0; waited < PP_THREADS_WAIT_MS; waited++) {
        if (*value >= at_least) {
            return true;
        }
        Sleep(1);
    }
    return false;
}

// Suspends the spinning thread, checks that it stays still, and changes its context; returns
// whether the context was changed, so that the thread will end by itself.
static bool suspend_and_redirect(struct pp_test_run *run, HANDLE thread,
                                 struct suspend_state *state) {
    if (!pp_test_check(run, SuspendThread(thread) == 0U, "SuspendThread suspends the thread")) {
        return false;
    }
    CONTEXT context;
    memset(&context, 0, sizeof context);
    context.ContextFlags = CONTEXT_CONTROL | CONTEXT_INTEGER;
    bool changed = false;
    if (pp_test_check(run, GetThreadContext(thread, &context) != FALSE,
                      "GetThreadContext reads the suspended thread")) {
        LONG before = state->spins;
        Sleep(50);
        if (pp_test_check(run, state->spins == before, "the suspended thread stays still")) {
            redirect_context(&context, state);
            changed = pp_test_check(run, SetThreadContext(thread, &context) != FALSE,
                                    "SetThreadContext changes the suspended thread");
        }
    }
    pp_test_check(run, ResumeThread(thread) == 1U, "ResumeThread resumes the thread");
    return changed;
}

static bool check_suspend_and_context(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    struct suspend_state state;
    memset((void *)&state, 0, sizeof state);
    HANDLE thread = CreateThread(NULL, 0, spinner, &state, 0, NULL);
    if (!pp_test_check(run, thread != NULL, "CreateThread starts the spinning thread")) {
        return false;
    }
    bool redirected = false;
    if (pp_test_check(run, wait_for(&state.running, 1), "the spinning thread starts") &&
        pp_test_check(run, wait_for(&state.spins, 1), "the spinning thread spins")) {
        redirected = suspend_and_redirect(run, thread, &state);
    }
    if (redirected) {
        DWORD exit_code = 0;
        bool ended = WaitForSingleObject(thread, PP_THREADS_WAIT_MS) == WAIT_OBJECT_0;
        pp_test_check(run, ended, "the resumed thread ends");
        if (ended) {
            pp_test_check(run, GetExitCodeThread(thread, &exit_code) != FALSE,
                          "GetExitCodeThread reads the thread's exit code");
            pp_test_check(run, exit_code == PP_THREADS_REDIRECT_EXIT_CODE,
                          "the resumed thread runs where its new context points");
            pp_test_check(run, state.redirected == 1, "the resumed thread reaches the new code");
            pp_test_check(run, state.marker == PP_THREADS_MARKER,
                          "the resumed thread receives its new register arguments");
        } else {
            TerminateThread(thread, 1);
        }
    } else {
        TerminateThread(thread, 1);
    }
    CloseHandle(thread);
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run, "%s: suspend, context change and resume: %s", area,
                     pp_test_verdict(passed));
    }
    return passed;
}

bool pp_threads_report(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    check_interlocked(run, area, detailed);
    check_critical_section(run, area, detailed);
    check_events(run, area, detailed);
    check_suspend_and_context(run, area, detailed);
    bool passed = run->failures == failures;
    if (!detailed) {
        pp_test_line(run, "%s: interlocked, critical section, events and suspend with context: %s",
                     area, pp_test_verdict(passed));
    }
    return passed;
}
