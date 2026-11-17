// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Everything of the signal dispatcher that runs in a signal handler (docs/design/signals.md
// sections 3 to 6): the dispatcher's handler, the routing reads, the calls of the guest and
// probe handlers, the chain to the saved actions, the end of the app with a crash record, and
// the last-resort handler that the host installs when it is loaded. It calls only functions
// that POSIX lists as async-signal-safe, the host's own pp_crash_record_write,
// pp_host_thread_role and pp_platform_stack_bounds, and the handlers it chains to. A CTest
// test lists the undefined symbols of this file's object with nm and fails on any other call
// (tests/native/check_signal_safe_calls.cmake).

#include <errno.h>
#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "crash_record.h"
#include "host_state.h"
#include "platform.h"
#include "signal_context.h"
#include "signal_state.h"
#include "signals.h"

// How far below the lowest address of a thread's stack a fault still counts as a stack overflow:
// the guard pages, and the frame of a function that skipped them.
#define PP_SIGNAL_STACK_OVERFLOW_REACH (UINT64_C(1024) * 1024U)

// The messages of the crash records that the dispatcher writes, at most 63 bytes each.
#define PP_MESSAGE_RUNTIME_DEFAULT "A fault outside the guest path that nothing handled."
#define PP_MESSAGE_LAST_RESORT "A fault that the runtime did not handle."
#define PP_MESSAGE_RUNTIME_CRASH "A fault that ended in the runtime's crash path."
#define PP_MESSAGE_GUEST_DECLINED "The guest fault handler declined a fault."
#define PP_MESSAGE_PROBE_DECLINED "The JIT probe handler declined a fault."
#define PP_MESSAGE_STACK_OVERFLOW "A stack overflow outside the guest path."

pp_signal_state_t pp_signal_state;

// Whether the kernel raised the signal for the faulting instruction. Such a signal carries one
// of the small positive codes of its signal, such as SEGV_MAPERR; a signal that a thread sent
// carries SI_USER or one of its kin, which Darwin numbers from 0x10001, or 0.
static bool from_instruction(const siginfo_t *info) {
    if (info == NULL || info->si_code <= 0) {
        return false;
    }
    return info->si_code < 0x10000;
}

static uint64_t fault_address(const siginfo_t *info) {
    return info != NULL ? (uint64_t)(uintptr_t)info->si_addr : 0U;
}

// The calling thread's mark. Only the thread itself writes its slot, so a thread that never
// marked itself finds none, whatever other threads do meanwhile.
static uint32_t current_mark(void) {
    const uintptr_t self = (uintptr_t)pthread_self();
    const uint32_t used = atomic_load_explicit(&pp_signal_state.threads_used, memory_order_acquire);
    for (uint32_t index = 0U; index < used && index < PP_SIGNAL_MARKED_THREADS; index++) {
        const pp_signal_thread_slot_t *slot = &pp_signal_state.threads[index];
        if (atomic_load_explicit(&slot->thread, memory_order_acquire) == self) {
            return atomic_load_explicit(&slot->mark, memory_order_relaxed);
        }
    }
    return PP_THREAD_MARK_NONE;
}

// Reads a slot as a whole. A slot that a writer is changing, or changed while it was read, is
// read again a few times and then taken as empty: a range is registered before anything can
// fault in it, and removed after.
static bool slot_contains(const pp_signal_range_slot_t *slot, uint64_t address) {
    for (int attempt = 0; attempt < PP_SIGNAL_READ_ATTEMPTS; attempt++) {
        const uint32_t before = atomic_load_explicit(&slot->sequence, memory_order_acquire);
        if ((before & 1U) != 0U) {
            continue;
        }
        const uint64_t start = atomic_load_explicit(&slot->start, memory_order_relaxed);
        const uint64_t end = atomic_load_explicit(&slot->end, memory_order_relaxed);
        atomic_thread_fence(memory_order_acquire);
        if (atomic_load_explicit(&slot->sequence, memory_order_relaxed) == before) {
            if (address < start) {
                return false;
            }
            return address < end;
        }
    }
    return false;
}

static bool table_contains(const pp_signal_range_slot_t *slots, uint32_t capacity,
                           const _Atomic(uint32_t) *used, uint64_t address) {
    const uint32_t count = atomic_load_explicit(used, memory_order_acquire);
    for (uint32_t index = 0U; index < count && index < capacity; index++) {
        if (slot_contains(&slots[index], address)) {
            return true;
        }
    }
    return false;
}

static bool in_pool(uint64_t address) {
    if (address == 0U) {
        return false;
    }
    return table_contains(pp_signal_state.pool, PP_SIGNAL_POOL_RANGES, &pp_signal_state.pool_used,
                          address);
}

static bool in_reservation(uint64_t address) {
    if (address == 0U) {
        return false;
    }
    return table_contains(pp_signal_state.reservations, PP_SIGNAL_RESERVATIONS,
                          &pp_signal_state.reservations_used, address);
}

// The route of C-0005 section 4. The address of a signal that a thread sent means nothing, so
// such a signal follows the thread's mark and the program counter only. A fault in the pool
// counts by its address or by its program counter, because translated code runs there.
static uint32_t choose_route(const siginfo_t *info, const void *context, uint32_t *mark) {
    uint64_t address = 0U;
    if (from_instruction(info)) {
        address = fault_address(info);
    }
    bool pool = in_pool(address);
    if (!pool) {
        pool = in_pool(pp_signal_context_pc(context));
    }
    *mark = current_mark();
    if (pool && atomic_load_explicit(&pp_signal_state.probe_mode, memory_order_acquire) != 0U) {
        return PP_FAULT_ROUTE_JIT_PROBE;
    }
    if (*mark != PP_THREAD_MARK_NONE) {
        return PP_FAULT_ROUTE_THREAD_MARK;
    }
    if (pool) {
        return PP_FAULT_ROUTE_JIT_POOL;
    }
    if (in_reservation(address)) {
        return PP_FAULT_ROUTE_RESERVATION;
    }
    return PP_FAULT_ROUTE_RUNTIME;
}

static uint32_t thread_role(uint32_t mark) {
    switch (mark) {
    case PP_THREAD_MARK_GUEST:
        return PP_THREAD_ROLE_GUEST;
    case PP_THREAD_MARK_TRANSLATOR:
        return PP_THREAD_ROLE_TRANSLATOR;
    default:
        return pp_host_thread_role();
    }
}

// Writes the crash record of a fault (C-0005 section 2).
static void write_record(int signal_number, const siginfo_t *info, const void *context,
                         uint32_t mark, const char *message) {
    const pp_crash_info_t crash = {
        .signal = signal_number,
        .code = info != NULL ? info->si_code : 0,
        .fault_address = fault_address(info),
        .program_counter = pp_signal_context_pc(context),
        .thread_role = thread_role(mark),
        .is_test = 0U,
        .message = message,
    };
    (void)pp_crash_record_write(&crash);
}

// Writes the crash record and ends the app by the same signal: the default action comes back,
// and the signal, raised again, is delivered with it once the handler returns (C-0005 section
// 2). A second fault while the first is written finds the writer busy and ends the app too.
static void end_app(int signal_number, const siginfo_t *info, const void *context, uint32_t mark,
                    const char *message) {
    write_record(signal_number, info, context, mark, message);

    struct sigaction action;
    (void)memset(&action, 0, sizeof action);
    action.sa_handler = SIG_DFL;
    (void)sigemptyset(&action.sa_mask);
    (void)sigaction(signal_number, &action, NULL);
    (void)raise(signal_number);
}

// Calls a handler box, unless writers switch boxes under the caller more often than
// PP_SIGNAL_READ_ATTEMPTS times, which counts as no handler. The count of callers keeps a
// writer from returning while the box it replaced is in use.
static uint32_t call_handler(pp_signal_handler_slot_t *slot, pp_guest_fault_t *fault) {
    for (int attempt = 0; attempt < PP_SIGNAL_READ_ATTEMPTS; attempt++) {
        const uint32_t index = atomic_load_explicit(&slot->active, memory_order_seq_cst) & 1U;
        pp_signal_handler_box_t *box = &slot->boxes[index];
        (void)atomic_fetch_add_explicit(&box->callers, 1U, memory_order_seq_cst);
        if (atomic_load_explicit(&slot->active, memory_order_seq_cst) == index) {
            const pp_guest_fault_handler_t handler =
                atomic_load_explicit(&box->handler, memory_order_relaxed);
            void *user_data = atomic_load_explicit(&box->user_data, memory_order_relaxed);
            uint32_t result = PP_GUEST_FAULT_DECLINED;
            if (handler != NULL) {
                result = handler(fault, user_data);
            }
            (void)atomic_fetch_sub_explicit(&box->callers, 1U, memory_order_release);
            return result == PP_GUEST_FAULT_HANDLED ? PP_GUEST_FAULT_HANDLED
                                                    : PP_GUEST_FAULT_DECLINED;
        }
        (void)atomic_fetch_sub_explicit(&box->callers, 1U, memory_order_release);
    }
    return PP_GUEST_FAULT_DECLINED;
}

static void to_guest(uint32_t route, uint32_t mark, int signal_number, siginfo_t *info,
                     void *context) {
    pp_guest_fault_t fault = {
        .struct_size = (uint32_t)sizeof(pp_guest_fault_t),
        .route = route,
        .thread_mark = mark,
        .signal = signal_number,
        .code = info != NULL ? info->si_code : 0,
        .fault_address = fault_address(info),
        .program_counter = pp_signal_context_pc(context),
        .signal_info = info,
        .context = context,
    };
    pp_signal_handler_slot_t *slot = &pp_signal_state.guest;
    _Atomic(uint64_t) *handled = &pp_signal_state.guest_handled_count;
    _Atomic(uint64_t) *declined = &pp_signal_state.guest_declined_count;
    const char *message = PP_MESSAGE_GUEST_DECLINED;
    if (route == PP_FAULT_ROUTE_JIT_PROBE) {
        slot = &pp_signal_state.probe;
        handled = &pp_signal_state.probe_handled_count;
        declined = &pp_signal_state.probe_declined_count;
        message = PP_MESSAGE_PROBE_DECLINED;
    }
    if (call_handler(slot, &fault) == PP_GUEST_FAULT_HANDLED) {
        (void)atomic_fetch_add_explicit(handled, 1U, memory_order_relaxed);
        return;
    }
    (void)atomic_fetch_add_explicit(declined, 1U, memory_order_relaxed);
    end_app(signal_number, info, context, mark, message);
}

// Whether a fault lies just below the calling thread's stack: a stack overflow, which the
// handler can only see on an alternate stack. The record names it, whichever handler writes it.
static bool stack_overflow(int signal_number, const siginfo_t *info) {
    if ((signal_number != SIGSEGV && signal_number != SIGBUS) || !from_instruction(info)) {
        return false;
    }
    uintptr_t low = 0U;
    uintptr_t high = 0U;
    if (!pp_platform_stack_bounds(&low, &high)) {
        return false;
    }
    const uint64_t address = fault_address(info);
    if (address >= low) {
        return false;
    }
    return low - address <= PP_SIGNAL_STACK_OVERFLOW_REACH;
}

// Runs a saved action's handler as the kernel would have: with the action's mask added to the
// blocked signals, and with the signal itself unblocked when the action asked for SA_NODEFER.
static void call_saved(const struct sigaction *saved, int signal_number, siginfo_t *info,
                       void *context) {
    sigset_t previous;
    (void)pthread_sigmask(SIG_BLOCK, &saved->sa_mask, &previous);
    if (pp_signal_has_flag(saved->sa_flags, SA_NODEFER)) {
        sigset_t own;
        (void)sigemptyset(&own);
        (void)sigaddset(&own, signal_number);
        (void)pthread_sigmask(SIG_UNBLOCK, &own, NULL);
    }
    if (pp_signal_has_flag(saved->sa_flags, SA_SIGINFO)) {
        saved->sa_sigaction(signal_number, info, context);
    } else {
        saved->sa_handler(signal_number);
    }
    (void)pthread_sigmask(SIG_SETMASK, &previous, NULL);
}

// Whether an ignored signal would come back at once: a fault of an instruction faults again
// when the handler returns, and Darwin gives the SIGTRAP of a breakpoint instruction the code of
// a sent signal, so an ignored SIGILL or SIGTRAP counts as one too.
static bool ignoring_repeats(int signal_number, const siginfo_t *info) {
    if (signal_number == SIGILL || signal_number == SIGTRAP) {
        return true;
    }
    return from_instruction(info);
}

// Whether the runtime's handler may end the app with a fault in its own crash path, which
// writes no record: with crash chaining off, Mono does so for every SIGILL, and for a SIGSEGV or
// SIGBUS in managed code above the page of its null-reference checks, a stack overflow among
// them (signals.md section 5). Which code faulted, the handler cannot tell, so every such
// SIGSEGV or SIGBUS counts.
static bool runtime_may_end(int signal_number, const siginfo_t *info) {
    if (signal_number == SIGILL) {
        return true;
    }
    if ((signal_number != SIGSEGV && signal_number != SIGBUS) || !from_instruction(info)) {
        return false;
    }
    return fault_address(info) > PP_SIGNAL_RUNTIME_NULL_LIMIT;
}

// Hands a fault to the action that the signal had before the dispatcher, with the original
// arguments. The default action ends the app with a record; ignore returns, unless the signal
// would come back at once, which counts as the default action. The handler of the action is
// checked before its flags, which may be stale (pp_signal_is_default). A fault that the
// runtime's crash path may end gets its record first; when the runtime chains to the
// last-resort handler instead, that handler writes the record again.
static void to_runtime(int signal_number, siginfo_t *info, void *context) {
    (void)atomic_fetch_add_explicit(&pp_signal_state.runtime_count, 1U, memory_order_relaxed);
    const bool overflow = stack_overflow(signal_number, info);
    const char *message = PP_MESSAGE_RUNTIME_DEFAULT;
    const char *first_message = PP_MESSAGE_RUNTIME_CRASH;
    if (overflow) {
        message = PP_MESSAGE_STACK_OVERFLOW;
        first_message = PP_MESSAGE_STACK_OVERFLOW;
    }

    const int index = pp_signal_index(signal_number);
    if (index < 0 || !atomic_load_explicit(&pp_signal_state.installed, memory_order_acquire)) {
        end_app(signal_number, info, context, PP_THREAD_MARK_NONE, message);
        return;
    }
    const struct sigaction *saved = &pp_signal_state.saved[index];
    if (pp_signal_is_ignored(saved)) {
        if (ignoring_repeats(signal_number, info)) {
            end_app(signal_number, info, context, PP_THREAD_MARK_NONE, message);
        }
        return;
    }
    if (pp_signal_is_default(saved) || saved->sa_sigaction == pp_signal_dispatch) {
        end_app(signal_number, info, context, PP_THREAD_MARK_NONE, message);
        return;
    }
    if (saved->sa_sigaction != pp_signal_last_resort && runtime_may_end(signal_number, info)) {
        write_record(signal_number, info, context, PP_THREAD_MARK_NONE, first_message);
    }
    call_saved(saved, signal_number, info, context);
}

void pp_signal_dispatch(int signal_number, siginfo_t *info, void *context) {
    const int saved_errno = errno;
    uint32_t mark = PP_THREAD_MARK_NONE;
    const uint32_t route = choose_route(info, context, &mark);
    if (route == PP_FAULT_ROUTE_RUNTIME) {
        to_runtime(signal_number, info, context);
    } else {
        to_guest(route, mark, signal_number, info, context);
    }
    errno = saved_errno;
}

// The handler that the runtime's own handler chains to for a fault it does not take, such as a
// fault in native code: with Mono's crash chaining off, Mono calls the handler that it found
// when it started, before its own crash path (signals.md section 5).
void pp_signal_last_resort(int signal_number, siginfo_t *info, void *context) {
    const int saved_errno = errno;
    const char *message = PP_MESSAGE_LAST_RESORT;
    if (stack_overflow(signal_number, info)) {
        message = PP_MESSAGE_STACK_OVERFLOW;
    }
    end_app(signal_number, info, context, current_mark(), message);
    errno = saved_errno;
}

// Installs the last-resort handler for each of the six signals that still has the default
// action when the host is loaded: before main, and so before the runtime installs its own
// handlers and saves what it found. A signal that already has another action keeps it, as in
// a test host that loads the host's library after its runtime started.
__attribute__((constructor)) static void install_last_resort(void) {
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        const int signal_number = pp_signal_number(index);
        struct sigaction current;
        if (sigaction(signal_number, NULL, &current) != 0 || !pp_signal_is_default(&current)) {
            continue;
        }
        struct sigaction action;
        (void)memset(&action, 0, sizeof action);
        action.sa_sigaction = pp_signal_last_resort;
        action.sa_flags = pp_signal_action_flags();
        (void)sigemptyset(&action.sa_mask);
        (void)sigaction(signal_number, &action, NULL);
    }
}
