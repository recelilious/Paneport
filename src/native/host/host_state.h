// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The host's life cycle as the other parts of the host see it.

#ifndef PANEPORT_HOST_HOST_STATE_H
#define PANEPORT_HOST_HOST_STATE_H

#include <stdbool.h>
#include <stdint.h>

// Returns whether the calling thread counts as the main thread. In test builds
// (PANEPORT_TEST_BUILD) the thread whose pp_init succeeded stands in for it, because test
// runners do not run tests on the process's main thread. thread_role.h holds the rule, and
// pp_host_thread_role, the calling thread's role for a crash record.
bool pp_host_on_main_thread(void);

#endif // PANEPORT_HOST_HOST_STATE_H
