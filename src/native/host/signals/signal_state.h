// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The state that the dispatcher's handler reads and the registration functions write, shared by
// the files of this folder only. It is one process-wide singleton (C-0010 section 3),
// pp_signal_state, defined in signal_handler.c. Every member that the handler reads is atomic,
// or is written once before the handler can run; signals.md section 4 describes how writers
// publish.

#ifndef PANEPORT_HOST_SIGNAL_STATE_H
#define PANEPORT_HOST_SIGNAL_STATE_H

#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "signals.h"

// How often the handler reads a slot that a writer is changing before it takes the slot as
// empty, and how often it tries to enter a handler box while writers switch boxes.
#define PP_SIGNAL_READ_ATTEMPTS 4

// One marked thread. Only the thread itself claims, changes and frees its slot, under the
// mutex of the thread table; the handler on that thread compares the slot's thread with
// pthread_self().
typedef struct pp_signal_thread_slot_t {
    _Atomic(uintptr_t) thread; // pthread_self() of the marked thread, or 0 when the slot is free.
    _Atomic(uint32_t) mark;    // A PP_THREAD_MARK_ value.
    // The alternate signal stack that pp_signals_mark_thread allocated for the thread, or NULL;
    // only the thread itself reads and writes these.
    void *stack;
    size_t stack_size;
} pp_signal_thread_slot_t;

// One registered range. Writers change a slot under the mutex of the range tables: they make
// sequence odd, store the bounds, and make it even again (a sequence lock).
typedef struct pp_signal_range_slot_t {
    _Atomic(uint32_t) sequence;
    _Atomic(uint64_t) start;
    _Atomic(uint64_t) end; // Exclusive; 0 when the slot is free.
    uint32_t generation;   // Part of the handle; only writers read it.
} pp_signal_range_slot_t;

// One handler with its user data. A caller counts itself in callers before it reads the box.
typedef struct pp_signal_handler_box_t {
    _Atomic(pp_guest_fault_handler_t) handler;
    _Atomic(void *) user_data;
    _Atomic(uint32_t) callers;
} pp_signal_handler_box_t;

// A handler that writers replace without stopping the readers: they fill the box that is not
// active, switch active to it, and wait until no caller is left in the other box.
typedef struct pp_signal_handler_slot_t {
    pp_signal_handler_box_t boxes[2];
    _Atomic(uint32_t) active;
} pp_signal_handler_slot_t;

typedef struct pp_signal_state_t {
    // The actions that the six signals had before the dispatcher, in the order of
    // pp_signal_number(); written by pp_signals_install before it sets installed.
    struct sigaction saved[PP_SIGNAL_COUNT];
    _Atomic(bool) installed;

    pp_signal_thread_slot_t threads[PP_SIGNAL_MARKED_THREADS];
    _Atomic(uint32_t) threads_used; // One past the highest slot ever claimed.

    pp_signal_range_slot_t pool[PP_SIGNAL_POOL_RANGES];
    _Atomic(uint32_t) pool_used;
    pp_signal_range_slot_t reservations[PP_SIGNAL_RESERVATIONS];
    _Atomic(uint32_t) reservations_used;
    _Atomic(uint8_t) probe_mode;

    pp_signal_handler_slot_t guest;
    pp_signal_handler_slot_t probe;

    _Atomic(uint64_t) runtime_count;
    _Atomic(uint64_t) guest_handled_count;
    _Atomic(uint64_t) guest_declined_count;
    _Atomic(uint64_t) probe_handled_count;
    _Atomic(uint64_t) probe_declined_count;
} pp_signal_state_t;

extern pp_signal_state_t pp_signal_state;

// The dispatcher's handler and the last-resort handler of signal_handler.c.
void pp_signal_dispatch(int signal_number, siginfo_t *info, void *context);
void pp_signal_last_resort(int signal_number, siginfo_t *info, void *context);

// Gives the calling thread an alternate signal stack unless it has one. Sets *stack and
// *size to the stack it allocated, or to NULL and 0 when the thread had one already. Returns
// PP_OK; PP_E_OUT_OF_MEMORY; PP_E_SYSTEM.
pp_status_t pp_signal_stack_ensure(void **stack, size_t *size);

// Takes away the alternate stack that pp_signal_stack_ensure allocated, if it is still the
// calling thread's, and frees it. Does nothing for NULL.
void pp_signal_stack_release(void *stack, size_t size);

// The six signals, by their index in pp_signal_state.saved.
static inline int pp_signal_number(size_t index) {
    switch (index) {
    case 0U:
        return SIGSEGV;
    case 1U:
        return SIGBUS;
    case 2U:
        return SIGILL;
    case 3U:
        return SIGFPE;
    case 4U:
        return SIGTRAP;
    case 5U:
        return SIGABRT;
    default:
        return 0;
    }
}

// The index of one of the six signals, or -1.
static inline int pp_signal_index(int signal_number) {
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        if (pp_signal_number(index) == signal_number) {
            return (int)index;
        }
    }
    return -1;
}

// Whether flags of type int, such as sa_flags or ss_flags, hold a flag; the bits are compared
// unsigned.
static inline bool pp_signal_has_flag(int flags, int flag) {
    return ((unsigned)flags & (unsigned)flag) != 0U;
}

// The flags of the dispatcher's and the last-resort handler's actions.
static inline int pp_signal_action_flags(void) {
    return (int)((unsigned)SA_SIGINFO | (unsigned)SA_ONSTACK);
}

// Whether an action is the default action. The handler alone tells: Darwin keeps SA_SIGINFO in
// the flags of a signal whose handler exec reset to the default, so a process that another one
// started with posix_spawn may find SIG_DFL with SA_SIGINFO set.
static inline bool pp_signal_is_default(const struct sigaction *action) {
    return action->sa_handler == SIG_DFL;
}

static inline bool pp_signal_is_ignored(const struct sigaction *action) {
    return action->sa_handler == SIG_IGN;
}

#endif // PANEPORT_HOST_SIGNAL_STATE_H
