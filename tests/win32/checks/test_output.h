// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The output and the check count that every program of tests/win32 shares. A program prints
// lines of the form "<area>: <text>" on its standard output and ends with a result line, which
// the harness in tests/runtime compares with its expected outputs. Every function here runs on
// the program's main thread; worker threads never print.

#ifndef PP_TEST_OUTPUT_H
#define PP_TEST_OUTPUT_H

#include <stdbool.h>

// The longest line, with its line feed, that pp_test_line writes; a longer one is cut.
#define PP_TEST_LINE_MAX 512

// The exit code of a program whose checks failed, or whose argument is not an exit code.
#define PP_TEST_EXIT_FAILURE 1

// The architecture that the program was built for, as its greeting names it.
#ifdef __x86_64__
#define PP_TEST_ARCHITECTURE "x86-64"
#elif defined(__i386__)
#define PP_TEST_ARCHITECTURE "i686"
#else
#error "tests/win32 builds for x86-64 and i686 only."
#endif

// One run of a test program: its name, how many checks failed, and whether a line could not
// be written. The caller owns it, usually as a local of main.
struct pp_test_run {
    const char *program;
    unsigned failures;
    bool output_failed;
};

// Writes one line, formatted like printf and followed by a line feed, to the standard output
// handle with WriteFile, so that no line waits in a C library buffer if a later check crashes
// the program. A line that does not fit in PP_TEST_LINE_MAX bytes is cut.
void pp_test_line(struct pp_test_run *run, const char *format, ...)
    __attribute__((format(printf, 2, 3)));

// Records the outcome of one check and returns it. A failed check also prints a line that
// names the program and what failed, so that the log shows why the result line says fail.
bool pp_test_check(struct pp_test_run *run, bool passed, const char *what);

// Reads the optional exit-code argument of a program: with no argument the exit code is 0,
// and with one it is that number, from 0 to 255. Anything else fails a check and gives
// PP_TEST_EXIT_FAILURE.
int pp_test_exit_code_argument(struct pp_test_run *run, int argc, char *const *argv);

// "pass" or "fail", for the line that ends a group of checks. The text is static.
const char *pp_test_verdict(bool passed);

// Prints the result line, "result: pass" or "result: fail, <n> checks failed", and returns
// the exit code for main: exit_code when every check passed, PP_TEST_EXIT_FAILURE otherwise.
int pp_test_finish(struct pp_test_run *run, int exit_code);

#endif
