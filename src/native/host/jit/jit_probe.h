// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The verification probe of C-0006 section 3 step 6: with no debugger attached, a small test
// routine is written through the alias into the first page, the last page and one page in
// every 16 MiB of each region, and run from the executable view under the signal dispatcher's
// JIT probe route (C-0005 section 4). Any fault, and any wrong return value, means that the
// authorization failed. jit.md section 7 describes it.
//
// The probe handler (jit_probe_handler.c) runs in the faulting thread's signal context. It
// takes a fault only inside the window that the probe opened for the current page, moves the
// thread to its recovery point, and counts the fault; any other fault it declines, which ends
// the app with a crash record (docs/design/signals.md section 6).

#ifndef PANEPORT_HOST_JIT_PROBE_H
#define PANEPORT_HOST_JIT_PROBE_H

#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>

#include "jit/jit_machine.h"
#include "jit/jit_map.h"
#include "signals/signals.h"

// The probed pages are the first of every block of this size, and the last page.
#define PP_JIT_PROBE_INTERVAL (UINT64_C(16) * 1024U * 1024U)

// The most pages the probe runs in one region, which is at most PP_JIT_REGION_MIB: one in
// every interval, and the last page. It follows the region size, so that a larger region
// still has its last page probed.
#define PP_JIT_PROBE_MAX_PAGES                                                                     \
    ((uint32_t)((((uint64_t)PP_JIT_REGION_MIB * 1024U * 1024U) + PP_JIT_PROBE_INTERVAL - 1U) /     \
                PP_JIT_PROBE_INTERVAL) +                                                           \
     1U)

// The value that the routine of the probe's first page returns. Each later page's routine
// returns the next value, so that a page that runs another page's routine fails.
#define PP_JIT_PROBE_FIRST_VALUE 0x5000U

// The value that x0 holds when a probed routine faulted.
#define PP_JIT_PROBE_FAULTED UINT64_C(0xFFFFFFFF)

// What the probe does at the moment, which tells the handler where a fault may come from.
enum {
    PP_JIT_PROBE_IDLE = 0,  // Nothing: the handler declines every fault.
    PP_JIT_PROBE_STORE = 1, // Writing the routine through the alias at write_start.
    PP_JIT_PROBE_FLUSH = 2, // Making it visible to instruction fetches at exec_start.
    PP_JIT_PROBE_RUN = 3,   // Running it from the view at exec_start.
};

// The window of the current page: a process-wide singleton that the probe writes and the
// handler reads (C-0010 section 3). Only one probe runs at a time.
typedef struct pp_jit_probe_window_t {
    _Atomic(uint32_t) phase;
    _Atomic(uint64_t) write_start; // The alias address of the routine's 8 bytes.
    _Atomic(uint64_t) exec_start;  // The view address of the page being probed.
    _Atomic(uint64_t) page_size;
    _Atomic(uint32_t) faults;      // Faults that the handler took since the probe began.
    _Atomic(int32_t) first_signal; // The signal of the current page's first fault, or 0.
    _Atomic(int32_t) last_signal;
    _Atomic(uint64_t) last_address;
    _Atomic(uint64_t) last_pc;
} pp_jit_probe_window_t;

extern pp_jit_probe_window_t pp_jit_probe_window;

// The probe handler, for pp_signals_set_probe_handler(). Async-signal-safe: atomics and the
// fault's context only, which a CTest test checks with nm.
uint32_t pp_jit_probe_handle_fault(pp_guest_fault_t *fault, void *user_data);

// What a probe found.
typedef struct pp_jit_probe_result_t {
    bool passed;
    uint32_t pages;    // Pages probed, the failing one included.
    uint32_t region;   // The failing page's region.
    uint64_t offset;   // The failing page's offset in its region.
    int32_t signal;    // The page's first fault's signal, or 0 for a wrong return value.
    uint64_t returned; // What the routine returned, PP_JIT_PROBE_FAULTED after a fault.
    uint32_t faults;   // Faults that the handler took.
} pp_jit_probe_result_t;

// Writes the offsets in a region of region_size bytes that the probe runs, in order: the start
// of every PP_JIT_PROBE_INTERVAL, and the last page. Returns their count, at most capacity.
uint32_t pp_jit_probe_offsets(uint64_t region_size, uint64_t page_size, uint64_t *offsets,
                              uint32_t capacity);

// Runs the probe over the regions, which the dispatcher must know as pool ranges, and stops at
// the first page that fails. It sets the probe handler and turns the probe mode on for its own
// run only, and leaves no routine behind. Any thread outside signal context; one at a time.
void pp_jit_probe_run(const pp_jit_pool_region_t *regions, uint32_t count,
                      pp_jit_probe_result_t *result);

// Writes the routine of one page, which returns value, through the alias at write_address and
// runs it from the view at exec_address, inside the probe's window; the probe handler must be
// set and the probe mode on. Returns what the routine returned, or PP_JIT_PROBE_FAULTED.
uint64_t pp_jit_probe_page(uint64_t write_address, uint64_t exec_address, uint32_t value);

#endif // PANEPORT_HOST_JIT_PROBE_H
