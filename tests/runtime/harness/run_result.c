// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "run_result.h"

#include <errno.h>
#include <limits.h>
#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

#include "case_file.h"

// The longest result file that the reader takes.
#define PP_RESULT_MAX_SIZE ((size_t)64U * 1024U)

void pp_run_result_init(pp_run_result_t *result) { memset(result, 0, sizeof *result); }

// Replaces the control characters of a text with dots, so that it stays on one line.
static void flatten(char *text) {
    for (char *next = text; *next != '\0'; next++) {
        const unsigned char byte = (unsigned char)*next;
        if (byte < 0x20U || byte == 0x7FU) {
            *next = '.';
        }
    }
}

void pp_run_result_add_failure(pp_run_result_t *result, const char *format, ...) {
    if (result->failure_count == PP_RESULT_ITEMS) {
        result->failures_cut = true;
        return;
    }
    char *text = result->failures[result->failure_count];
    va_list list;
    va_start(list, format);
    (void)vsnprintf(text, PP_RESULT_TEXT_SIZE, format, list);
    va_end(list);
    flatten(text);
    result->failure_count++;
}

void pp_run_result_add_trap(pp_run_result_t *result, const char *report) {
    if (result->trap_lines == PP_RESULT_ITEMS) {
        return;
    }
    char *text = result->traps[result->trap_lines];
    (void)snprintf(text, PP_RESULT_TEXT_SIZE, "%s", report);
    flatten(text);
    result->trap_lines++;
}

bool pp_run_result_write(const pp_run_result_t *result, const char *path) {
    char temporary[PATH_MAX];
    const int length = snprintf(temporary, sizeof temporary, "%s.new", path);
    if (length <= 0 || (size_t)length >= sizeof temporary) {
        return false;
    }
    FILE *file = fopen(temporary, "wb");
    if (file == NULL) {
        return false;
    }
    if (result->has_exit_code) {
        (void)fprintf(file, "exit_code %u\n", (unsigned)result->exit_code);
    }
    (void)fprintf(file, "trap_count %u\n", (unsigned)result->trap_count);
    for (size_t index = 0; index < result->trap_lines; index++) {
        (void)fprintf(file, "trap %s\n", result->traps[index]);
    }
    for (size_t index = 0; index < result->failure_count; index++) {
        (void)fprintf(file, "failure %s\n", result->failures[index]);
    }
    if (result->failures_cut) {
        (void)fputs("failures_cut 1\n", file);
    }
    bool written = ferror(file) == 0;
    if (fclose(file) != 0) {
        written = false;
    }
    if (written && rename(temporary, path) != 0) {
        written = false;
    }
    if (!written) {
        (void)remove(temporary);
    }
    return written;
}

// Reads a decimal number that fits in 32 bits, with no sign and no leading zero.
static bool read_number(const char *text, uint32_t *value) {
    uint64_t result = 0U;
    size_t digits = 0U;
    for (; text[digits] != '\0'; digits++) {
        const char next = text[digits];
        if (next < '0' || next > '9' || digits == 10U) {
            return false;
        }
        result = (result * 10U) + (uint64_t)(next - '0');
    }
    if (digits == 0U || (digits > 1U && text[0] == '0') || result > UINT32_MAX) {
        return false;
    }
    *value = (uint32_t)result;
    return true;
}

// Reads one line, "<name> <value>", into the result; false when it is malformed. trap_count
// comes once, and the trap lines follow it, at most as many as it counts, since the parent
// names the refused calls by the count.
static bool read_item(char *line, pp_run_result_t *result, bool *trap_count_seen) {
    char *space = strchr(line, ' ');
    if (space == NULL) {
        return false;
    }
    *space = '\0';
    const char *value = space + 1;
    if (strcmp(line, "exit_code") == 0) {
        if (result->has_exit_code || !read_number(value, &result->exit_code)) {
            return false;
        }
        result->has_exit_code = true;
        return true;
    }
    if (strcmp(line, "trap_count") == 0) {
        if (*trap_count_seen || !read_number(value, &result->trap_count)) {
            return false;
        }
        *trap_count_seen = true;
        return true;
    }
    if (strcmp(line, "trap") == 0 && result->trap_lines < result->trap_count &&
        result->trap_lines < PP_RESULT_ITEMS) {
        pp_run_result_add_trap(result, value);
        return true;
    }
    if (strcmp(line, "failure") == 0 && result->failure_count < PP_RESULT_ITEMS) {
        pp_run_result_add_failure(result, "%s", value);
        return true;
    }
    if (strcmp(line, "failures_cut") == 0 && strcmp(value, "1") == 0) {
        result->failures_cut = true;
        return true;
    }
    return false;
}

bool pp_run_result_read(const char *path, pp_run_result_t *result, char *error, size_t error_size) {
    pp_run_result_init(result);
    struct stat status;
    if (stat(path, &status) != 0 && errno == ENOENT) {
        (void)snprintf(error, error_size, "there is no result file");
        return false;
    }
    size_t length = 0;
    char *text = pp_read_file(path, PP_RESULT_MAX_SIZE, &length, error, error_size);
    if (text == NULL) {
        return false;
    }
    // Every line ends with a line feed, and no NUL hides a part of the file.
    bool valid = length > 0U;
    if (valid && (text[length - 1U] != '\n' || memchr(text, '\0', length) != NULL)) {
        valid = false;
    }
    unsigned number = 1U;
    bool trap_count_seen = false;
    char *line = text;
    while (valid && *line != '\0') {
        char *end = strchr(line, '\n');
        if (end == NULL) {
            valid = false;
            break;
        }
        *end = '\0';
        valid = read_item(line, result, &trap_count_seen);
        if (valid) {
            line = end + 1;
            number++;
        }
    }
    free(text);
    if (!valid) {
        (void)snprintf(error, error_size, "line %u of the result file is malformed", number);
    } else if (!trap_count_seen) {
        // The writer always writes the count, so a file without it is not a whole result.
        (void)snprintf(error, error_size, "the result file has no trap_count line");
        valid = false;
    }
    if (!valid) {
        pp_run_result_init(result);
    }
    return valid;
}

bool pp_run_result_exit_code_matches(uint32_t exit_code, int expected, char *reason,
                                     size_t reason_size) {
    if (exit_code == (uint32_t)expected) {
        return true;
    }
    if (exit_code > 0xFFU) {
        (void)snprintf(reason, reason_size, "exited with %u, not %d (Windows exit code 0x%x)",
                       (unsigned)exit_code, expected, (unsigned)exit_code);
    } else {
        (void)snprintf(reason, reason_size, "exited with %u, not %d", (unsigned)exit_code,
                       expected);
    }
    return false;
}
