// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Threads and synchronization: interlocked updates from several threads, a critical section,
// events, and a worker that is suspended, has its context changed and is resumed. The last one
// is what a debugger or a game's own crash handler does, and under Wine with FEX it crosses the
// emulator's view of the thread's registers.

#ifndef PP_THREADS_CHECK_H
#define PP_THREADS_CHECK_H

#include "test_output.h"

// Runs the four checks. With detailed set, it prints one line per check, with its counts;
// otherwise one line, "<area>: interlocked, critical section, events and suspend with context:
// pass", or fail. Returns whether every check passed.
bool pp_threads_report(struct pp_test_run *run, const char *area, bool detailed);

#endif
