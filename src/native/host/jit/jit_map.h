// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The dual mapping of each region of the pool (C-0006 sections 1 and 3 step 4): the region
// that the debugger prepared stays the executable view, readable and executable and never
// writable, and vm_remap with vm_protect makes its writable alias, readable and writable and
// never executable. Both share the same memory, so code written through the alias runs from the
// view. jit.md section 6 describes it, and what the probe of stage M3.1 found on the runner.

#ifndef PANEPORT_HOST_JIT_MAP_H
#define PANEPORT_HOST_JIT_MAP_H

#include <stdbool.h>
#include <stdint.h>

#include <mach/vm_prot.h>

#include "paneport/jit.h"

// One region of the pool, as the registry keeps it: the hand-off to the translator through the
// Wine bridge lists these (C-0005 section 3).
typedef struct pp_jit_pool_region_t {
    uint64_t exec_address;  // The executable view: readable and executable, never writable.
    uint64_t write_address; // The writable alias: readable and writable, never executable.
    uint64_t size;          // Bytes; a multiple of the page size.
    uint64_t exec_range;    // The signal dispatcher's handle of the view's pool range.
    uint64_t write_range;   // The handle of the alias's pool range.
} pp_jit_pool_region_t;

// The protections of the two views.
#define PP_JIT_EXEC_PROTECTION (VM_PROT_READ | VM_PROT_EXECUTE)
#define PP_JIT_WRITE_PROTECTION (VM_PROT_READ | VM_PROT_WRITE)

// What the mapping of one region found besides its addresses.
typedef struct pp_jit_map_result_t {
    uint64_t write_address;
    // Whether the maximum protections of both views were lowered to their current ones, so
    // that neither can ever gain the other's permission. Best effort: the current protections
    // are what C-0006 requires.
    bool maxima_lowered;
} pp_jit_map_result_t;

// Returns whether every page of [address, address + size) is mapped, with exactly the current
// protection expected (PP_JIT_EXEC_PROTECTION or PP_JIT_WRITE_PROTECTION), and none of them both
// writable and executable. When check_maximum is true, the maximum protection must equal
// expected as well. Any thread.
bool pp_jit_map_has_protection(uint64_t address, uint64_t size, int expected, bool check_maximum);

// Returns whether any page of [address, address + size) is both writable and executable, or
// may become so, because its current or its maximum protection holds both permissions; or
// whether the range is not mapped throughout. Any thread.
bool pp_jit_map_has_writable_executable(uint64_t address, uint64_t size);

// Makes the writable alias of a region that the debugger prepared at exec_address, and checks
// both views. Returns PP_JIT_FAILURE_NONE; PP_JIT_FAILURE_REGION_REFUSED when the region is not
// mapped readable and executable only; PP_JIT_FAILURE_MAPPING when the alias cannot be made
// with its protection, in which case no alias is left behind. Any thread.
pp_jit_failure_t pp_jit_map_alias(uint64_t exec_address, uint64_t size,
                                  pp_jit_map_result_t *result);

// Unmaps a writable alias. The executable view stays: on iOS it belongs to the authorization.
void pp_jit_map_release_alias(uint64_t write_address, uint64_t size);

// Returns the system's page size in bytes: 16 KiB on every Apple silicon device.
uint64_t pp_jit_page_size(void);

#endif // PANEPORT_HOST_JIT_MAP_H
