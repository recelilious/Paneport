// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The session and process groups of paneport/session.h and the Wine bridge of
// paneport/wine_bridge.h (C-0005 sections 2 and 3). session.c loads the Wine fork's Unix library
// from the runtime folder, finds the entry points that the fork's header include/wine/paneport.h
// declares, passes the fork the bridge, and keeps the state of the launch's one session and its
// process. docs/design/single-process-wine.md section 8 describes it.

#ifndef PANEPORT_HOST_WINE_SESSION_H
#define PANEPORT_HOST_WINE_SESSION_H

#include <stdbool.h>
#include <stdint.h>

// Where the fork's Unix library lies in the runtime folder: ntdll's Unix side, which exports the
// entry points.
#define PP_WINE_LIBRARY_PATH "lib/wine/aarch64-unix/ntdll.so"

// Returns whether the launch's session is starting, running or stopping, or a pp_process_launch
// is still inside the runtime, which posts the process's end once it returns, so that
// pp_shutdown refuses to stop the host under either. Any thread.
bool pp_session_is_active(void);

// Collects a session that ended by itself as pp_session_stop does, unless pp_session_stop did:
// joins the threads that the host created for it, waits up to 30 s for the runtime's report of
// the process's end and for the reader of the process's output, stops a reader whose pipes stay
// open, and posts the process's lines and end and the session's end that were still to come.
// Nothing when no session ended. The main thread; the caller is pp_shutdown, before it stops the
// event thread.
void pp_session_collect(void);

#ifdef PANEPORT_TEST_BUILD
// A function of a test that the session runs at a point of its own, with its argument.
typedef void (*pp_session_hook_t)(void *argument);

// Test builds only: sets a hook that pp_session_start runs after the fork started the session
// and before the check of the signal dispatcher, so that a test can plant a handler of its own
// there, or no hook for NULL. Any thread, before pp_session_start.
void pp_session_set_started_hook(pp_session_hook_t hook, void *argument);

// Test builds only: sets a hook that pp_process_launch runs after it started the reader of the
// program's output and before its last check that the session runs, or no hook for NULL, so that
// a test can stop the session there. Any thread, before pp_process_launch.
void pp_session_set_launch_hook(pp_session_hook_t hook, void *argument);

// Test builds only: sets how long pp_session_stop waits for the end of the session's process and
// of the reader of its output, in milliseconds, instead of 30 s, or 30 s again for 0, so that a
// test of a runtime that never reports them ends quickly. Any thread, before pp_session_stop.
void pp_session_set_stop_wait(uint32_t milliseconds);

// Test builds only: sets how long pp_process_launch waits for the process to finish its start,
// in milliseconds, instead of 120 s, or 120 s again for 0, so that a test of a program that never
// finishes its start ends quickly. Any thread, before pp_process_launch.
void pp_session_set_launch_wait(uint32_t milliseconds);
#endif

#endif // PANEPORT_HOST_WINE_SESSION_H
