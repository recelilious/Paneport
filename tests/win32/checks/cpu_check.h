// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The CPU features that a Windows program sees, read with CPUID and compared with what
// IsProcessorFeaturePresent reports. Under Wine with FEX both answers come from the emulator.

#ifndef PP_CPU_CHECK_H
#define PP_CPU_CHECK_H

#include "test_output.h"

// Prints one line, "<area>: vendor <vendor>, features <list>", with the vendor string of CPUID
// leaf 0 and the features of leaves 1, 7 and 0x80000001 in a fixed order that starts with sse
// and sse2. It checks that the vendor string is 12 printable characters and that SSE and SSE2
// are present, as every x86-64 processor has them, and returns whether both held.
bool pp_cpu_report(struct pp_test_run *run, const char *area);

// Checks that IsProcessorFeaturePresent agrees with CPUID about SSE, SSE2 and SSE3, and
// prints one line that says so. Returns whether they agree.
bool pp_cpu_compare_with_windows(struct pp_test_run *run, const char *area);

#endif
