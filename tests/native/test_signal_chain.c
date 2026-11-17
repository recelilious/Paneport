// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.1: installing the dispatcher, saving the previous actions and chaining to them. A
// stand-in for the runtime's handler is installed before pp_init, as the runtime's is on the
// device; each fault on an unmarked thread must reach it exactly once, with the siginfo that
// it receives without the dispatcher. The cases that end by a signal run in a child process,
// and their parent reads the crash record they leave (docs/design/signals.md section 10).

#include <errno.h>
#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdint.h>
#include <string.h>
#include <time.h>

#include "crash_record.h"
#include "pp_check.h"
#include "signal_test_support.h"
#include "test_support.h"
#include "thread_role.h"

// The documented singletons of this program (C-0010 section 3): what the handlers saw, and how
// long the crash record writer's hook holds a write, because a signal handler and the hook
// receive no argument of the test, and the path that the program started with, which a case
// needs to start a child.
static pp_stand_in_t s_stand_in;
static _Atomic(uint32_t) s_writer_hold_milliseconds;
static struct {
    _Atomic(uint64_t) count;
    _Atomic(int) signal;
    _Atomic(bool) mask_applied;
    _Atomic(bool) own_signal_blocked;
} s_plain;
static const char *s_program;

static void stand_in_handler(int signal_number, siginfo_t *info, void *context) {
    pp_stand_in_handle(&s_stand_in, signal_number, info, context);
}

// A plain handler, without SA_SIGINFO, whose action blocks SIGUSR2 while it runs; the kernel
// would block its own signal too, as its action has no SA_NODEFER.
static void plain_handler(int signal_number) {
    sigset_t blocked;
    (void)pthread_sigmask(SIG_BLOCK, NULL, &blocked);
    atomic_store(&s_plain.mask_applied, sigismember(&blocked, SIGUSR2) == 1);
    atomic_store(&s_plain.own_signal_blocked, sigismember(&blocked, signal_number) == 1);
    atomic_store(&s_plain.signal, signal_number);
    (void)atomic_fetch_add(&s_plain.count, 1U);
}

typedef struct raise_context_t {
    int signal;
    uint64_t address;
} raise_context_t;

static void *raise_on_thread(void *argument) {
    raise_context_t *context = argument;
    pp_raise_signal(context->signal, &context->address);
    return NULL;
}

// What the stand-in saw of one signal.
typedef struct seen_t {
    siginfo_t info;
    uint64_t program_counter;
    uint64_t planted;
} seen_t;

// Raises a signal on a new, unmarked thread and checks that the stand-in got it exactly once.
static void raise_and_observe(int signal_number, seen_t *seen) {
    const uint64_t before = pp_stand_in_count(&s_stand_in, signal_number);
    raise_context_t context = {.signal = signal_number, .address = 0U};
    pp_run_on_thread(raise_on_thread, &context);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, signal_number), before + 1U);
    PP_CHECK_EQUAL(atomic_load(&s_stand_in.last_signal), signal_number);
    memcpy(&seen->info, &s_stand_in.last_info, sizeof seen->info);
    seen->program_counter = atomic_load(&s_stand_in.last_program_counter);
    seen->planted = context.address;
}

// Checks that the stand-in saw the same siginfo, member by member, through the dispatcher as
// without it, from the same instruction. A fault on a fresh page reports that page, which
// differs between the two runs.
static void check_same_info(const seen_t *through, const seen_t *direct) {
    PP_CHECK_EQUAL(through->program_counter, direct->program_counter);
    PP_CHECK_EQUAL(through->info.si_signo, direct->info.si_signo);
    PP_CHECK_EQUAL(through->info.si_errno, direct->info.si_errno);
    PP_CHECK_EQUAL(through->info.si_code, direct->info.si_code);
    PP_CHECK_EQUAL(through->info.si_pid, direct->info.si_pid);
    PP_CHECK_EQUAL(through->info.si_uid, direct->info.si_uid);
    PP_CHECK_EQUAL(through->info.si_status, direct->info.si_status);
    PP_CHECK_EQUAL(through->info.si_band, direct->info.si_band);
    PP_CHECK(through->info.si_value.sival_ptr == direct->info.si_value.sival_ptr);
    if (through->planted != 0U) {
        PP_CHECK_EQUAL((uint64_t)(uintptr_t)through->info.si_addr, through->planted);
        PP_CHECK_EQUAL((uint64_t)(uintptr_t)direct->info.si_addr, direct->planted);
    } else {
        PP_CHECK(through->info.si_addr == direct->info.si_addr);
    }
}

// Each of the six signals reaches the stand-in once, with the siginfo it gets without the
// dispatcher.
static void every_signal_reaches_runtime_once(void) {
    atomic_store(&s_stand_in.owns_all, true);
    atomic_store(&s_stand_in.keeps_info, true);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    seen_t direct[PP_SIGNAL_COUNT];
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        raise_and_observe(pp_test_signal_at(index), &direct[index]);
    }

    char folder[64];
    pp_make_folder(folder, sizeof folder);
    pp_start_host(folder);
    pp_signal_counters_t before;
    pp_signals_read_counters(&before);
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        seen_t through;
        raise_and_observe(pp_test_signal_at(index), &through);
        check_same_info(&through, &direct[index]);
    }
    pp_signal_counters_t after;
    pp_signals_read_counters(&after);
    PP_CHECK_EQUAL(after.runtime - before.runtime, PP_SIGNAL_COUNT);
    PP_CHECK_EQUAL(after.guest_handled + after.guest_declined, 0U);
    pp_remove_record_folder(folder);
}

static void *send_fpe(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pthread_kill(pthread_self(), SIGFPE), 0);
    return NULL;
}

// A plain handler gets the signal number, with its action's mask applied and its own signal
// blocked while it runs, as the kernel would run it, although the dispatcher's own action leaves
// the signal unblocked. Under ThreadSanitizer, which runs every handler with all signals
// blocked, the mask checks cannot fail; the asan-ubsan run makes them count.
static void plain_handler_gets_signal(void) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = plain_handler;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaddset(&action.sa_mask, SIGUSR2), 0);
    PP_CHECK_EQUAL(sigaction(SIGFPE, &action, NULL), 0);
    pp_start_host(NULL);

    pp_run_on_thread(send_fpe, NULL);
    PP_CHECK_EQUAL(atomic_load(&s_plain.count), 1U);
    PP_CHECK_EQUAL(atomic_load(&s_plain.signal), SIGFPE);
    PP_CHECK(atomic_load(&s_plain.mask_applied));
    PP_CHECK(atomic_load(&s_plain.own_signal_blocked));
}

static void *send_abort(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pthread_kill(pthread_self(), SIGABRT), 0);
    return NULL;
}

// An ignored signal that a thread sent is ignored through the dispatcher too.
static void ignored_sent_signal_returns(void) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_IGN;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaction(SIGABRT, &action, NULL), 0);
    pp_start_host(NULL);

    pp_signal_counters_t before;
    pp_signals_read_counters(&before);
    pp_run_on_thread(send_abort, NULL);
    pp_signal_counters_t after;
    pp_signals_read_counters(&after);
    PP_CHECK_EQUAL(after.runtime - before.runtime, 1U);
}

// The same with SA_SIGINFO in the flags of the ignored action: the dispatcher judges an action
// by its handler, which its flags may not match, so it never calls SIG_IGN as a function.
static void ignored_with_siginfo_returns(void) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_IGN;
    action.sa_flags = SA_SIGINFO;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaction(SIGABRT, &action, NULL), 0);
    struct sigaction current;
    PP_CHECK_EQUAL(sigaction(SIGABRT, NULL, &current), 0);
    PP_CHECK(pp_signal_is_ignored(&current));
    PP_CHECK(pp_signal_has_flag(current.sa_flags, SA_SIGINFO));
    pp_start_host(NULL);

    pp_signal_counters_t before;
    pp_signals_read_counters(&before);
    pp_run_on_thread(send_abort, NULL);
    pp_signal_counters_t after;
    pp_signals_read_counters(&after);
    PP_CHECK_EQUAL(after.runtime - before.runtime, 1U);
}

static void *fault_near_null(void *argument) {
    (void)argument;
    pp_signal_plant_read(pp_test_address(16U));
    return NULL;
}

static void *fault_above_null(void *argument) {
    (void)argument;
    pp_signal_plant_read(pp_test_address((uint64_t)PP_SIGNAL_RUNTIME_NULL_LIMIT + 16U));
    return NULL;
}

// A fault in the first page, which the runtime resolves as a managed null reference, needs no
// record. A fault above it gets its record before it goes to the runtime, and the record stays
// when the runtime resolves the fault after all, as here (signals.md section 5).
static void first_page_fault_needs_no_record(void) {
    pp_stand_in_own(&s_stand_in, NULL, 2U * (size_t)PP_SIGNAL_RUNTIME_NULL_LIMIT);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    char folder[64];
    pp_make_folder(folder, sizeof folder);
    pp_start_host(folder);

    pp_run_on_thread(fault_near_null, NULL);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGSEGV), 1U);
    PP_CHECK(!pp_has_crash_record(folder));

    pp_run_on_thread(fault_above_null, NULL);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGSEGV), 2U);
    pp_test_record_t record;
    pp_read_crash_record(folder, &record);
    PP_CHECK_EQUAL(record.signal, SIGSEGV);
    PP_CHECK_EQUAL(record.fault_address, (uint64_t)PP_SIGNAL_RUNTIME_NULL_LIMIT + 16U);
    PP_CHECK_TEXT(record.message, PP_TEST_MESSAGE_CRASH_PATH);
    pp_remove_record_folder(folder);
}

// Child: SIGBUS has the default action when pp_init runs, so a fault on a page that cannot be
// read ends the child with a record.
static void child_default_action(const char *folder) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_DFL;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaction(SIGBUS, &action, NULL), 0);
    pp_start_host(folder);
    unsigned char *page = pp_map_pages(1U);
    pp_write_planted(folder, page);
    pp_signal_plant_read(page);
}

// Child: the stand-in owns no page, so it passes the fault, which lies in native code, to the
// handler it found, the last-resort handler, as Mono does with crash chaining off.
static void child_runtime_declines(const char *folder) {
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_start_host(folder);
    unsigned char *page = pp_map_pages(1U);
    pp_write_planted(folder, page);
    pp_signal_plant_read(page);
}

// Child: Mono ends a SIGILL in its crash path without chaining, so the dispatcher writes the
// record before it hands the fault on.
static void child_illegal_to_crash_path(const char *folder) {
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_start_host(folder);
    pp_write_planted(folder, NULL);
    pp_signal_plant_illegal();
}

// Child: Mono ends a fault in managed code above the first page in its crash path too.
static void child_managed_fault_to_crash_path(const char *folder) {
    atomic_store(&s_stand_in.in_managed_code, true);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_start_host(folder);
    unsigned char *page = pp_map_pages(1U);
    pp_write_planted(folder, page);
    pp_signal_plant_read(page);
}

// Child: SIGTRAP is ignored when pp_init runs. A breakpoint would trap again at once, so the
// dispatcher takes the ignored SIGTRAP for the default action, whatever code Darwin gives it.
// The alarm ends a child that loops instead.
static void child_ignored_breakpoint(const char *folder) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_IGN;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaction(SIGTRAP, &action, NULL), 0);
    pp_start_host(folder);
    pp_write_planted(folder, NULL);
    (void)alarm(PP_TEST_CHILD_ALARM_SECONDS);
    pp_signal_plant_breakpoint();
}

static void *fault_below_stack(void *argument) {
    const char *folder = argument;
    uintptr_t low = 0U;
    uintptr_t high = 0U;
    PP_CHECK(pp_thread_stack_bounds(&low, &high));
    // An address on the guard page below the thread's stack, where a deeper frame would have
    // landed, reached from the frame of this function, which has the whole stack to spare. A
    // local variable would not do: with detect_stack_use_after_return, AddressSanitizer keeps
    // locals on a stack of its own.
    const unsigned char *frame = __builtin_frame_address(0);
    PP_CHECK((uintptr_t)frame > low);
    PP_CHECK((uintptr_t)frame < high);
    const unsigned char *below = frame - (((uintptr_t)frame - low) + 64U);
    pp_write_planted(folder, below);
    pp_signal_plant_read(below);
    return NULL;
}

// Child: a fault just below the stack of an unmarked thread, which has no alternate stack, is
// told apart as a stack overflow. It simulates one: a real overflow there leaves the kernel no
// stack to run the handler on (signals.md section 7).
static void child_simulated_overflow(const char *folder) {
    pp_start_host(folder);
    pp_run_on_thread(fault_below_stack, (void *)folder);
}

// Child: the main thread runs out of stack. The dispatcher runs on the main thread's alternate
// stack and hands the overflow to the last-resort handler, which writes its record.
static void child_main_thread_overflow(const char *folder) {
    pp_start_host(folder);
    uintptr_t low = 0U;
    uintptr_t high = 0U;
    PP_CHECK(pp_thread_stack_bounds(&low, &high));
    pp_write_planted_value(folder, (uint64_t)low);
    volatile bool going_on = true;
    pp_exhaust_stack(&going_on);
}

// Child: the same with a runtime that ends the overflow in its crash path, as Mono does in
// managed code. The overflow goes to the runtime (C-0005 section 4), with its record first.
static void child_main_thread_overflow_to_crash_path(const char *folder) {
    atomic_store(&s_stand_in.in_managed_code, true);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    child_main_thread_overflow(folder);
}

// The crash record writer's hook: it holds each write between its temporary file and the
// rename, as a slow fsync would.
static void hold_writer(void) {
    const uint32_t milliseconds = atomic_load(&s_writer_hold_milliseconds);
    struct timespec pause = {
        .tv_sec = (time_t)(milliseconds / 1000U),
        .tv_nsec = (long)(milliseconds % 1000U) * 1000000L,
    };
    while (nanosleep(&pause, &pause) != 0 && errno == EINTR) {
    }
}

static void *fault_on_page(void *argument) {
    pp_signal_plant_read(argument);
    return NULL;
}

static void set_default_action(int signal_number) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_DFL;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaction(signal_number, &action, NULL), 0);
}

// A thread's fault ends the app while the writer holds its record; then the main thread faults
// too. Both signals have the default action, so each fault ends the app with a record.
static void fault_while_writer_holds(const char *folder, uint32_t hold_milliseconds) {
    set_default_action(SIGSEGV);
    set_default_action(SIGBUS);
    pp_start_host(folder);
    atomic_store(&s_writer_hold_milliseconds, hold_milliseconds);
    pp_crash_record_test_set_write_hook(hold_writer);
    unsigned char *page = pp_map_pages(1U);
    pp_write_planted(folder, page);
    (void)pp_start_thread(fault_on_page, page);
    while (pp_crash_record_writer_idle()) {
        pp_sleep_milliseconds(1);
    }
    uint64_t address = 0U;
    pp_raise_signal(SIGSEGV, &address);
}

// Child: the writer holds the first record for half a second. The main thread's fault waits for
// it, so the first record stays.
static void child_busy_writer(const char *folder) { fault_while_writer_holds(folder, 500U); }

// Child: the writer holds the first record for longer than the wait, which gives up.
static void child_stuck_writer(const char *folder) { fault_while_writer_holds(folder, 30000U); }

static int64_t elapsed_milliseconds(const struct timespec *start) {
    struct timespec now;
    PP_CHECK_EQUAL(clock_gettime(CLOCK_MONOTONIC, &now), 0);
    return (((int64_t)now.tv_sec - (int64_t)start->tv_sec) * 1000) +
           (((int64_t)now.tv_nsec - (int64_t)start->tv_nsec) / 1000000);
}

static void busy_writer_keeps_first_record(void) {
    const pp_child_end_t expected = {
        .ended_by = PP_TEST_ENDED_BY_FAULT,
        .signal = SIGBUS,
        .role = PP_THREAD_ROLE_OTHER,
        .message = PP_TEST_MESSAGE_DEFAULT,
    };
    pp_check_child_end(s_program, "busy_writer", &expected);
}

// The second fault waits for the writer, but no longer than the dispatcher's bound of two
// seconds, and ends the app without a record of its own while the first is still held.
static void stuck_writer_wait_is_bounded(void) {
    char folder[64];
    pp_make_folder(folder, sizeof folder);
    struct timespec start;
    PP_CHECK_EQUAL(clock_gettime(CLOCK_MONOTONIC, &start), 0);
    const int status = pp_run_child(s_program, "stuck_writer", folder);
    const int64_t elapsed = elapsed_milliseconds(&start);
    pp_check_ended_by(status, SIGSEGV);
    PP_CHECK(elapsed >= 2000);
    PP_CHECK(elapsed < 20000);
    PP_CHECK(!pp_has_crash_record(folder));
    (void)pp_read_planted(folder);
    pp_remove_record_folder(folder);
}

static void default_action_ends_with_record(void) {
    const pp_child_end_t expected = {
        .signal = SIGBUS,
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_DEFAULT,
    };
    pp_check_child_end(s_program, "default_action", &expected);
}

static void runtime_declined_fault_ends_with_record(void) {
    const pp_child_end_t expected = {
        .signal = SIGBUS,
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_LAST_RESORT,
    };
    pp_check_child_end(s_program, "runtime_declines", &expected);
}

static void crash_path_illegal_keeps_record(void) {
    const pp_child_end_t expected = {
        .ended_by = SIGABRT,
        .signal = SIGILL,
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_CRASH_PATH,
        .any_code = true,
    };
    pp_check_child_end(s_program, "illegal_to_crash_path", &expected);
}

static void crash_path_managed_fault_keeps_record(void) {
    const pp_child_end_t expected = {
        .ended_by = SIGABRT,
        .signal = SIGBUS,
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_CRASH_PATH,
    };
    pp_check_child_end(s_program, "managed_fault_to_crash_path", &expected);
}

static void ignored_breakpoint_ends_with_record(void) {
    const pp_child_end_t expected = {
        .signal = SIGTRAP,
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_DEFAULT,
        .any_code = true,
    };
    pp_check_child_end(s_program, "ignored_breakpoint", &expected);
}

static void simulated_overflow_ends_with_record(void) {
    const pp_child_end_t expected = {
        .signal = SIGBUS,
        .role = PP_THREAD_ROLE_OTHER,
        .message = PP_TEST_MESSAGE_OVERFLOW,
    };
    pp_check_child_end(s_program, "simulated_overflow", &expected);
}

static void main_thread_overflow_ends_with_record(void) {
    const pp_child_end_t expected = {
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_OVERFLOW,
        .below_stack = true,
    };
    pp_check_child_end(s_program, "main_thread_overflow", &expected);
}

static void main_thread_overflow_reaches_runtime(void) {
    const pp_child_end_t expected = {
        .ended_by = SIGABRT,
        .role = PP_THREAD_ROLE_MAIN,
        .message = PP_TEST_MESSAGE_OVERFLOW,
        .below_stack = true,
    };
    pp_check_child_end(s_program, "main_thread_overflow_to_crash_path", &expected);
}

int main(int argc, char **argv) {
    s_program = argv[0];
    static const pp_test_case_t cases[] = {
        {"every_signal_reaches_runtime_once", every_signal_reaches_runtime_once},
        {"plain_handler_gets_signal", plain_handler_gets_signal},
        {"ignored_sent_signal_returns", ignored_sent_signal_returns},
        {"ignored_with_siginfo_returns", ignored_with_siginfo_returns},
        {"first_page_fault_needs_no_record", first_page_fault_needs_no_record},
        {"default_action_ends_with_record", default_action_ends_with_record},
        {"runtime_declined_fault_ends_with_record", runtime_declined_fault_ends_with_record},
        {"crash_path_illegal_keeps_record", crash_path_illegal_keeps_record},
        {"crash_path_managed_fault_keeps_record", crash_path_managed_fault_keeps_record},
        {"ignored_breakpoint_ends_with_record", ignored_breakpoint_ends_with_record},
        {"simulated_overflow_ends_with_record", simulated_overflow_ends_with_record},
        {"main_thread_overflow_ends_with_record", main_thread_overflow_ends_with_record},
        {"main_thread_overflow_reaches_runtime", main_thread_overflow_reaches_runtime},
        {"busy_writer_keeps_first_record", busy_writer_keeps_first_record},
        {"stuck_writer_wait_is_bounded", stuck_writer_wait_is_bounded},
    };
    static const pp_child_case_t children[] = {
        {"default_action", child_default_action},
        {"runtime_declines", child_runtime_declines},
        {"illegal_to_crash_path", child_illegal_to_crash_path},
        {"managed_fault_to_crash_path", child_managed_fault_to_crash_path},
        {"ignored_breakpoint", child_ignored_breakpoint},
        {"simulated_overflow", child_simulated_overflow},
        {"main_thread_overflow", child_main_thread_overflow},
        {"main_thread_overflow_to_crash_path", child_main_thread_overflow_to_crash_path},
        {"busy_writer", child_busy_writer},
        {"stuck_writer", child_stuck_writer},
    };
    return pp_signal_test_main(argc, argv, cases, PP_CASE_COUNT(cases), children,
                               PP_CASE_COUNT(children));
}
