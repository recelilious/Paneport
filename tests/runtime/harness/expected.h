// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The expected outputs of tests/runtime/expected, and their comparison with what a program
// printed. Each line of an expected file is one line of the program's standard output:
//
//   "= <text>"     the output line must be exactly <text>
//   "~ <pattern>"  the output line must match <pattern>, a POSIX extended regular expression,
//                  as a whole
//
// Nothing else may appear: no blank line, no comment. A line of output that ends in a carriage
// return matches without it. Every function here runs on the harness's only thread.

#ifndef PP_HARNESS_EXPECTED_H
#define PP_HARNESS_EXPECTED_H

#include <regex.h>
#include <stdbool.h>
#include <stddef.h>

#define PP_EXPECTED_MAX_SIZE ((size_t)64U * 1024U)
#define PP_EXPECTED_MAX_LINE 1024U
#define PP_COMPARISON_MESSAGE_SIZE 2560U

typedef enum pp_expected_kind_t {
    PP_EXPECTED_LITERAL,
    PP_EXPECTED_PATTERN,
} pp_expected_kind_t;

typedef struct pp_expected_line_t {
    pp_expected_kind_t kind;
    char *text;
    regex_t pattern;
} pp_expected_line_t;

// The lines of one expected file, owned by it and freed by pp_expected_free.
typedef struct pp_expected_t {
    pp_expected_line_t *lines;
    size_t count;
} pp_expected_t;

// The result of a comparison: whether every line matched, the number of the first line that
// did not, from 1, or 0 when all did, both line counts, and a message that shows the first
// difference.
typedef struct pp_comparison_t {
    bool matches;
    size_t first_difference;
    size_t expected_count;
    size_t output_count;
    char message[PP_COMPARISON_MESSAGE_SIZE];
} pp_comparison_t;

// Parses the text of an expected file. On success it fills result, which the caller frees with
// pp_expected_free; on failure it writes a message with the line number into error.
bool pp_expected_parse(const char *text, size_t length, pp_expected_t *result, char *error,
                       size_t error_size);

// Reads and parses the expected file at path.
bool pp_expected_read(const char *path, pp_expected_t *result, char *error, size_t error_size);

void pp_expected_free(pp_expected_t *value);

// Compares a program's standard output, length bytes, with the expected lines.
void pp_expected_compare(const pp_expected_t *expected, const char *output, size_t length,
                         pp_comparison_t *result);

// Returns the first line of Wine's error output that reports an unhandled exception, such as
// "wine: Unhandled page fault on write access to ...", copied into line, or false when there
// is none.
bool pp_wine_reported_unhandled(const char *errors, size_t length, char *line, size_t line_size);

#endif
