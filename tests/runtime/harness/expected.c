// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "expected.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "case_file.h"

// The longest output line that a pattern is matched against; a longer one never matches.
#define PP_OUTPUT_MAX_LINE 4096U

// One line of a text: where it starts and how long it is, without its line feed.
typedef struct line_t {
    const char *start;
    size_t length;
} line_t;

// Returns the line that starts at offset, and moves offset past it and its line feed.
static line_t next_line(const char *text, size_t length, size_t *offset) {
    line_t line = {text + *offset, 0U};
    const char *end = memchr(line.start, '\n', length - *offset);
    if (end == NULL) {
        line.length = length - *offset;
        *offset = length;
    } else {
        line.length = (size_t)(end - line.start);
        *offset += line.length + 1U;
    }
    return line;
}

static size_t count_lines(const char *text, size_t length) {
    size_t count = 0;
    size_t offset = 0;
    while (offset < length) {
        (void)next_line(text, length, &offset);
        count++;
    }
    return count;
}

static void free_lines(pp_expected_line_t *lines, size_t count) {
    for (size_t index = 0; index < count; index++) {
        if (lines[index].kind == PP_EXPECTED_PATTERN) {
            regfree(&lines[index].pattern);
        }
        free(lines[index].text);
    }
    free(lines);
}

static bool parse_line(line_t line, unsigned number, pp_expected_line_t *result, char *error,
                       size_t error_size) {
    if (line.length < 2U || (line.start[0] != '=' && line.start[0] != '~') ||
        line.start[1] != ' ') {
        (void)snprintf(error, error_size, "line %u: an expected line starts with \"= \" or \"~ \"",
                       number);
        return false;
    }
    size_t text_length = line.length - 2U;
    if (text_length > PP_EXPECTED_MAX_LINE || memchr(line.start, '\0', line.length) != NULL) {
        (void)snprintf(error, error_size, "line %u: the line is too long or holds a NUL", number);
        return false;
    }
    result->kind = line.start[0] == '=' ? PP_EXPECTED_LITERAL : PP_EXPECTED_PATTERN;
    result->text = malloc(text_length + 1U);
    if (result->text == NULL) {
        (void)snprintf(error, error_size, "out of memory");
        return false;
    }
    memcpy(result->text, line.start + 2, text_length);
    result->text[text_length] = '\0';
    if (result->kind == PP_EXPECTED_LITERAL) {
        return true;
    }
    // The pattern must match the whole line.
    char anchored[PP_EXPECTED_MAX_LINE + 5U];
    (void)snprintf(anchored, sizeof anchored, "^(%s)$", result->text);
    int status = regcomp(&result->pattern, anchored, REG_EXTENDED | REG_NOSUB);
    if (status != 0) {
        char reason[128];
        (void)regerror(status, &result->pattern, reason, sizeof reason);
        (void)snprintf(error, error_size, "line %u: the pattern does not compile: %s", number,
                       reason);
        free(result->text);
        result->text = NULL;
        return false;
    }
    return true;
}

bool pp_expected_parse(const char *text, size_t length, pp_expected_t *result, char *error,
                       size_t error_size) {
    memset(result, 0, sizeof *result);
    if (length > 0U && text[length - 1U] != '\n') {
        (void)snprintf(error, error_size, "the last line does not end with a line feed");
        return false;
    }
    size_t count = count_lines(text, length);
    pp_expected_line_t *lines = calloc(count == 0U ? 1U : count, sizeof *lines);
    if (lines == NULL) {
        (void)snprintf(error, error_size, "out of memory");
        return false;
    }
    size_t offset = 0;
    for (size_t index = 0; index < count; index++) {
        line_t line = next_line(text, length, &offset);
        if (!parse_line(line, (unsigned)(index + 1U), &lines[index], error, error_size)) {
            free_lines(lines, index);
            return false;
        }
    }
    result->lines = lines;
    result->count = count;
    return true;
}

bool pp_expected_read(const char *path, pp_expected_t *result, char *error, size_t error_size) {
    size_t length = 0;
    char *text = pp_read_file(path, PP_EXPECTED_MAX_SIZE, &length, error, error_size);
    if (text == NULL) {
        memset(result, 0, sizeof *result);
        return false;
    }
    bool parsed = pp_expected_parse(text, length, result, error, error_size);
    free(text);
    return parsed;
}

void pp_expected_free(pp_expected_t *value) {
    free_lines(value->lines, value->count);
    value->lines = NULL;
    value->count = 0;
}

static bool line_matches(const pp_expected_line_t *expected, line_t line) {
    if (line.length > 0U && line.start[line.length - 1U] == '\r') {
        line.length--;
    }
    if (memchr(line.start, '\0', line.length) != NULL) {
        return false;
    }
    if (expected->kind == PP_EXPECTED_LITERAL) {
        if (strlen(expected->text) != line.length) {
            return false;
        }
        return memcmp(expected->text, line.start, line.length) == 0;
    }
    if (line.length > PP_OUTPUT_MAX_LINE) {
        return false;
    }
    char copy[PP_OUTPUT_MAX_LINE + 1U];
    memcpy(copy, line.start, line.length);
    copy[line.length] = '\0';
    return regexec(&expected->pattern, copy, 0, NULL, 0) == 0;
}

// Prints a line of output into a message, cut where it would not fit and with control
// characters shown as dots, so that the message stays on one line.
static void quote_line(char *buffer, size_t size, line_t line) {
    size_t used = 0;
    for (size_t index = 0; index < line.length && used + 1U < size; index++) {
        unsigned char next = (unsigned char)line.start[index];
        buffer[used] = (next < 0x20U || next == 0x7FU) ? '.' : (char)next;
        used++;
    }
    buffer[used] = '\0';
}

void pp_expected_compare(const pp_expected_t *expected, const char *output, size_t length,
                         pp_comparison_t *result) {
    memset(result, 0, sizeof *result);
    result->expected_count = expected->count;
    result->output_count = count_lines(output, length);
    size_t offset = 0;
    size_t index = 0;
    for (; index < expected->count && offset < length; index++) {
        line_t line = next_line(output, length, &offset);
        if (!line_matches(&expected->lines[index], line)) {
            char printed[512];
            quote_line(printed, sizeof printed, line);
            result->first_difference = index + 1U;
            (void)snprintf(result->message, sizeof result->message,
                           "line %zu differs: expected %s %s, printed %s", index + 1U,
                           expected->lines[index].kind == PP_EXPECTED_LITERAL ? "=" : "~",
                           expected->lines[index].text, printed);
            return;
        }
    }
    if (index < expected->count) {
        result->first_difference = index + 1U;
        (void)snprintf(result->message, sizeof result->message,
                       "the output ends after %zu lines, but line %zu should be %s %s",
                       result->output_count, index + 1U,
                       expected->lines[index].kind == PP_EXPECTED_LITERAL ? "=" : "~",
                       expected->lines[index].text);
        return;
    }
    if (offset < length) {
        char printed[512];
        quote_line(printed, sizeof printed, next_line(output, length, &offset));
        result->first_difference = index + 1U;
        (void)snprintf(result->message, sizeof result->message,
                       "line %zu is more than the %zu expected lines: %s", index + 1U,
                       expected->count, printed);
        return;
    }
    result->matches = true;
    (void)snprintf(result->message, sizeof result->message, "all %zu lines match", expected->count);
}

bool pp_wine_reported_unhandled(const char *errors, size_t length, char *line, size_t line_size) {
    static const char *const markers[] = {"wine: Unhandled ", "Unhandled exception: "};
    size_t offset = 0;
    while (offset < length) {
        line_t current = next_line(errors, length, &offset);
        for (size_t index = 0; index < sizeof markers / sizeof markers[0]; index++) {
            size_t marker_length = strlen(markers[index]);
            bool found = false;
            for (size_t start = 0; !found && start + marker_length <= current.length; start++) {
                found = memcmp(current.start + start, markers[index], marker_length) == 0;
            }
            if (found) {
                quote_line(line, line_size, current);
                return true;
            }
        }
    }
    return false;
}
