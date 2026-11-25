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

#endif // PANEPORT_TESTS_FAKE_WINE_FORK_EXPORTS_H
