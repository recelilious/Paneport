// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The provider's one seam between the protocol and the system: how the flags are read, how a
// region is prepared and how the debugger is asked to detach, and the clock. Two backends
// implement it. On iOS (jit_backend_ios.c) the flags come from the system and the commands are
// brk #0xf00d (C-0006 section 1). On macOS (jit_backend_macos.c), for the tests and the
// harness, a simulated broker attaches when the app opens the enabler, maps each region as
// anonymous memory that is readable and executable, and detaches on command 0. The CTest suite
// puts a fake of its own in its place. docs/design/jit.md section 2 describes both.

#ifndef PANEPORT_HOST_JIT_PLATFORM_H
#define PANEPORT_HOST_JIT_PLATFORM_H

#include <stdbool.h>
#include <stdint.h>

#include <TargetConditionals.h>

#include "jit/jit_machine.h"

typedef struct pp_jit_platform_t {
    // Passed to every function below.
    void *context;
    // Reads both flags of C-0006 section 3 step 1. Returns false when the system does not
    // report them. Any thread.
    bool (*read_flags)(void *context, pp_jit_flags_t *flags);
    // Told, after the provider recorded the reading, that the app opens the enabler; may be
    // NULL. The macOS backend's simulated broker attaches here.
    void (*enabler_opened)(void *context);
    // Prepares one region of length bytes with a NULL address hint, and returns its address,
    // or 0 when the debugger returned none: command 1 on iOS. It may block for seconds, and on
    // iOS for ever when the debugger never answers; the provider's timeout covers that.
    uint64_t (*prepare_region)(void *context, uint64_t length);
    // Asks the debugger to detach: command 0 on iOS. It returns when the debugger lets the
    // thread go on, which may be before P_TRACED is clear.
    void (*detach)(void *context);
    // Returns a monotonic time in milliseconds.
    uint64_t (*now_ms)(void *context);
    // The pause between two readings while the provider waits, in milliseconds.
    uint32_t poll_ms;
} pp_jit_platform_t;

// The pause between two readings of the backends of the system: short enough that the
// detach and the broker's attach are seen at once, and long enough to cost nothing.
#define PP_JIT_POLL_MS 50U

// Returns the platform of this build: the debugger broker on iOS, the simulated broker on
// macOS. Static memory.
const pp_jit_platform_t *pp_jit_default_platform(void);

// Reads CS_DEBUGGED with csops and P_TRACED with sysctl, for the calling process. Returns
// false when either call fails. Any thread; not async-signal-safe.
bool pp_jit_read_darwin_flags(pp_jit_flags_t *flags);

// Decodes the two raw values that pp_jit_read_darwin_flags() reads: the code-signing status
// word of CS_OPS_STATUS and the p_flag word of the process. Pure.
pp_jit_flags_t pp_jit_decode_flags(uint32_t code_signing_status, int32_t process_flags);

// Returns the system's monotonic clock in milliseconds.
uint64_t pp_jit_monotonic_ms(void);

#if defined(PANEPORT_TEST_BUILD) && !TARGET_OS_IOS
// Test builds for macOS: maps one region as the simulated broker does, and gives it back.
uint64_t pp_jit_macos_prepare_region(uint64_t length);
void pp_jit_macos_release_region(uint64_t address, uint64_t length);
#endif

#endif // PANEPORT_HOST_JIT_PLATFORM_H
