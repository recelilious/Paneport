// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The iOS backend of the JIT provider: the debugger flags of the system, and the two commands
// of the convention that StikJIT's INTEGRATION.md documents (C-0006 section 1). The app
// executes brk #0xf00d with the command in x16 and the arguments in x0 and x1; the broker
// script handles the breakpoint, moves the program counter past it and, for command 1, returns
// the prepared region in x0. Without the broker attached the breakpoint ends the process, so
// the provider sends a command only in the requesting state and only right after reading that
// a debugger is attached (C-0006 section 3). CMake builds this file for iOS only.
//
// INTEGRATION.md presents each command as a C function, so the broker may change any register
// that a call may change under the arm64 calling convention. Each command therefore declares
// them all as changed: x0 to x17, the vector registers that a call does not preserve, and the
// condition flags. x18 is Apple's to keep, and the breakpoint leaves the link register alone.

#include "jit/jit_platform.h"

#include <stddef.h>
#include <stdint.h>

// Command 1, JIT26PrepareRegion: a NULL address asks the debugger to allocate the region,
// because a region that the app mapped itself never became executable (C-0006 section 1).
static uint64_t prepare_region(void *context, uint64_t length) {
    (void)context;
    uint64_t address = 0U;
    __asm__ volatile("mov x0, xzr\n"
                     "mov x1, %1\n"
                     "mov x16, #1\n"
                     "brk #0xf00d\n"
                     "mov %0, x0\n"
                     : "=r"(address)
                     : "r"(length)
                     : "x0", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11",
                       "x12", "x13", "x14", "x15", "x16", "x17", "v0", "v1", "v2", "v3", "v4", "v5",
                       "v6", "v7", "v16", "v17", "v18", "v19", "v20", "v21", "v22", "v23", "v24",
                       "v25", "v26", "v27", "v28", "v29", "v30", "v31", "cc", "memory");
    return address;
}

// Command 0, JIT26Detach.
static void detach(void *context) {
    (void)context;
    __asm__ volatile("mov x16, #0\n"
                     "brk #0xf00d\n"
                     :
                     :
                     : "x0", "x1", "x2", "x3", "x4", "x5", "x6", "x7", "x8", "x9", "x10", "x11",
                       "x12", "x13", "x14", "x15", "x16", "x17", "v0", "v1", "v2", "v3", "v4", "v5",
                       "v6", "v7", "v16", "v17", "v18", "v19", "v20", "v21", "v22", "v23", "v24",
                       "v25", "v26", "v27", "v28", "v29", "v30", "v31", "cc", "memory");
}

static bool read_flags(void *context, pp_jit_flags_t *flags) {
    (void)context;
    return pp_jit_read_darwin_flags(flags);
}

static uint64_t now_ms(void *context) {
    (void)context;
    return pp_jit_monotonic_ms();
}

static const pp_jit_platform_t k_ios_platform = {
    .context = NULL,
    .read_flags = read_flags,
    .enabler_opened = NULL,
    .prepare_region = prepare_region,
    .detach = detach,
    .now_ms = now_ms,
    .poll_ms = PP_JIT_POLL_MS,
};

const pp_jit_platform_t *pp_jit_default_platform(void) { return &k_ios_platform; }
