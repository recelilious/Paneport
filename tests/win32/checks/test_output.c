// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "test_output.h"

#include <stdarg.h>
#include <stdio.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

void pp_test_line(struct pp_test_run *run, const char *format, ...) {
    char line[PP_TEST_LINE_MAX];
    va_list arguments;
    va_start(arguments, format);
    // The last byte stays free for the line feed that ends the line.
    int length = vsnprintf(line, sizeof line - 1U, format, arguments);
    va_end(arguments);
    if (length < 0) {
        run->output_failed = true;
        return;
    }
    size_t size = (size_t)length;
    if (size > sizeof line - 2U) {
        size = sizeof line - 2U;
    }
    line[size] = '\n';
    size++;

    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);
    if (output == NULL || output == INVALID_HANDLE_VALUE) {
        run->output_failed = true;
        return;
    }
    size_t done = 0;
    while (done < size) {
        DWORD written = 0;
        if (WriteFile(output, line + done, (DWORD)(size - done), &written, NULL) == FALSE ||
            written == 0) {
            run->output_failed = true;
            return;
        }
        done += written;
    }
}

bool pp_test_check(struct pp_test_run *run, bool passed, const char *what) {
    if (!passed) {
        run->failures++;
        pp_test_line(run, "%s: check failed: %s", run->program, what);
    }
    return passed;
}

// The number that text spells with one to three digits, or -1.
static int small_number(const char *text) {
    size_t length = strlen(text);
    if (length == 0U || length > 3U) {
        return -1;
    }
    int value = 0;
    for (size_t index = 0; index < length; index++) {
        char digit = text[index];
        if (digit < '0' || digit > '9') {
            return -1;
        }
        value = (value * 10) + (digit - '0');
    }
    return value;
}

int pp_test_exit_code_argument(struct pp_test_run *run, int argc, char *const *argv) {
    if (argc <= 1) {
        return 0;
    }
    int value = -1;
    if (argc == 2) {
        value = small_number(argv[1]);
    }
    if (value > 255) {
        value = -1;
    }
    if (!pp_test_check(run, value >= 0, "the only argument is an exit code from 0 to 255")) {
        return PP_TEST_EXIT_FAILURE;
    }
    return value;
}

const char *pp_test_verdict(bool passed) {
    if (passed) {
        return "pass";
    }
    return "fail";
}

int pp_test_finish(struct pp_test_run *run, int exit_code) {
    if (run->output_failed) {
        run->failures++;
    }
    if (run->failures == 0U) {
        pp_test_line(run, "result: pass");
        return exit_code;
    }
    pp_test_line(run, "result: fail, %u checks failed", run->failures);
    return PP_TEST_EXIT_FAILURE;
}
