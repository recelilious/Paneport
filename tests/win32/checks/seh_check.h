// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Structured exception handling. Each exception happens in a function of its own, called from
// a compiler __try block, because clang catches only the exceptions raised inside a call when
// it targets MinGW: it has no asynchronous exceptions there, so a fault in the __try body itself
// may be optimized away. The build gives clang -fms-extensions, which __try needs.

#ifndef PP_SEH_CHECK_H
#define PP_SEH_CHECK_H

#include "test_output.h"

// The code of the software exceptions that the checks raise, in the range Windows leaves to
// applications.
#define PP_SEH_RAISED_CODE 0xE0505054UL
#define PP_SEH_CONTINUED_CODE 0xE0505055UL

// Catches an access violation (a write to address 0x10), an integer divide by zero and a
// RaiseException call, each with its own handler, and checks the exception records. Prints
// "<area>: access violation, divide by zero and RaiseException caught: pass", or fail, and
// returns whether all three passed.
bool pp_seh_basic(struct pp_test_run *run, const char *area);

// Checks that a __finally block runs while an exception unwinds through it, that a vectored
// handler runs before the frame's handler, that a vectored handler can continue execution
// after RaiseException, and that a division reads its divisor once while another thread
// changes it. Prints one line for each and returns whether all passed.
bool pp_seh_extended(struct pp_test_run *run, const char *area);

#endif
