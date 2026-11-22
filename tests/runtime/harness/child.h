// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Runs one child process with its standard output and error captured, for the harness's multi
// mode, where Wine's loader is that child. The child gets /dev/null as its input and a process
// group of its own, so that a timeout ends it with everything it started in that group. Every
// function here runs on the harness's only thread.

#ifndef PP_HARNESS_CHILD_H
#define PP_HARNESS_CHILD_H

#include <stdbool.h>
#include <stddef.h>

// After the child exits, how long the harness still reads what it wrote, for a process that it
// left running with the same pipes, such as a helper of Wine's.
#define PP_CHILD_DRAIN_MS 2000U

typedef struct pp_child_result_t {
    char *output;
    size_t output_length;
    char *errors;
    size_t errors_length;
    bool started;
    bool timed_out;
    bool exited;
    int exit_code;
    int signal;
    bool output_truncated;
    bool errors_truncated;
    double seconds;
    char message[256];
} pp_child_result_t;

// Runs arguments[0] with arguments and environment, in directory when it is not NULL, and waits
// at most timeout_seconds for it. Each captured stream keeps at most limit bytes: the output its
// first limit bytes, the error stream its last, where Wine reports how a program ended. More
// sets output_truncated or errors_truncated. Returns whether the child could be started; result
// says how it ended, and the caller frees it with pp_child_result_free.
bool pp_child_run(char *const arguments[], char *const environment[], const char *directory,
                  unsigned timeout_seconds, size_t limit, pp_child_result_t *result);

void pp_child_result_free(pp_child_result_t *result);

#endif
