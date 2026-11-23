// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The fork and exec trap of the harness's single mode (S4.3.9, docs/design/runtime-macos-harness.md
// section 5.2): libpaneport_trap.dylib, which paneport_harness links, so that dyld applies its
// interposing at launch to every image that the process binds later, Wine's among them. It
// intercepts fork, vfork, execl, execle, execlp, execv, execve, execvp, execvP, posix_spawn,
// posix_spawnp, system and popen, and the sending of SIGUSR1 and SIGQUIT through kill, killpg,
// pthread_kill, raise and __pthread_kill.
//
// Disarmed, as it starts, the trap hands every call on unchanged and reports nothing, except that
// a vfork runs as fork. Armed, it refuses each of those calls: it never runs it, and returns the
// call's failure value with errno set to EPERM, or EPERM itself where the call returns an error
// number. It counts each refused call, keeps the reports of the first PP_TRAP_REPORTS, each
// naming the call and the image, symbol and offset of its caller, and writes each report to
// standard error at once. Every function here runs on any thread.

#ifndef PP_HARNESS_TRAP_H
#define PP_HARNESS_TRAP_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// Marks a function that the trap's library exports; the rest of it is hidden.
#define PP_TRAP_EXPORT __attribute__((visibility("default")))

// How many reports the trap keeps, and the size of each, with its NUL.
#define PP_TRAP_REPORTS 8U
#define PP_TRAP_REPORT_SIZE 256U

// Arms the trap. The harness's child arms it right after pp_init and before pp_session_start;
// the harness's parent never arms it, because it starts the children itself.
PP_TRAP_EXPORT void pp_trap_arm(void);

// Disarms the trap: from then on, every call is handed on again.
PP_TRAP_EXPORT void pp_trap_disarm(void);

// How many calls the trap has refused since the process started.
PP_TRAP_EXPORT uint32_t pp_trap_count(void);

// Copies the report of the refused call with this number, counted from 0, into buffer, such as
// "posix_spawn from ntdll.so!spawn_process+0x48", cut to fit. Returns false when the trap keeps
// no such report: fewer calls were refused, or the call came after the first PP_TRAP_REPORTS.
PP_TRAP_EXPORT bool pp_trap_report(uint32_t index, char *buffer, size_t size);

#endif
