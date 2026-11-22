// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The case files of tests/runtime/cases. A case is a JSON object with these members:
//
//   "program"          the file name of a program in the programs folder, ending in .exe
//   "arguments"        optional: an array of strings, the program's arguments
//   "environment"      optional: an object whose members are environment variables, each
//                      with its value as a string, which every run of the program gets on top
//                      of the harness's own environment. A name is capital letters, digits and
//                      underscores, does not start with a digit, and is not WINEPREFIX
//   "runs"             optional: how many times the program runs, from 1 to 100, 1 when it is
//                      left out. The case passes only when every run passes, and its runs stop
//                      at the first that fails
//   "timeout_seconds"  how long each run of the program may take, from 1 to 3600
//   "exit_code"        the exit code the program must return, from 0 to 255
//   "expected"         the file name of its expected output in tests/runtime/expected, ending
//                      in .txt
//
// Any other member, a missing one or a value of another type is an error, so that a typo fails
// the case instead of being ignored. Strings are ASCII; the reader refuses the JSON escape of a
// code point. Every function here runs on the harness's only thread.

#ifndef PP_HARNESS_CASE_FILE_H
#define PP_HARNESS_CASE_FILE_H

#include <stdbool.h>
#include <stddef.h>

#define PP_CASE_MAX_ARGUMENTS 16U
#define PP_CASE_MAX_VARIABLES 16U
#define PP_CASE_MAX_RUNS 100
#define PP_CASE_MAX_SIZE ((size_t)64U * 1024U)
#define PP_CASE_MAX_ENVIRONMENT 4096U

// One case. The strings are owned by the case and freed by pp_case_free.
typedef struct pp_case_t {
    char *program;
    char *arguments[PP_CASE_MAX_ARGUMENTS];
    size_t argument_count;
    // The environment's variables, each as "NAME=value".
    char *variables[PP_CASE_MAX_VARIABLES];
    size_t variable_count;
    unsigned runs;
    unsigned timeout_seconds;
    int exit_code;
    char *expected;
} pp_case_t;

// Parses the text of a case file, length bytes long. On success it fills result, which the
// caller frees with pp_case_free; on failure it writes a message with the line number into
// error and leaves nothing to free.
bool pp_case_parse(const char *text, size_t length, pp_case_t *result, char *error,
                   size_t error_size);

// Reads and parses the case file at path, as pp_case_parse does.
bool pp_case_read(const char *path, pp_case_t *result, char *error, size_t error_size);

// The environment of a case's runs: the first PP_CASE_MAX_ENVIRONMENT entries of base, the
// NULL-terminated environment that the harness got, without WINEPREFIX and without the variables
// that the case sets; then the case's variables; then WINEPREFIX naming prefix. The caller frees
// the array and the new WINEPREFIX, which owned returns; the other strings stay owned by base
// and by the case. Returns NULL when out of memory.
char **pp_case_environment(const pp_case_t *test, char *const base[], const char *prefix,
                           char **owned);

// Frees what pp_case_parse put in a case, and clears it.
void pp_case_free(pp_case_t *value);

// Reads the whole file at path into a NUL-terminated buffer of at most limit bytes, which the
// caller frees. Shared with the reader of expected outputs.
char *pp_read_file(const char *path, size_t limit, size_t *length, char *error, size_t error_size);

#endif
