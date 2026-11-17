// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Installing the dispatcher, the installed check and the alternate signal stacks
// (docs/design/signals.md sections 3 and 7). None of this runs in a signal handler.

#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <sys/mman.h>
#include <unistd.h>

#include "error.h"
#include "signal_state.h"
#include "signals.h"

// The alternate stack that pp_signals_install gave the thread that installed the dispatcher,
// the main thread, and that pp_signals_uninstall takes away again: a process-wide singleton
// (C-0010 section 3), which only that thread reads and writes.
static struct {
    void *stack;
    size_t size;
} s_main_stack;

// Whether two actions name the same handler. sa_handler and sa_sigaction share their storage,
// so the handler compares as one whatever the flags say.
static bool same_action(const struct sigaction *first, const struct sigaction *second) {
    return first->sa_handler == second->sa_handler;
}

// Puts the saved actions of the first count signals back.
static void restore_saved(size_t count) {
    for (size_t index = 0U; index < count && index < PP_SIGNAL_COUNT; index++) {
        (void)sigaction(pp_signal_number(index), &pp_signal_state.saved[index], NULL);
    }
}

// Installs the dispatcher for each signal. An action that changed between reading it and
// replacing it would leave the dispatcher chaining to the wrong handler, so that undoes all.
static pp_status_t install_dispatcher(void) {
    struct sigaction action;
    (void)memset(&action, 0, sizeof action);
    action.sa_sigaction = pp_signal_dispatch;
    action.sa_flags = pp_signal_action_flags();
    (void)sigemptyset(&action.sa_mask);

    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        const int signal_number = pp_signal_number(index);
        struct sigaction replaced;
        if (sigaction(signal_number, &action, &replaced) != 0) {
            restore_saved(index);
            return pp_error_set(PP_E_SYSTEM, "The system refused the dispatcher's handler of %s.",
                                pp_signals_name(signal_number));
        }
        if (!same_action(&replaced, &pp_signal_state.saved[index])) {
            restore_saved(index + 1U);
            return pp_error_set(PP_E_SYSTEM,
                                "The action of %s changed while the dispatcher was installed.",
                                pp_signals_name(signal_number));
        }
    }
    return PP_OK;
}

pp_status_t pp_signals_install(void) {
    if (atomic_load_explicit(&pp_signal_state.installed, memory_order_acquire)) {
        return pp_error_set(PP_E_ALREADY_INITIALIZED,
                            "The signal dispatcher is installed already.");
    }

    // The previous actions are saved before the dispatcher can run, because it reads them as
    // soon as it is installed.
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        if (sigaction(pp_signal_number(index), NULL, &pp_signal_state.saved[index]) != 0) {
            return pp_error_set(PP_E_SYSTEM, "The action of %s could not be read.",
                                pp_signals_name(pp_signal_number(index)));
        }
    }
    atomic_store_explicit(&pp_signal_state.installed, true, memory_order_release);

    pp_status_t status = install_dispatcher();
    if (status == PP_OK) {
        // The main thread's stack stays for the life of the process, unless pp_init fails
        // after this and uninstalls the dispatcher again.
        status = pp_signal_stack_ensure(&s_main_stack.stack, &s_main_stack.size);
        if (status != PP_OK) {
            restore_saved(PP_SIGNAL_COUNT);
        }
    }
    if (status != PP_OK) {
        atomic_store_explicit(&pp_signal_state.installed, false, memory_order_release);
    }
    return status;
}

void pp_signals_uninstall(void) {
    if (atomic_load_explicit(&pp_signal_state.installed, memory_order_acquire)) {
        restore_saved(PP_SIGNAL_COUNT);
        atomic_store_explicit(&pp_signal_state.installed, false, memory_order_release);
        pp_signal_stack_release(s_main_stack.stack, s_main_stack.size);
        s_main_stack.stack = NULL;
        s_main_stack.size = 0U;
    }
}

pp_status_t pp_signals_check_installed(int32_t *replaced_signal) {
    if (replaced_signal != NULL) {
        *replaced_signal = 0;
    }
    if (!atomic_load_explicit(&pp_signal_state.installed, memory_order_acquire)) {
        return pp_error_set(PP_E_NOT_INITIALIZED,
                            "The signal dispatcher is not installed: pp_init installs it.");
    }
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        const int signal_number = pp_signal_number(index);
        struct sigaction current;
        const bool read = sigaction(signal_number, NULL, &current) == 0;
        if (!read || !pp_signal_has_flag(current.sa_flags, SA_SIGINFO) ||
            current.sa_sigaction != pp_signal_dispatch) {
            if (replaced_signal != NULL) {
                *replaced_signal = signal_number;
            }
            return pp_error_set(PP_E_SYSTEM, "%s no longer has the signal dispatcher's handler.",
                                pp_signals_name(signal_number));
        }
    }
    return PP_OK;
}

const char *pp_signals_name(int32_t signal) {
    switch (signal) {
    case SIGSEGV:
        return "SIGSEGV";
    case SIGBUS:
        return "SIGBUS";
    case SIGILL:
        return "SIGILL";
    case SIGFPE:
        return "SIGFPE";
    case SIGTRAP:
        return "SIGTRAP";
    case SIGABRT:
        return "SIGABRT";
    default:
        return "an unknown signal";
    }
}

pp_status_t pp_signal_stack_ensure(void **stack, size_t *size) {
    *stack = NULL;
    *size = 0U;
    stack_t current;
    if (sigaltstack(NULL, &current) != 0) {
        return pp_error_set(PP_E_SYSTEM, "The thread's alternate signal stack could not be read.");
    }
    // A stack that another part of the process set, such as a sanitizer's or the Wine fork's,
    // stays: whoever set it frees it.
    if (!pp_signal_has_flag(current.ss_flags, SS_DISABLE)) {
        return PP_OK;
    }

    const long page = sysconf(_SC_PAGESIZE);
    if (page <= 0) {
        return pp_error_set(PP_E_SYSTEM, "The system did not report its page size.");
    }
    const size_t guard = (size_t)page;
    const size_t mapped = guard + PP_SIGNAL_STACK_SIZE;
    unsigned char *memory =
        mmap(NULL, mapped, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
    if (memory == MAP_FAILED) {
        return pp_error_set(PP_E_OUT_OF_MEMORY, "The alternate signal stack could not be mapped.");
    }
    // A handler that overflows the stack faults on the guard page instead of writing below it.
    if (mprotect(memory, guard, PROT_NONE) != 0) {
        (void)munmap(memory, mapped);
        return pp_error_set(PP_E_SYSTEM, "The guard page of the alternate signal stack could not "
                                         "be set.");
    }

    const stack_t replacement = {
        .ss_sp = memory + guard,
        .ss_size = PP_SIGNAL_STACK_SIZE,
        .ss_flags = 0,
    };
    if (sigaltstack(&replacement, NULL) != 0) {
        (void)munmap(memory, mapped);
        return pp_error_set(PP_E_SYSTEM, "The alternate signal stack could not be set.");
    }
    *stack = memory;
    *size = mapped;
    return PP_OK;
}

void pp_signal_stack_release(void *stack, size_t size) {
    if (stack == NULL) {
        return;
    }
    stack_t current;
    const unsigned char *start = stack;
    if (sigaltstack(NULL, &current) == 0 && !pp_signal_has_flag(current.ss_flags, SS_DISABLE) &&
        (const unsigned char *)current.ss_sp > start &&
        (const unsigned char *)current.ss_sp < start + size) {
        const stack_t disabled = {.ss_sp = NULL, .ss_size = 0U, .ss_flags = SS_DISABLE};
        (void)sigaltstack(&disabled, NULL);
    }
    (void)munmap(stack, size);
}
