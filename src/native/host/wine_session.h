// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The session and process groups of paneport/session.h and the Wine bridge of
// paneport/wine_bridge.h (C-0005 sections 2 and 3). session.c loads the Wine fork's Unix library
// from the runtime folder, finds the entry points that the fork's header include/wine/paneport.h
// declares, passes the fork the bridge, and keeps the state of the launch's one session and its
// process. docs/design/single-process-wine.md section 8 describes it.

#ifndef PANEPORT_HOST_WINE_SESSION_H
#define PANEPORT_HOST_WINE_SESSION_H

#include <stdint.h>

// Where the fork's Unix library lies in the runtime folder: ntdll's Unix side, which exports the
// entry points.
#define PP_WINE_LIBRARY_PATH "lib/wine/aarch64-unix/ntdll.so"

#ifdef PANEPORT_TEST_BUILD
// A function that pp_session_start runs after the fork started the session and before the
// check of the signal dispatcher, so that a test can plant a handler of its own there.
typedef void (*pp_session_hook_t)(void *argument);

// Test builds only: sets the hook and its argument, or no hook for NULL. Any thread, before
// pp_session_start.
void pp_session_set_started_hook(pp_session_hook_t hook, void *argument);

// Test builds only: sets how long pp_session_stop waits for the end of the session's process and
// of the reader of its output, in milliseconds, instead of 30 s, or 30 s again for 0, so that a
// test of a runtime that never reports them ends quickly. Any thread, before pp_session_stop.
void pp_session_set_stop_wait(uint32_t milliseconds);
#endif

#endif // PANEPORT_HOST_WINE_SESSION_H
