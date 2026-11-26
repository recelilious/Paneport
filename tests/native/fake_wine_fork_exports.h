// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The functions that the fake Wine fork of fake_wine_fork.c exports, which the session tests
// find with dlsym, as the host finds the fork's.

#ifndef PANEPORT_TESTS_FAKE_WINE_FORK_EXPORTS_H
#define PANEPORT_TESTS_FAKE_WINE_FORK_EXPORTS_H

#include <stdint.h>

#include "paneport/wine_bridge.h"
#include "wine/paneport.h"

// The entry points, under the names of the fork's header, which follow Wine's __wine_ prefix
// for its exports; .clang-tidy allows these five reserved names.
int32_t __wine_paneport_session_start(const pp_wine_bridge_t *bridge,
                                      const struct wine_paneport_session *session);
int32_t __wine_paneport_session_stop(void);
int32_t __wine_paneport_process_launch(const struct wine_paneport_launch *launch);
int32_t __wine_paneport_process_terminate(uint64_t process, uint32_t exit_code);
int32_t __wine_paneport_process_query(uint64_t process, struct wine_paneport_process_state *state);

// 1 when the bridge that the fake received is complete and of this version, else 0.
int fake_fork_bridge_ok(void);

// The stack size of the thread that the host created for the fake's server, in KiB.
int fake_fork_server_stack_kib(void);

// How often the host called the fake's launch.
int fake_fork_launches(void);

// What the bridge's start_thread returned when the fake's stop asked for a thread in the mode
// late_thread, or 1 while the stop has not run.
int fake_fork_late_thread_status(void);

// Waits up to a minute until the fake waits at the gate of the modes launch_hold_reported and
// late_sigsegv; 1 when it does, else 0.
int fake_fork_reached_gate(void);

// Opens the fake's gate, and returns 1.
int fake_fork_open_gate(void);

#endif // PANEPORT_TESTS_FAKE_WINE_FORK_EXPORTS_H
