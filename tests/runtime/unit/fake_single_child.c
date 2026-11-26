// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// A fake of the child role of single mode (single_child.h), with which test_single_parent runs the
// harness's parent role, main.c, on any host, without the native host and without Wine. For a
// program case it prints PP_FAKE_CHILD_LINE as the program's only line and reports, as the
// program's Windows exit code, the number that the case's one argument names, from 0 to
// 4294967295, as the session's process-exited event would: no Win32 test program can exit with
// more than 255. It writes its result as the real child does (run_result.h), with no refused
// call, and exits with 0, or with 1 when the result names a failure.

#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "case_file.h"
#include "run_result.h"
#include "single_child.h"

// The program's line that the fake prints, which test_single_parent's expected output holds.
#define PP_FAKE_CHILD_LINE "fake: the program's only line\n"

// Reads text as a decimal number from 0 to UINT32_MAX, with nothing after it.
static bool read_exit_code(const char *text, uint32_t *code) {
    if (text[0] < '0' || text[0] > '9') {
        return false;
    }
    errno = 0;
    char *end = NULL;
    const unsigned long long value = strtoull(text, &end, 10);
    if (errno != 0 || *end != '\0' || value > UINT32_MAX) {
        return false;
    }
    *code = (uint32_t)value;
    return true;
}

// Does what the real child does for a program, as far as the parent can see it.
static void run_program(const pp_case_t *test, pp_run_result_t *result) {
    const size_t length = strlen(PP_FAKE_CHILD_LINE);
    if (write(STDOUT_FILENO, PP_FAKE_CHILD_LINE, length) != (ssize_t)length) {
        pp_run_result_add_failure(result, "the fake child cannot write its line");
    }
    if (test->argument_count != 1U || !read_exit_code(test->arguments[0], &result->exit_code)) {
        pp_run_result_add_failure(result, "the fake child needs one argument, the exit code");
        return;
    }
    result->has_exit_code = true;
}

int pp_single_child_run(const pp_single_child_t *paths) {
    pp_run_result_t result;
    pp_run_result_init(&result);
    pp_case_t test;
    char error[512];
    if (pp_case_read(paths->case_path, &test, error, sizeof error)) {
        if (test.kind == PP_CASE_PROGRAM) {
            run_program(&test, &result);
        }
        pp_case_free(&test);
    } else {
        pp_run_result_add_failure(&result, "the fake child cannot read the case: %s", error);
    }
    char path[PATH_MAX];
    const int length = snprintf(path, sizeof path, "%s/%s", paths->logs, PP_RESULT_FILE);
    if (length <= 0 || (size_t)length >= sizeof path || !pp_run_result_write(&result, path)) {
        return 1;
    }
    if (result.failure_count > 0U) {
        return 1;
    }
    return 0;
}
