// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Thread control for the guest work of later stages (C-0005 section 4,
// docs/design/signals.md section 8): suspending a thread, reading and writing its registers,
// and resuming it, through the Mach calls thread_suspend, thread_get_state, thread_set_state
// and thread_resume on arm64. SIGUSR1 and SIGQUIT are never used for thread control. A
// thread is named by a port that pp_thread_control_open takes a reference to, so the name
// stays valid after the thread ends, and the calls then fail. None of this runs in a signal
// handler.

#ifndef PANEPORT_HOST_THREAD_CONTROL_H
#define PANEPORT_HOST_THREAD_CONTROL_H

#include <pthread.h>
#include <stdint.h>

#include "paneport/common.h"

// The general registers of an arm64 thread.
typedef struct pp_thread_state_t {
    uint32_t struct_size; // sizeof(pp_thread_state_t).
    uint32_t cpsr;        // The processor state.
    uint64_t x[29];       // x0 to x28.
    uint64_t fp;          // x29.
    uint64_t lr;          // x30.
    uint64_t sp;
    uint64_t pc;
} pp_thread_state_t;

// Takes a reference to the port of a running thread and returns its name. Returns PP_OK;
// PP_E_INVALID_ARGUMENT for a NULL port or a thread without a port; PP_E_SYSTEM.
pp_status_t pp_thread_control_open(pthread_t thread, uint32_t *port);

// Gives the reference back. Returns PP_OK; PP_E_SYSTEM.
pp_status_t pp_thread_control_close(uint32_t port);

// Suspends the thread; a thread suspended twice needs two resumes. Returns PP_OK;
// PP_E_INVALID_ARGUMENT for the calling thread itself, or for a thread that has ended;
// PP_E_SYSTEM.
pp_status_t pp_thread_control_suspend(uint32_t port);

// Resumes the thread. The results are those of pp_thread_control_suspend.
pp_status_t pp_thread_control_resume(uint32_t port);

// Reads the registers of a suspended thread. Returns PP_OK; PP_E_INVALID_ARGUMENT for a NULL
// state or a thread that has ended; PP_E_SYSTEM.
pp_status_t pp_thread_control_get_state(uint32_t port, pp_thread_state_t *state);

// Writes the registers of a suspended thread, which go on from them once it is resumed. The
// results are those of pp_thread_control_get_state.
pp_status_t pp_thread_control_set_state(uint32_t port, const pp_thread_state_t *state);

#endif // PANEPORT_HOST_THREAD_CONTROL_H
