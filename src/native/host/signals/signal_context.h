// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The machine-dependent part of a fault: the program counter of a signal's context, and the
// single instructions with which the stress harness and the tests plant faults. The host runs
// on arm64 Apple platforms only (iOS, and macOS for tests), where every instruction is 4 bytes
// long, so a handler skips a planted fault by moving the program counter past it. Everything
// here is async-signal-safe.

#ifndef PANEPORT_HOST_SIGNAL_CONTEXT_H
#define PANEPORT_HOST_SIGNAL_CONTEXT_H

#include <signal.h>
#include <stdint.h>
#include <sys/ucontext.h>

#if !defined(__APPLE__) || !defined(__aarch64__) || defined(__arm64e__)
#error "The signal dispatcher reads the machine context of arm64 Apple platforms only."
#endif

// The size of the instruction that each planting function below executes.
#define PP_SIGNAL_INSTRUCTION_SIZE 4U

// Returns the program counter of a signal's context, or 0 without one.
static inline uint64_t pp_signal_context_pc(const void *context) {
    const ucontext_t *user_context = context;
    if (user_context == NULL || user_context->uc_mcontext == NULL) {
        return 0U;
    }
    return (uint64_t)user_context->uc_mcontext->__ss.__pc;
}

// Sets the program counter of a signal's context, so that the thread goes on there when the
// handler returns.
static inline void pp_signal_context_set_pc(void *context, uint64_t program_counter) {
    ucontext_t *user_context = context;
    if (user_context != NULL && user_context->uc_mcontext != NULL) {
        user_context->uc_mcontext->__ss.__pc = program_counter;
    }
}

// The planting functions are always inlined, so that the instruction lies in the caller's own
// body at every optimization level, as a test of routing by program counter needs.

// Reads one byte at address with a single load, which faults on a page that cannot be read.
__attribute__((always_inline)) static inline void
pp_signal_plant_read(const volatile void *address) {
    __asm__ volatile("ldrb w9, [%0]" : : "r"(address) : "x9", "memory");
}

// Writes a zero byte at address with a single store, which faults on a page that cannot be
// written.
__attribute__((always_inline)) static inline void pp_signal_plant_write(volatile void *address) {
    __asm__ volatile("strb wzr, [%0]" : : "r"(address) : "memory");
}

// Executes a permanently undefined instruction, which raises SIGILL.
__attribute__((always_inline)) static inline void pp_signal_plant_illegal(void) {
    __asm__ volatile("udf #0" : : : "memory");
}

// Executes a breakpoint instruction, which raises SIGTRAP.
__attribute__((always_inline)) static inline void pp_signal_plant_breakpoint(void) {
    __asm__ volatile("brk #1" : : : "memory");
}

#endif // PANEPORT_HOST_SIGNAL_CONTEXT_H
