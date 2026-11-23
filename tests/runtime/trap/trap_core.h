// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// What the interposing half of the fork and exec trap (trap_darwin.c) asks its core (trap.c):
// whether to refuse a call, and the record of a refused one. Every function here runs on any
// thread and allocates no memory; only pp_trap_record takes a lock, dyld's, inside dladdr.

#ifndef PP_HARNESS_TRAP_CORE_H
#define PP_HARNESS_TRAP_CORE_H

#include <stdbool.h>

// Whether the trap is armed, so that a call that would start a process or a program is refused.
bool pp_trap_refuses(void);

// Whether the armed trap refuses to send this signal: SIGUSR1 or SIGQUIT, which C-0005 section 4
// keeps out of thread control.
bool pp_trap_refuses_signal(int signal_number);

// Counts a refused call, keeps its report while fewer than PP_TRAP_REPORTS are kept, and writes
// the report to standard error with one write. call names the function, such as "posix_spawn";
// signal_number is the signal that a refused send would have sent, or 0; caller is the return
// address of the call into the trap.
void pp_trap_record(const char *call, int signal_number, const void *caller);

#endif
