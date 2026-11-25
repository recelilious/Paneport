// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The shared user data that Windows maps read-only at 0x7ffe0000 in every process, as x86-64
// code reads it. Wine on Apple silicon cannot map it there, because the first 4 GB are the
// executable's page zero, so it places the data where macOS chooses and FEX redirects the
// guest's reads to it (docs/design/runtime-macos-harness.md section 3.1). The checks read the
// page at its fixed address in many instruction forms, call ntdll's syscall exports, whose x64
// thunks read it too, and write to it, which must fault as on Windows. The field offsets are
// those of KUSER_SHARED_DATA in Wine's include/ddk/wdm.h.

#ifndef PP_SHARED_DATA_CHECK_H
#define PP_SHARED_DATA_CHECK_H

#include "test_output.h"

// Calls NtQueryPerformanceCounter, NtQuerySystemTime and NtDelayExecution through the program's
// imports of ntdll, compares what they return with QueryPerformanceCounter,
// QueryPerformanceFrequency, GetSystemTimeAsFileTime and the time that passed, and prints one
// line that says so. Returns whether every check passed.
bool pp_shared_data_imported_syscalls(struct pp_test_run *run, const char *area);

// The same calls through the addresses that GetProcAddress gives for ntdll's exports. Returns
// whether every check passed.
bool pp_shared_data_syscalls_by_address(struct pp_test_run *run, const char *area);

// Reads the version fields of the page, NtMajorVersion, NtMinorVersion, NtProductType and
// ProductTypeIsValid, in each of the instruction forms of its table, compares every result
// with what RtlGetVersion reports, checks that LEA of the page's address gives the address
// itself, and prints one line that names the number of forms. Returns whether every check
// passed.
bool pp_shared_data_version_fields(struct pp_test_run *run, const char *area);

// Reads the interrupt time, the system time and the tick count from the page in the three-part
// form that Windows documents, and the interrupt time also in one 64-bit load through a
// register, as Go's runtime reads its clock. Two readings of the interrupt time bracket what
// QueryUnbiasedInterruptTime returns, and GetSystemTimeAsFileTime and GetTickCount64, called
// before and after, bracket the page's system time and tick count, each within 5 s. Then checks
// that the interrupt time and the tick count move across a sleep of 100 ms, as a copy of the
// page would not. Prints one line. Returns whether every check passed.
bool pp_shared_data_clocks(struct pp_test_run *run, const char *area);

// Writes to the page in each of the write forms of its table, which must each raise an access
// violation that reports a write at the written address and leave the page as it was, and
// prints one line that names the number of forms. Returns whether every check passed.
bool pp_shared_data_writes(struct pp_test_run *run, const char *area);

#endif
