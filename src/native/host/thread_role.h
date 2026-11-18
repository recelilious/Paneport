// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// What the signal dispatcher's handler asks about the thread that faulted: its role, as a
// crash record names it, and the bounds of its stack. Everything here is async-signal-safe,
// and a CTest test checks the calls of thread_role.c's object together with the handler's
// (tests/native/check_signal_safe_calls.cmake, docs/design/signals.md section 10).

#ifndef PANEPORT_HOST_THREAD_ROLE_H
#define PANEPORT_HOST_THREAD_ROLE_H

#include <stdbool.h>
#include <stdint.h>

// Records the calling thread as the host event thread, which calls it when it starts.
void pp_thread_role_set_event_thread(void);

// Forgets the host event thread once it has ended.
void pp_thread_role_clear_event_thread(void);

// Returns whether the calling thread is the host event thread.
bool pp_thread_role_is_event_thread(void);

#ifdef PANEPORT_TEST_BUILD
// Test builds only: records the calling thread as the one that counts as the main thread, the
// thread whose pp_init succeeded, because test runners do not run tests on the process's main
// thread.
void pp_thread_role_set_main_thread(void);
#endif

// Returns whether the calling thread counts as the main thread: the process's main thread, or
// in test builds the thread that pp_thread_role_set_main_thread recorded.
bool pp_thread_role_is_main_thread(void);

// Returns the calling thread's PP_THREAD_ROLE_ value for a crash record: the event thread, the
// main thread or another thread. A marked thread's role comes from its mark instead.
uint32_t pp_host_thread_role(void);

// Reads the lowest and the highest address of the calling thread's stack, for the dispatcher's
// stack overflow check. Apple's pthread_get_stackaddr_np and pthread_get_stacksize_np read the
// calling thread's own record, without a lock. Returns false when the system reports no stack.
bool pp_thread_stack_bounds(uintptr_t *low, uintptr_t *high);

#endif // PANEPORT_HOST_THREAD_ROLE_H
