// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The guest fault handler of the signal gate's stress harness (signal_harness.h). It runs in
// signal context, so it reads the harness's pages and adds to its counts with atomics only; a
// CTest test lists the undefined symbols of this file's object with nm and fails on any call
// (tests/native/check_signal_safe_calls.cmake). Test builds only.

#include "signal_harness.h"

#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "signals/signal_context.h"
#include "signals/signals.h"

pp_harness_faults_t pp_harness_faults;

static bool in_pages(uint64_t address, uint32_t first, uint32_t count) {
    const uint64_t start = (uint64_t)(uintptr_t)pp_harness_faults.pages +
                           ((uint64_t)first * pp_harness_faults.page_size);
    if (address < start) {
        return false;
    }
    return address < start + ((uint64_t)count * pp_harness_faults.page_size);
}

uint32_t pp_harness_handle_fault(pp_guest_fault_t *fault, void *user_data) {
    (void)user_data;
    const bool marked_pages = in_pages(fault->fault_address, PP_HARNESS_GUARD_PAGE, 2U);
    const bool reservation_pages =
        in_pages(fault->fault_address, PP_HARNESS_RESERVATION_PAGE, PP_HARNESS_RESERVATION_PAGES);
    if (!marked_pages && !reservation_pages) {
        (void)atomic_fetch_add_explicit(&pp_harness_faults.unexpected, 1U, memory_order_relaxed);
        return PP_GUEST_FAULT_DECLINED;
    }
    if (marked_pages && fault->route == PP_FAULT_ROUTE_THREAD_MARK) {
        (void)atomic_fetch_add_explicit(&pp_harness_faults.handled_by_mark, 1U,
                                        memory_order_relaxed);
    } else if (reservation_pages && fault->route == PP_FAULT_ROUTE_RESERVATION) {
        (void)atomic_fetch_add_explicit(&pp_harness_faults.handled_by_address, 1U,
                                        memory_order_relaxed);
    } else {
        (void)atomic_fetch_add_explicit(&pp_harness_faults.unexpected, 1U, memory_order_relaxed);
    }
    pp_signal_context_set_pc(fault->context, fault->program_counter + PP_SIGNAL_INSTRUCTION_SIZE);
    return PP_GUEST_FAULT_HANDLED;
}
