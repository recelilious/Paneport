// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// What the fake Wine fork of fake_wine_fork.c writes, and the exit codes of its process, which
// test_session.c expects.

#ifndef PANEPORT_TESTS_FAKE_WINE_FORK_H
#define PANEPORT_TESTS_FAKE_WINE_FORK_H

// How many pool regions the fake keeps of what the bridge's get_jit_pool lists; a pool has at
// most seven.
#define FAKE_FORK_POOL_CAPACITY 16U

// The exit code of the process in the mode normal.
#define FAKE_FORK_EXIT_CODE 3U

// The exit code of a process that ends before it finishes its start (STATUS_DLL_NOT_FOUND).
#define FAKE_FORK_START_FAILURE 0xC0000135U

// The exit code of a waiting process that the session's stop ends.
#define FAKE_FORK_STOPPED_EXIT_CODE 9U

// The exit code that the query reports in the mode query_exited, which the bridge never reports.
#define FAKE_FORK_QUERY_EXIT_CODE 42U

// A line of 600 bytes, longer than a log line holds.
#define FAKE_FORK_X10 "xxxxxxxxxx"
#define FAKE_FORK_X100                                                                             \
    FAKE_FORK_X10 FAKE_FORK_X10 FAKE_FORK_X10 FAKE_FORK_X10 FAKE_FORK_X10 FAKE_FORK_X10            \
        FAKE_FORK_X10 FAKE_FORK_X10 FAKE_FORK_X10 FAKE_FORK_X10
#define FAKE_FORK_LONG_LINE                                                                        \
    FAKE_FORK_X100 FAKE_FORK_X100 FAKE_FORK_X100 FAKE_FORK_X100 FAKE_FORK_X100 FAKE_FORK_X100

// A line with characters of two, three and four bytes.
#define FAKE_FORK_BYTES_LINE "bytes: \xc3\xa9 \xe2\x82\xac \xf0\x9f\x98\x80 end"

// The standard output: a line that ends with CR LF, one with LF, the long line, the line of
// characters with CR LF, and a last line without a line break.
#define FAKE_FORK_OUTPUT                                                                           \
    "first line\r\n"                                                                               \
    "second line\n" FAKE_FORK_LONG_LINE "\n" FAKE_FORK_BYTES_LINE "\r\n"                           \
    "last line"

// The standard error.
#define FAKE_FORK_ERRORS "an error line\n"

// The mode flood: how many lines the process writes and the runtime logs, more than the host's
// log queue holds, and their text.
#define FAKE_FORK_FLOOD_LINES 1500U
#define FAKE_FORK_FLOOD_TEXT "a line of the flood"

// The line that the process writes in the mode late_output after it reported its end.
#define FAKE_FORK_LATE_LINE "a line after the end was reported"

#endif // PANEPORT_TESTS_FAKE_WINE_FORK_H
