// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The result of one run of single mode, which the harness's child writes and the parent reads
// once the child has ended (docs/design/runtime-macos-harness.md section 5.1). It travels in a file
// of its own, in the case's log folder, because the child's exit status cannot carry it: in multi
// mode the loader's exit status is the program's exit code, but in single mode the child exits
// with 0 when the harness's side of the run worked, and a call of exit from inside Wine would
// otherwise pass for the program's end. The child writes the file under another name and renames
// it, so that the parent finds the whole file or none. It holds one item per line, the item's
// name, a space and its value:
//
//   exit_code <n>    the program's Windows exit code, from its process-exited event, in decimal
//   trap_count <n>   how many calls the fork and exec trap refused, in decimal
//   trap <text>      the report of one of them, for the first PP_RESULT_ITEMS
//   failure <text>   something that the child found wrong with the run, for the first
//                    PP_RESULT_ITEMS; failures_cut says that more were found
//   failures_cut 1
//
// A text holds no control character. Every function here runs on one thread.

#ifndef PP_HARNESS_RUN_RESULT_H
#define PP_HARNESS_RUN_RESULT_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

// The file's name in the case's log folder.
#define PP_RESULT_FILE "run-result.txt"

// How many reports of the trap and how many failures the result keeps, and the size of each.
#define PP_RESULT_ITEMS 16U
#define PP_RESULT_TEXT_SIZE 512U

typedef struct pp_run_result_t {
    bool has_exit_code;
    uint32_t exit_code;
    uint32_t trap_count;
    char traps[PP_RESULT_ITEMS][PP_RESULT_TEXT_SIZE];
    size_t trap_lines;
    char failures[PP_RESULT_ITEMS][PP_RESULT_TEXT_SIZE];
    size_t failure_count;
    bool failures_cut;
} pp_run_result_t;

// Clears a result: no exit code, no trap and no failure.
void pp_run_result_init(pp_run_result_t *result);

// Adds a failure, formatted as printf does and cut to fit, with every control character shown as
// a dot. Past PP_RESULT_ITEMS failures it sets failures_cut instead.
void pp_run_result_add_failure(pp_run_result_t *result, const char *format, ...)
    __attribute__((format(printf, 2, 3)));

// Adds the report of a refused call, as pp_run_result_add_failure adds a failure; past
// PP_RESULT_ITEMS it adds nothing, since trap_count counts every call.
void pp_run_result_add_trap(pp_run_result_t *result, const char *report);

// Writes the result to path, through path with ".new" added and a rename. Returns whether it did.
bool pp_run_result_write(const pp_run_result_t *result, const char *path);

// Reads the result at path. Returns false, with a message in error, when there is no such file
// or it is malformed.
bool pp_run_result_read(const char *path, pp_run_result_t *result, char *error, size_t error_size);

#endif
