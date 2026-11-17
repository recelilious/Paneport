// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.1: installing the dispatcher, saving the previous actions and chaining to them. A
// stand-in for the runtime's handler is installed before pp_init, as the runtime's is on the
// device; each fault on an unmarked thread must reach it exactly once, with the siginfo that
// it receives without the dispatcher. The cases that end by a signal run in a child process,
// and their parent reads the crash record they leave (docs/design/signals.md section 10).

#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdint.h>
#include <string.h>

#include "crash_record.h"
#include "platform.h"
#include "pp_check.h"
#include "signal_test_support.h"
#include "test_support.h"

// The documented singletons of this program (C-0010 section 3): what the handlers saw, because
// a signal handler receives no argument of the test, and the path that the program started
// with, which a case needs to start a child.
static pp_stand_in_t s_stand_in;
static struct {
    _Atomic(uint64_t) count;
    _Atomic(int) signal;
    _Atomic(bool) mask_applied;
} s_plain;
static const char *s_program;

static void stand_in_handler(int signal_number, siginfo_t *info, void *context) {
    pp_stand_in_handle(&s_stand_in, signal_number, info, context);
}

// A plain handler, without SA_SIGINFO, whose action blocks SIGUSR2 while it runs.
static void plain_handler(int signal_number) {
    sigset_t blocked;
    (void)pthread_sigmask(SIG_BLOCK, NULL, &blocked);
    atomic_store(&s_plain.mask_applied, sigismember(&blocked, SIGUSR2) == 1);
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
    sigset_t blocked;
    PP_CHECK_EQUAL(pthread_sigmask(SIG_BLOCK, NULL, &blocked), 0);
    PP_CHECK_EQUAL(sigismember(&blocked, SIGUSR2), 0);
    return NULL;
}

// A plain handler gets the signal number, with its action's mask applied while it runs and
// taken away afterwards.
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

// Child: the stand-in owns no page, so it passes the fault to the handler it found, the
// last-resort handler, as Mono does with crash chaining off.
static void child_runtime_declines(const char *folder) {
    atomic_store(&s_stand_in.owns_all, false);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_start_host(folder);
    unsigned char *page = pp_map_pages(1U);
    pp_write_planted(folder, page);
    pp_signal_plant_read(page);
}

static void *overflow_stack(void *argument) {
    const char *folder = argument;
    uintptr_t low = 0U;
    uintptr_t high = 0U;
    PP_CHECK(pp_platform_stack_bounds(&low, &high));
    // An address on the guard page below the thread's stack, where a deeper frame would have
    // landed, reached from a local variable on the stack.
    const unsigned char *local = (const unsigned char *)&low;
    PP_CHECK((uintptr_t)local > low && (uintptr_t)local < high);
    const unsigned char *below = local - (((uintptr_t)local - low) + 64U);
    pp_write_planted(folder, below);
    pp_signal_plant_read(below);
    return NULL;
}

// Child: a fault just below a thread's stack ends the app as a stack overflow.
static void child_stack_overflow(const char *folder) {
    pp_start_host(folder);
    pp_run_on_thread(overflow_stack, (void *)folder);
}

static void check_child_record(const char *child_case, int signal_number, uint32_t role,
                               const char *message) {
    char folder[64];
    pp_make_folder(folder, sizeof folder);
    pp_check_ended_by(pp_run_child(s_program, child_case, folder), signal_number);
    const uint64_t planted = pp_read_planted(folder);
    pp_test_record_t record;
    pp_read_crash_record(folder, &record);
    PP_CHECK_EQUAL(record.signal, signal_number);
    PP_CHECK(record.code > 0);
    PP_CHECK_EQUAL(record.thread_role, role);
    PP_CHECK_EQUAL(record.fault_address, planted);
    PP_CHECK(record.program_counter != 0U);
    PP_CHECK_TEXT(record.message, message);
    pp_remove_record_folder(folder);
}

static void default_action_ends_with_record(void) {
    check_child_record("default_action", SIGBUS, PP_THREAD_ROLE_MAIN,
                       "A fault outside the guest path that nothing handled.");
}

static void runtime_declined_fault_ends_with_record(void) {
    check_child_record("runtime_declines", SIGBUS, PP_THREAD_ROLE_MAIN,
                       "A fault that the runtime did not handle.");
}

static void stack_overflow_ends_with_record(void) {
    check_child_record("stack_overflow", SIGBUS, PP_THREAD_ROLE_OTHER,
                       "A stack overflow outside the guest path.");
}

int main(int argc, char **argv) {
    s_program = argv[0];
    static const pp_test_case_t cases[] = {
        {"every_signal_reaches_runtime_once", every_signal_reaches_runtime_once},
        {"plain_handler_gets_signal", plain_handler_gets_signal},
        {"ignored_sent_signal_returns", ignored_sent_signal_returns},
        {"default_action_ends_with_record", default_action_ends_with_record},
        {"runtime_declined_fault_ends_with_record", runtime_declined_fault_ends_with_record},
        {"stack_overflow_ends_with_record", stack_overflow_ends_with_record},
    };
    static const pp_child_case_t children[] = {
        {"default_action", child_default_action},
        {"runtime_declines", child_runtime_declines},
        {"stack_overflow", child_stack_overflow},
    };
    return pp_signal_test_main(argc, argv, cases, PP_CASE_COUNT(cases), children,
                               PP_CASE_COUNT(children));
}
