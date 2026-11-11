// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The host's life cycle as the other parts of the host see it.

#ifndef PANEPORT_HOST_HOST_STATE_H
#define PANEPORT_HOST_HOST_STATE_H

#include <stdbool.h>
#include <stdint.h>

// Returns whether the calling thread counts as the main thread. In test builds
// (PANEPORT_TEST_BUILD) the thread that first called pp_init stands in for it, because test
// runners do not run tests on the process's main thread.
bool pp_host_on_main_thread(void);

// Returns the calling thread's PP_THREAD_ROLE_ value, for a crash record. Async-signal-safe
// on Darwin.
uint32_t pp_host_thread_role(void);

#endif // PANEPORT_HOST_HOST_STATE_H
