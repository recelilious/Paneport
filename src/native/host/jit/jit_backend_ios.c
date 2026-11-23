// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The iOS backend of the JIT provider: the debugger flags of the system, and the two commands
// of the convention that StikJIT's INTEGRATION.md documents (C-0006 section 1). The app
// executes brk #0xf00d with the command in x16 and the arguments in x0 and x1; the broker
// script handles the breakpoint, moves the program counter past it and, for command 1, returns
// the prepared region in x0. Without the broker attached the breakpoint ends the process, so
// the provider sends a command only in the requesting state and only right after reading that
// a debugger is attached (C-0006 section 3). CMake builds this file for iOS only.

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
                     : "x0", "x1", "x16", "memory");
    return address;
}

// Command 0, JIT26Detach.
static void detach(void *context) {
    (void)context;
    __asm__ volatile("mov x16, #0\n"
                     "brk #0xf00d\n"
                     :
                     :
                     : "x0", "x1", "x16", "memory");
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
