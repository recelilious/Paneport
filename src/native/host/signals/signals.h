// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The signal dispatcher of C-0005 section 4: the process's single handler of SIGSEGV, SIGBUS,
// SIGILL, SIGFPE, SIGTRAP and SIGABRT, which pp_init installs. A fault takes the guest path
// when its thread carries a guest or translator mark, or when it lies in a registered JIT pool
// range or guest reservation; while the probe mode is on, a fault in the pool goes to the JIT
// probe instead. Every other fault goes to the action that was installed before the
// dispatcher, which is the .NET runtime's. docs/design/signals.md describes it.
//
// The registration functions follow the conventions of C-0005 section 1, so that the Wine
// bridge (paneport/wine_bridge.h, M4.3) can offer them unchanged. They run on any thread,
// outside signal context: no signal handler, guest fault handler or probe handler may call
// them. Their writers serialize on a mutex, and the dispatcher reads what they publish without
// a lock and without allocating.

#ifndef PANEPORT_HOST_SIGNALS_H
#define PANEPORT_HOST_SIGNALS_H

#include <stdbool.h>
#include <stdint.h>

#include "paneport/common.h"

// The number of signals that the dispatcher owns.
#define PP_SIGNAL_COUNT 6

// The capacities of the routing tables (signals.md section 4). A registration that finds its
// table full fails with PP_E_OUT_OF_MEMORY.
#define PP_SIGNAL_MARKED_THREADS 1024U
#define PP_SIGNAL_POOL_RANGES 64U
#define PP_SIGNAL_RESERVATIONS 256U

// The marks of a thread whose faults take the guest path.
enum {
    PP_THREAD_MARK_NONE = 0,
    PP_THREAD_MARK_GUEST = 1,
    PP_THREAD_MARK_TRANSLATOR = 2,
};

// The routes of a fault. A guest fault handler sees the three guest routes, and the probe
// handler the probe route; the runtime route is counted only.
enum {
    PP_FAULT_ROUTE_RUNTIME = 0,
    PP_FAULT_ROUTE_THREAD_MARK = 1,
    PP_FAULT_ROUTE_JIT_POOL = 2,
    PP_FAULT_ROUTE_RESERVATION = 3,
    PP_FAULT_ROUTE_JIT_PROBE = 4,
};

// What a guest fault handler or the probe handler returns.
enum {
    PP_GUEST_FAULT_DECLINED = 0,
    PP_GUEST_FAULT_HANDLED = 1,
};

// A fault on its way to a guest fault handler or to the probe handler. It lives on the stack of
// the signal handler and is valid only until the handler returns.
typedef struct pp_guest_fault_t {
    uint32_t struct_size;     // sizeof(pp_guest_fault_t).
    uint32_t route;           // A PP_FAULT_ROUTE_ value.
    uint32_t thread_mark;     // The faulting thread's PP_THREAD_MARK_ value.
    int32_t signal;           // The signal number.
    int32_t code;             // The signal's si_code.
    uint64_t fault_address;   // si_addr.
    uint64_t program_counter; // The program counter of the context.
    void *signal_info;        // The kernel's siginfo_t.
    void *context;            // The kernel's ucontext_t, which a handler may change.
} pp_guest_fault_t;

// Handles a fault of the guest path in the faulting thread's signal context, so it may call
// only async-signal-safe functions and must return. It returns PP_GUEST_FAULT_HANDLED when the
// thread may go on with the context as the handler left it, or PP_GUEST_FAULT_DECLINED, which
// ends the app with a crash record; a handler that declines leaves the context unchanged. The
// signal stays unblocked while it runs: a fault inside the handler on a marked thread ends the
// app with a record of its own, and on another thread is routed again like any fault.
typedef uint32_t (*pp_guest_fault_handler_t)(pp_guest_fault_t *fault, void *user_data);

// How many faults the dispatcher has sent each way since the process started.
typedef struct pp_signal_counters_t {
    uint32_t struct_size;    // sizeof(pp_signal_counters_t).
    uint64_t runtime;        // Passed to the runtime's saved action.
    uint64_t guest_handled;  // Handled by the guest fault handler.
    uint64_t guest_declined; // Declined by it, or with no handler set.
    uint64_t probe_handled;  // Handled by the probe handler.
    uint64_t probe_declined; // Declined by it, or with no handler set.
} pp_signal_counters_t;

// Installs the dispatcher for the six signals and saves the action that each had, and gives
// the calling thread an alternate signal stack unless it has one. pp_init calls it once, on
// the main thread. Returns PP_OK; PP_E_ALREADY_INITIALIZED; PP_E_OUT_OF_MEMORY when the stack
// cannot be allocated; PP_E_SYSTEM when the system refuses an action, or when an action
// changed while the dispatcher was installed. On an error nothing stays installed.
pp_status_t pp_signals_install(void);

// Puts the saved actions back. pp_init calls it when it fails after pp_signals_install.
void pp_signals_uninstall(void);

// Checks that each of the six signals still has the dispatcher's handler. Test builds run it
// at the end of pp_init, and M4.3 after pp_session_start (C-0005 section 4). Sets
// *replaced_signal, unless it is NULL, to 0 or to the first signal whose handler was replaced.
// Returns PP_OK; PP_E_NOT_INITIALIZED before pp_signals_install; PP_E_SYSTEM, with a message
// that names the signal, when a handler was replaced.
pp_status_t pp_signals_check_installed(int32_t *replaced_signal);

// Returns the name of one of the six signals, such as "SIGSEGV", or "an unknown signal".
const char *pp_signals_name(int32_t signal);

// Marks the calling thread as a guest or translator thread, or changes its mark; it keeps the
// mark until pp_signals_unmark_thread or until it ends. The thread gets an alternate signal
// stack unless it has one. Returns PP_OK; PP_E_INVALID_ARGUMENT for another mark;
// PP_E_OUT_OF_MEMORY when the table of marked threads is full or the stack cannot be
// allocated; PP_E_SYSTEM.
pp_status_t pp_signals_mark_thread(uint32_t mark);

// Takes the calling thread's mark away, if it has one, and frees the alternate stack that
// pp_signals_mark_thread set. Returns PP_OK.
pp_status_t pp_signals_unmark_thread(void);

// Registers a range of the JIT code pool, from start for size bytes, and returns its handle.
// Returns PP_OK; PP_E_INVALID_ARGUMENT for a NULL handle, a start of 0, a size of 0 or a range
// past the end of the address space; PP_E_OUT_OF_MEMORY when the table is full.
pp_status_t pp_signals_add_pool_range(uint64_t start, uint64_t size, uint64_t *handle);

// Removes a range of the JIT code pool. Returns PP_OK; PP_E_INVALID_ARGUMENT for a handle that
// names no registered range.
pp_status_t pp_signals_remove_pool_range(uint64_t handle);

// Registers a guest address reservation, from start for size bytes, and returns its handle.
// The results are those of pp_signals_add_pool_range.
pp_status_t pp_signals_add_reservation(uint64_t start, uint64_t size, uint64_t *handle);

// Removes a guest address reservation. The results are those of pp_signals_remove_pool_range.
pp_status_t pp_signals_remove_reservation(uint64_t handle);

// Turns the probe mode on (1) or off (0): while it is on, a fault in the JIT code pool goes to
// the probe handler (C-0006 section 3). Returns PP_OK; PP_E_INVALID_ARGUMENT for another value.
pp_status_t pp_signals_set_probe_mode(uint8_t on);

// Sets the guest fault handler, or removes it with NULL. When it returns, no fault is still in
// the handler it replaced, so it waits for a handler that has not returned yet; a handler that
// never returns, which the rules above forbid, makes it wait for ever. Returns PP_OK.
pp_status_t pp_signals_set_guest_handler(pp_guest_fault_handler_t handler, void *user_data);

// Sets the probe handler, or removes it with NULL, as pp_signals_set_guest_handler does.
pp_status_t pp_signals_set_probe_handler(pp_guest_fault_handler_t handler, void *user_data);

// Returns whether a guest fault handler is set.
bool pp_signals_has_guest_handler(void);

// Reads the counters. Any thread.
void pp_signals_read_counters(pp_signal_counters_t *counters);

#endif // PANEPORT_HOST_SIGNALS_H
