// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The host's life cycle as the other parts of the host see it.

#ifndef PANEPORT_HOST_HOST_STATE_H
#define PANEPORT_HOST_HOST_STATE_H

#include <stdbool.h>
#include <stdint.h>

#include "paneport/common.h"

// Returns whether the calling thread counts as the main thread. In test builds
// (PANEPORT_TEST_BUILD) the thread whose pp_init succeeded stands in for it, because test
// runners do not run tests on the process's main thread. thread_role.h holds the rule, and
// pp_host_thread_role, the calling thread's role for a crash record.
bool pp_host_on_main_thread(void);

// Returns whether pp_init succeeded and pp_shutdown has not run yet. Any thread.
bool pp_host_is_running(void);

// Checks what a [main] function of the interface needs: a running host, called on the main
// thread. Returns PP_OK, or PP_E_NOT_INITIALIZED or PP_E_WRONG_THREAD with the calling
// thread's message set, naming function.
pp_status_t pp_host_check_main_call(const char *function);

#endif // PANEPORT_HOST_HOST_STATE_H
