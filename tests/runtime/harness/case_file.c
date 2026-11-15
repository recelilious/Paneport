// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "case_file.h"

#include <ctype.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define PP_CASE_NAME_MAX 64U

// The state of one parse: where it is, on which line, and the first error.
typedef struct reader_t {
    const char *text;
    size_t length;
    size_t position;
    unsigned line;
    char *error;
    size_t error_size;
    bool failed;
} reader_t;

static void fail_at(reader_t *reader, const char *message) {
    if (!reader->failed) {
        (void)snprintf(reader->error, reader->error_size, "line %u: %s", reader->line, message);
        reader->failed = true;
    }
}

static void skip_space(reader_t *reader) {
    while (reader->position < reader->length) {
        char next = reader->text[reader->position];
        if (next == '\n') {
            reader->line++;
        } else if (next != ' ' && next != '\t' && next != '\r') {
            return;
        }
        reader->position++;
    }
}

static bool at(reader_t *reader, char expected) {
    skip_space(reader);
    if (reader->position >= reader->length) {
        return false;
    }
    return reader->text[reader->position] == expected;
}

static bool take(reader_t *reader, char expected, const char *message) {
    if (at(reader, expected)) {
        reader->position++;
        return true;
    }
    fail_at(reader, message);
    return false;
}

static char escaped(char letter) {
    switch (letter) {
    case '"':
    case '\\':
    case '/':
        return letter;
    case 'b':
        return '\b';
    case 'f':
        return '\f';
    case 'n':
        return '\n';
    case 'r':
        return '\r';
    case 't':
        return '\t';
    default:
        return '\0';
    }
}

// Reads a JSON string of ASCII characters, which the caller frees.
static char *read_string(reader_t *reader) {
    if (!take(reader, '"', "expected a string")) {
        return NULL;
    }
    char *value = malloc(reader->length - reader->position + 1U);
    if (value == NULL) {
        fail_at(reader, "out of memory");
        return NULL;
    }
    size_t used = 0;
    while (reader->position < reader->length) {
        char next = reader->text[reader->position];
        reader->position++;
        if (next == '"') {
            value[used] = '\0';
            return value;
        }
        if (next == '\\' && reader->position < reader->length) {
            char letter = reader->text[reader->position];
            reader->position++;
            next = escaped(letter);
            if (next == '\0') {
                fail_at(reader, letter == 'u' ? "strings are ASCII, without code point escapes"
                                              : "unknown escape in a string");
                break;
            }
        } else if ((unsigned char)next < 0x20U || (unsigned char)next >= 0x80U) {
            fail_at(reader, "strings hold printable ASCII characters only");
            break;
        }
        value[used] = next;
        used++;
    }
    fail_at(reader, "the string does not end");
    free(value);
    return NULL;
}

// Reads a JSON integer of at most nine digits, which is all a case needs.
static bool read_integer(reader_t *reader, long *value) {
    bool negative = at(reader, '-');
    if (negative) {
        reader->position++;
    }
    size_t start = reader->position;
    long result = 0;
    while (reader->position < reader->length && reader->text[reader->position] >= '0' &&
           reader->text[reader->position] <= '9') {
        if (reader->position - start >= 9U) {
            fail_at(reader, "the number is too long");
            return false;
        }
        result = (result * 10) + (reader->text[reader->position] - '0');
        reader->position++;
    }
    size_t digits = reader->position - start;
    if (digits == 0U || (digits > 1U && reader->text[start] == '0')) {
        fail_at(reader, "expected an integer");
        return false;
    }
    if (negative) {
        result = -result;
    }
    *value = result;
    return true;
}

static bool read_arguments(reader_t *reader, pp_case_t *result) {
    if (!take(reader, '[', "\"arguments\" must be an array of strings")) {
        return false;
    }
    if (at(reader, ']')) {
        reader->position++;
        return true;
    }
    for (;;) {
        if (result->argument_count == PP_CASE_MAX_ARGUMENTS) {
            fail_at(reader, "a case has at most 16 arguments");
            return false;
        }
        char *argument = read_string(reader);
        if (argument == NULL) {
            return false;
        }
        result->arguments[result->argument_count] = argument;
        result->argument_count++;
        if (at(reader, ']')) {
            reader->position++;
            return true;
        }
        if (!take(reader, ',', "expected , or ] in \"arguments\"")) {
            return false;
        }
    }
}

// A file name without a folder: letters, digits, dots, hyphens and underscores, not starting
// with a dot, ending in suffix.
static bool is_file_name(const char *name, const char *suffix) {
    size_t length = strlen(name);
    size_t suffix_length = strlen(suffix);
    if (length <= suffix_length || length > PP_CASE_NAME_MAX || name[0] == '.' ||
        strcmp(name + length - suffix_length, suffix) != 0) {
        return false;
    }
    for (size_t index = 0; index < length; index++) {
        char next = name[index];
        if (isalnum((unsigned char)next) == 0 && strchr("._-", next) == NULL) {
            return false;
        }
    }
    return true;
}

// The members that a case has seen, so that a repeated or a missing one is an error.
typedef struct seen_t {
    bool program;
    bool arguments;
    bool timeout;
    bool exit_code;
    bool expected;
} seen_t;

// Marks a member as seen, and fails the parse when it was seen before.
static bool first_time(reader_t *reader, bool *flag) {
    if (*flag) {
        fail_at(reader, "a member appears twice");
        return false;
    }
    *flag = true;
    return true;
}

static void read_bounded(reader_t *reader, long low, long high, long *value, const char *message) {
    if (read_integer(reader, value) && (*value < low || *value > high)) {
        fail_at(reader, message);
    }
}

// Reads one member; a failure shows in reader->failed.
static void read_member(reader_t *reader, pp_case_t *result, seen_t *seen) {
    char *name = read_string(reader);
    if (name == NULL || !take(reader, ':', "expected : after a member's name")) {
        free(name);
        return;
    }
    long number = 0;
    if (strcmp(name, "program") == 0) {
        if (first_time(reader, &seen->program)) {
            result->program = read_string(reader);
        }
    } else if (strcmp(name, "arguments") == 0) {
        if (first_time(reader, &seen->arguments)) {
            (void)read_arguments(reader, result);
        }
    } else if (strcmp(name, "timeout_seconds") == 0) {
        if (first_time(reader, &seen->timeout)) {
            read_bounded(reader, 1, 3600, &number, "\"timeout_seconds\" must be from 1 to 3600");
            result->timeout_seconds = (unsigned)number;
        }
    } else if (strcmp(name, "exit_code") == 0) {
        if (first_time(reader, &seen->exit_code)) {
            read_bounded(reader, 0, 255, &number, "\"exit_code\" must be from 0 to 255");
            result->exit_code = (int)number;
        }
    } else if (strcmp(name, "expected") == 0) {
        if (first_time(reader, &seen->expected)) {
            result->expected = read_string(reader);
        }
    } else {
        fail_at(reader, "unknown member; a case has program, arguments, timeout_seconds, "
                        "exit_code and expected");
    }
    free(name);
}

static void check_complete(reader_t *reader, const pp_case_t *result, const seen_t *seen) {
    if (!seen->program || !seen->timeout || !seen->exit_code || !seen->expected) {
        fail_at(reader, "a case needs program, timeout_seconds, exit_code and expected");
    } else if (!is_file_name(result->program, ".exe")) {
        fail_at(reader, "\"program\" must be a file name ending in .exe, without a folder");
    } else if (!is_file_name(result->expected, ".txt")) {
        fail_at(reader, "\"expected\" must be a file name ending in .txt, without a folder");
    }
}

bool pp_case_parse(const char *text, size_t length, pp_case_t *result, char *error,
                   size_t error_size) {
    memset(result, 0, sizeof *result);
    reader_t reader = {text, length, 0U, 1U, error, error_size, false};
    seen_t seen = {false, false, false, false, false};
    if (take(&reader, '{', "a case is a JSON object")) {
        if (at(&reader, '}')) {
            reader.position++;
        } else {
            for (;;) {
                read_member(&reader, result, &seen);
                if (reader.failed) {
                    break;
                }
                if (at(&reader, '}')) {
                    reader.position++;
                    break;
                }
                if (!take(&reader, ',', "expected , or } after a member")) {
                    break;
                }
            }
        }
    }
    if (!reader.failed) {
        skip_space(&reader);
        if (reader.position != reader.length) {
            fail_at(&reader, "text follows the case's object");
        }
    }
    if (!reader.failed) {
        check_complete(&reader, result, &seen);
    }
    if (reader.failed) {
        pp_case_free(result);
        return false;
    }
    return true;
}

char *pp_read_file(const char *path, size_t limit, size_t *length, char *error, size_t error_size) {
    FILE *file = fopen(path, "rb");
    if (file == NULL) {
        char reason[128] = "unknown error";
        (void)strerror_r(errno, reason, sizeof reason);
        (void)snprintf(error, error_size, "cannot open %s: %s", path, reason);
        return NULL;
    }
    char *text = malloc(limit + 1U);
    size_t read = text == NULL ? 0U : fread(text, 1U, limit + 1U, file);
    bool failed = text == NULL;
    if (ferror(file) != 0) {
        failed = true;
    }
    (void)fclose(file);
    if (failed) {
        (void)snprintf(error, error_size, "cannot read %s", path);
        free(text);
        return NULL;
    }
    if (read > limit) {
        (void)snprintf(error, error_size, "%s is too long", path);
        free(text);
        return NULL;
    }
    text[read] = '\0';
    *length = read;
    return text;
}

bool pp_case_read(const char *path, pp_case_t *result, char *error, size_t error_size) {
    size_t length = 0;
    char *text = pp_read_file(path, PP_CASE_MAX_SIZE, &length, error, error_size);
    if (text == NULL) {
        memset(result, 0, sizeof *result);
        return false;
    }
    bool parsed = pp_case_parse(text, length, result, error, error_size);
    free(text);
    return parsed;
}

void pp_case_free(pp_case_t *value) {
    free(value->program);
    for (size_t index = 0; index < value->argument_count; index++) {
        free(value->arguments[index]);
    }
    free(value->expected);
    memset(value, 0, sizeof *value);
}
