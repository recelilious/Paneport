// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "threads_check.h"

#include <inttypes.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
// QueryUnbiasedInterruptTime.
#include <realtimeapiset.h>

#define PP_THREADS_WORKERS 4U
#define PP_THREADS_ITERATIONS 100000U
#define PP_THREADS_ROUND_TRIPS 1000U
#define PP_THREADS_WAIT_MS 30000U
#define PP_THREADS_REDIRECT_EXIT_CODE 42U

// The thread that loops on a syscall: how often it is suspended, has its context read and is
// resumed, how many calls it makes first, so that FEX has translated its loop, the code it ends
// with, and how long the check spins for its next call before it sleeps.
#define PP_THREADS_SYSCALL_ROUNDS 1000U
#define PP_THREADS_SYSCALL_WARM_UP 1000
#define PP_THREADS_SYSCALL_EXIT_CODE 43U
#define PP_THREADS_SYSCALL_SPINS 100000U

// The clocks must advance across a sleep of 100 ms by 50 ms to 10 s: Sleep may wake a tick
// early, and a loaded runner late. The interrupt time counts in units of 100 ns.
#define PP_THREADS_CLOCK_SLEEP_MS 100U
#define PP_THREADS_CLOCK_MIN_MS 50U
#define PP_THREADS_CLOCK_MAX_MS 10000U
#define PP_THREADS_INTERRUPT_UNITS_PER_MS 10000U

// The misaligned access: its rounds, and its place in a 16-byte-aligned buffer, so that it
// crosses 16 bytes, with 8 bytes on x86-64 and 4 on i686.
#define PP_THREADS_UNALIGNED_ROUNDS 1000U
#ifdef __x86_64__
#define PP_THREADS_UNALIGNED_OFFSET 12U
#define PP_THREADS_UNALIGNED_MOVE "movq"
#else
#define PP_THREADS_UNALIGNED_OFFSET 14U
#define PP_THREADS_UNALIGNED_MOVE "movl"
#endif

// The value that the suspended worker receives in its second register argument once its
// context is changed. On x86-64 it fills the whole register, so that a truncated context shows.
#ifdef __x86_64__
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

// The thread that loops on a syscall: whether it runs and should stop, how many calls it made,
// and how many of them gave a wrong result.
struct syscall_loop_state {
    volatile LONG running;
    volatile LONG stop;
    volatile LONG calls;
    volatile LONG wrong;
};

// The buffer of the misaligned accesses, and how many of them read back another value.
struct unaligned_state {
    _Alignas(16) unsigned char bytes[32];
    unsigned mismatches;
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

// Stores a value where it crosses 16 bytes and loads it back, through RCX (ECX on i686), not
// RSP. Under FEX both accesses get TSO's ordering, the host faults on them, and FEX's handler
// for misaligned accesses rewrites the two instructions in its code buffer, which must then
// still store and load the value.
static DWORD WINAPI unaligned_worker(void *parameter) {
    struct unaligned_state *state = parameter;
    unsigned char *at = state->bytes + PP_THREADS_UNALIGNED_OFFSET;
    for (ULONG_PTR index = 0; index < PP_THREADS_UNALIGNED_ROUNDS; index++) {
        ULONG_PTR value = PP_THREADS_MARKER ^ index;
        ULONG_PTR back = 0;
        // NOLINTNEXTLINE(portability-no-assembler): the test needs these two accesses as written.
        __asm__ volatile(PP_THREADS_UNALIGNED_MOVE " %1, (%2)\n\t" PP_THREADS_UNALIGNED_MOVE
                                                   " (%2), %0"
                         : "=&d"(back)
                         : "a"(value), "c"(at)
                         : "memory");
        if (back != value) {
            state->mismatches++;
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

// Calls QueryPerformanceCounter until told to stop. Under Wine it is a short syscall,
// NtQueryPerformanceCounter, so a suspension often finds the thread entering or leaving Wine's
// syscall dispatcher, where Wine builds the syscall frame from the signal context. Each counter
// must be written and must not go back.
static DWORD WINAPI syscall_looper(void *parameter) {
    struct syscall_loop_state *state = parameter;
    LONGLONG last = 0;
    InterlockedExchange(&state->running, 1);
    while (state->stop == 0) {
        LARGE_INTEGER counter;
        counter.QuadPart = -1;
        if (QueryPerformanceCounter(&counter) == FALSE || counter.QuadPart < last) {
            InterlockedIncrement(&state->wrong);
        }
        last = counter.QuadPart;
        InterlockedIncrement(&state->calls);
    }
    return PP_THREADS_SYSCALL_EXIT_CODE;
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

// Whether a clock advanced by about the time slept, in one comparison: below the minimum, the
// unsigned difference wraps around and exceeds the range.
static bool advanced(ULONGLONG milliseconds) {
    return milliseconds - PP_THREADS_CLOCK_MIN_MS <=
           PP_THREADS_CLOCK_MAX_MS - PP_THREADS_CLOCK_MIN_MS;
}

// GetTickCount, GetTickCount64 and QueryUnbiasedInterruptTime read the shared user data in
// kernel32 and ntdll. Wine on Apple silicon places that data where macOS chooses, since its
// fixed address lies in the page zero, so these clocks show whether the ARM64EC side follows it.
static bool check_clocks(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    ULONGLONG unbiased = 0;
    ULONGLONG unbiased_after = 0;
    DWORD tick = GetTickCount();
    ULONGLONG tick64 = GetTickCount64();
    pp_test_check(run, QueryUnbiasedInterruptTime(&unbiased) != FALSE,
                  "QueryUnbiasedInterruptTime reads the interrupt time");
    Sleep(PP_THREADS_CLOCK_SLEEP_MS);
    DWORD ticks = GetTickCount() - tick;
    ULONGLONG ticks64 = GetTickCount64() - tick64;
    pp_test_check(run, QueryUnbiasedInterruptTime(&unbiased_after) != FALSE,
                  "QueryUnbiasedInterruptTime reads the interrupt time again");
    pp_test_check(run, advanced(ticks), "GetTickCount advances by about the time slept");
    pp_test_check(run, advanced(ticks64), "GetTickCount64 advances by about the time slept");
    pp_test_check(run, unbiased_after >= unbiased, "QueryUnbiasedInterruptTime does not go back");
    pp_test_check(run, advanced((unbiased_after - unbiased) / PP_THREADS_INTERRUPT_UNITS_PER_MS),
                  "QueryUnbiasedInterruptTime advances by about the time slept");
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run,
                     "%s: clocks: GetTickCount, GetTickCount64 and QueryUnbiasedInterruptTime "
                     "advance across a sleep: %s",
                     area, pp_test_verdict(passed));
    }
    return passed;
}

static bool check_unaligned(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    struct unaligned_state state;
    memset(&state, 0, sizeof state);
    pp_test_check(run, run_workers(unaligned_worker, &state, 1U),
                  "a second thread makes misaligned accesses");
    pp_test_check(run, state.mismatches == 0U,
                  "each misaligned access reads back the value it stored");
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run, "%s: unaligned access: %u stores and loads across 16 bytes: %s", area,
                     PP_THREADS_UNALIGNED_ROUNDS, pp_test_verdict(passed));
    }
    return passed;
}

// Points the context of a suspended thread at redirect_target, with the state and the marker as
// its two arguments, on a stack below the one the thread was using, aligned as at a function's
// entry: 16 bytes, less the return address that a call would have pushed.
static void redirect_context(CONTEXT *context, struct suspend_state *state) {
#ifdef __x86_64__
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
    // CONTEXT_FULL holds the control and integer registers that the change needs.
    context.ContextFlags = CONTEXT_FULL;
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

// Waits for the looping thread's next call: by spinning first, since the thread runs on another
// CPU, and then by sleeping, as wait_for does.
static bool wait_for_call(const struct syscall_loop_state *state, LONG at_least) {
    for (unsigned spin = 0; spin < PP_THREADS_SYSCALL_SPINS; spin++) {
        if (state->calls >= at_least) {
            return true;
        }
        YieldProcessor();
    }
    return wait_for(&state->calls, at_least);
}

// One round: suspends the looping thread, reads its context and resumes it. Returns whether every
// step succeeded. Under Wine the suspension itself has the thread build its syscall frame from
// the signal context when it is inside a dispatcher, and the thread applies its context again on
// resume, so a frame built wrongly breaks it although the check sets nothing. Setting the context
// back as well crashed in M4.2, and once more in M4.3's single mode after the FEX fork's change
// (run 45787039425), so the check only reads it: M4.4 takes the set-back up, together with the
// race in section 11 of docs/design/runtime-macos-harness.md.
static bool suspend_in_syscall_loop(HANDLE thread) {
    if (SuspendThread(thread) != 0U) {
        return false;
    }
    CONTEXT context;
    memset(&context, 0, sizeof context);
    context.ContextFlags = CONTEXT_FULL;
    bool passed = GetThreadContext(thread, &context) != FALSE;
    if (ResumeThread(thread) != 1U) {
        passed = false;
    }
    return passed;
}

// Suspends a thread that loops on a syscall, again and again, reading its context each time, and
// checks that the thread lives on, keeps its results right and ends.
static bool check_syscall_loop(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    struct syscall_loop_state state;
    memset((void *)&state, 0, sizeof state);
    HANDLE thread = CreateThread(NULL, 0, syscall_looper, &state, 0, NULL);
    if (!pp_test_check(run, thread != NULL,
                       "CreateThread starts the thread that loops on a syscall")) {
        return false;
    }
    if (pp_test_check(run, wait_for(&state.running, 1),
                      "the thread that loops on a syscall starts") &&
        pp_test_check(run, wait_for_call(&state, PP_THREADS_SYSCALL_WARM_UP),
                      "the thread that loops on a syscall makes its first calls")) {
        bool steps_ok = true;
        bool calls_go_on = true;
        for (unsigned round = 0; round < PP_THREADS_SYSCALL_ROUNDS && steps_ok && calls_go_on;
             round++) {
            calls_go_on = wait_for_call(&state, state.calls + 1);
            if (calls_go_on) {
                steps_ok = suspend_in_syscall_loop(thread);
            }
        }
        pp_test_check(run, steps_ok,
                      "SuspendThread, GetThreadContext and ResumeThread succeed on the thread in "
                      "its syscall loop");
        pp_test_check(run, calls_go_on, "the thread goes on calling after each resume");
    }
    InterlockedExchange(&state.stop, 1);
    bool ended = WaitForSingleObject(thread, PP_THREADS_WAIT_MS) == WAIT_OBJECT_0;
    pp_test_check(run, ended, "the thread that loops on a syscall ends");
    DWORD exit_code = 0;
    if (ended) {
        pp_test_check(run, GetExitCodeThread(thread, &exit_code) != FALSE,
                      "GetExitCodeThread reads the looping thread's exit code");
        pp_test_check(run, exit_code == PP_THREADS_SYSCALL_EXIT_CODE,
                      "the looping thread returns from its function");
    } else {
        TerminateThread(thread, 1);
    }
    CloseHandle(thread);
    pp_test_check(run, state.wrong == 0,
                  "each QueryPerformanceCounter call of the thread is right");
    bool passed = run->failures == failures;
    if (detailed) {
        pp_test_line(run, "%s: syscall loop: %u suspensions with the context read: %s", area,
                     PP_THREADS_SYSCALL_ROUNDS, pp_test_verdict(passed));
    }
    return passed;
}

bool pp_threads_report(struct pp_test_run *run, const char *area, bool detailed) {
    unsigned failures = run->failures;
    check_interlocked(run, area, detailed);
    check_critical_section(run, area, detailed);
    check_events(run, area, detailed);
    check_clocks(run, area, detailed);
    check_unaligned(run, area, detailed);
    check_suspend_and_context(run, area, detailed);
    check_syscall_loop(run, area, detailed);
    bool passed = run->failures == failures;
    if (!detailed) {
        pp_test_line(run,
                     "%s: interlocked, critical section, events, clocks, unaligned access, "
                     "suspend with context and suspend in a syscall loop: %s",
                     area, pp_test_verdict(passed));
    }
    return passed;
}
