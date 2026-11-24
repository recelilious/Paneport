// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The JIT probe handler (jit_probe.h). It runs in the faulting thread's signal context, so it
// reads the probe's window and changes the fault's context with atomics only; a CTest test
// lists the undefined symbols of this file's object with nm and fails on any call
// (tests/native/check_signal_safe_calls.cmake).

#include "jit/jit_probe.h"

#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>

#include "signals/signal_context.h"
#include "signals/signals.h"

pp_jit_probe_window_t pp_jit_probe_window;

static bool inside(uint64_t value, uint64_t start, uint64_t size) {
    if (start == 0U || value < start) {
        return false;
    }
    return value - start < size;
}

uint32_t pp_jit_probe_handle_fault(pp_guest_fault_t *fault, void *user_data) {
    (void)user_data;
    pp_jit_probe_window_t *window = &pp_jit_probe_window;
    const uint32_t phase = atomic_load_explicit(&window->phase, memory_order_acquire);
    const uint64_t page = atomic_load_explicit(&window->page_size, memory_order_relaxed);
    const uint64_t write_start = atomic_load_explicit(&window->write_start, memory_order_relaxed);
    const uint64_t exec_start = atomic_load_explicit(&window->exec_start, memory_order_relaxed);

    bool handled = false;
    switch (phase) {
    case PP_JIT_PROBE_STORE:
    case PP_JIT_PROBE_FLUSH:
        // A store through the alias, or a cache operation on either view: each is a single
        // instruction with no other effect, so the thread goes on after it.
        if ((phase == PP_JIT_PROBE_STORE && inside(fault->fault_address, write_start, 8U)) ||
            (phase == PP_JIT_PROBE_FLUSH && (inside(fault->fault_address, write_start, page) ||
                                             inside(fault->fault_address, exec_start, page)))) {
            pp_signal_context_set_pc(fault->context,
                                     fault->program_counter + PP_SIGNAL_INSTRUCTION_SIZE);
            handled = true;
        }
        break;
    case PP_JIT_PROBE_RUN:
        // The routine is a leaf that leaves the link register alone, so returning to it is
        // returning from the routine, with the mark of a fault as its value.
        if (inside(fault->program_counter, exec_start, page)) {
            pp_signal_context_set_x0(fault->context, PP_JIT_PROBE_FAULTED);
            pp_signal_context_set_pc(fault->context, pp_signal_context_lr(fault->context));
            handled = true;
        }
        break;
    default:
        break;
    }
    if (!handled) {
        return PP_GUEST_FAULT_DECLINED;
    }
    int32_t none = 0;
    (void)atomic_compare_exchange_strong_explicit(&window->first_signal, &none, fault->signal,
                                                  memory_order_relaxed, memory_order_relaxed);
    atomic_store_explicit(&window->last_signal, fault->signal, memory_order_relaxed);
    atomic_store_explicit(&window->last_address, fault->fault_address, memory_order_relaxed);
    atomic_store_explicit(&window->last_pc, fault->program_counter, memory_order_relaxed);
    (void)atomic_fetch_add_explicit(&window->faults, 1U, memory_order_release);
    return PP_GUEST_FAULT_HANDLED;
}
