// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The result file of single mode, which the harness's child writes and its parent reads: what
// the child writes reads back the same, a text stays on its line, the items that do not fit are
// cut, and a file that is missing or malformed is refused.

#include <limits.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#include "pp_check.h"
#include "run_result.h"

// A folder of its own for the test's result file, whose path is written to path.
static void make_path(char *folder, size_t folder_size, char *path, size_t path_size) {
    PP_CHECK(snprintf(folder, folder_size, "/tmp/paneport-result-XXXXXX") > 0);
    PP_CHECK(mkdtemp(folder) != NULL);
    const int length = snprintf(path, path_size, "%s/%s", folder, PP_RESULT_FILE);
    PP_CHECK(length > 0 && (size_t)length < path_size);
}

static void remove_path(const char *folder, const char *path) {
    (void)unlink(path);
    PP_CHECK_EQUAL(rmdir(folder), 0);
}

static void write_raw(const char *path, const char *text) {
    FILE *file = fopen(path, "wb");
    PP_CHECK(file != NULL);
    PP_CHECK(fputs(text, file) >= 0);
    PP_CHECK_EQUAL(fclose(file), 0);
}

static void round_trip(void) {
    char folder[PATH_MAX];
    char path[PATH_MAX];
    make_path(folder, sizeof folder, path, sizeof path);
    pp_run_result_t written;
    pp_run_result_init(&written);
    written.has_exit_code = true;
    written.exit_code = 0xC0000135U;
    written.trap_count = 2U;
    pp_run_result_add_trap(&written, "posix_spawn from ntdll.so!spawn_process+0x48");
    pp_run_result_add_trap(&written, "kill(SIGUSR1) from ntdll.so+0x1c0");
    pp_run_result_add_failure(&written, "pp_session_stop returned %s, not %s", "PP_E_SYSTEM",
                              "PP_OK");
    PP_CHECK(pp_run_result_write(&written, path));
    // Nothing is left beside the result.
    char temporary[PATH_MAX];
    PP_CHECK(snprintf(temporary, sizeof temporary, "%s.new", path) > 0);
    struct stat status;
    PP_CHECK(stat(temporary, &status) != 0);

    pp_run_result_t read;
    char error[256] = "";
    PP_CHECK(pp_run_result_read(path, &read, error, sizeof error));
    PP_CHECK(read.has_exit_code);
    PP_CHECK_EQUAL(read.exit_code, 0xC0000135U);
    PP_CHECK_EQUAL(read.trap_count, 2);
    PP_CHECK_EQUAL(read.trap_lines, 2);
    PP_CHECK_TEXT(read.traps[0], "posix_spawn from ntdll.so!spawn_process+0x48");
    PP_CHECK_TEXT(read.traps[1], "kill(SIGUSR1) from ntdll.so+0x1c0");
    PP_CHECK_EQUAL(read.failure_count, 1);
    PP_CHECK_TEXT(read.failures[0], "pp_session_stop returned PP_E_SYSTEM, not PP_OK");
    PP_CHECK(!read.failures_cut);
    remove_path(folder, path);
}

// A run that found nothing writes no exit code when the program never reported one, and the
// largest exit code reads back whole.
static void exit_codes(void) {
    char folder[PATH_MAX];
    char path[PATH_MAX];
    make_path(folder, sizeof folder, path, sizeof path);
    pp_run_result_t result;
    pp_run_result_init(&result);
    PP_CHECK(pp_run_result_write(&result, path));
    char error[256] = "";
    PP_CHECK(pp_run_result_read(path, &result, error, sizeof error));
    PP_CHECK(!result.has_exit_code);
    PP_CHECK_EQUAL(result.trap_count, 0);
    PP_CHECK_EQUAL(result.failure_count, 0);
    write_raw(path, "exit_code 4294967295\ntrap_count 0\n");
    PP_CHECK(pp_run_result_read(path, &result, error, sizeof error));
    PP_CHECK_EQUAL(result.exit_code, UINT32_MAX);
    remove_path(folder, path);
}

// Control characters become dots, so that a text stays on its line, and the failures past
// PP_RESULT_ITEMS are cut, which the file records.
static void texts_stay_on_their_lines(void) {
    char folder[PATH_MAX];
    char path[PATH_MAX];
    make_path(folder, sizeof folder, path, sizeof path);
    pp_run_result_t result;
    pp_run_result_init(&result);
    pp_run_result_add_failure(&result, "first\nsecond\tthird");
    pp_run_result_add_trap(&result, "a\rb");
    for (unsigned index = 1U; index < PP_RESULT_ITEMS + 4U; index++) {
        pp_run_result_add_failure(&result, "failure %u", index);
    }
    PP_CHECK_EQUAL(result.failure_count, PP_RESULT_ITEMS);
    PP_CHECK(result.failures_cut);
    PP_CHECK_TEXT(result.failures[0], "first.second.third");
    PP_CHECK_TEXT(result.traps[0], "a.b");
    result.trap_count = 1U;
    PP_CHECK(pp_run_result_write(&result, path));
    pp_run_result_t read;
    char error[256] = "";
    PP_CHECK(pp_run_result_read(path, &read, error, sizeof error));
    PP_CHECK_EQUAL(read.failure_count, PP_RESULT_ITEMS);
    PP_CHECK(read.failures_cut);
    PP_CHECK_TEXT(read.failures[0], "first.second.third");
    PP_CHECK_TEXT(read.failures[PP_RESULT_ITEMS - 1U], "failure 15");
    PP_CHECK_TEXT(read.traps[0], "a.b");
    remove_path(folder, path);
}

static void missing_file_named(void) {
    pp_run_result_t result;
    char error[256] = "";
    PP_CHECK(
        !pp_run_result_read("/nonexistent-paneport/run-result.txt", &result, error, sizeof error));
    PP_CHECK_TEXT(error, "there is no result file");
    PP_CHECK(!result.has_exit_code && result.failure_count == 0U);
}

// Checks that a file with this text is refused at the line.
static void refused(const char *path, const char *text, unsigned line) {
    write_raw(path, text);
    pp_run_result_t result;
    char error[256] = "";
    PP_CHECK(!pp_run_result_read(path, &result, error, sizeof error));
    char expected[64];
    (void)snprintf(expected, sizeof expected, "line %u of the result file is malformed", line);
    if (strcmp(error, expected) != 0) {
        (void)fprintf(stderr, "for \"%s\": \"%s\", not \"%s\"\n", text, error, expected);
        PP_CHECK(false);
    }
    PP_CHECK(!result.has_exit_code && result.failure_count == 0U && result.trap_lines == 0U);
}

static void malformed_files_refused(void) {
    char folder[PATH_MAX];
    char path[PATH_MAX];
    make_path(folder, sizeof folder, path, sizeof path);
    refused(path, "", 1U);
    refused(path, "trap_count 0", 1U);
    refused(path, "exit_code x\n", 1U);
    refused(path, "exit_code \n", 1U);
    refused(path, "exit_code 01\n", 1U);
    refused(path, "exit_code -1\n", 1U);
    refused(path, "exit_code 4294967296\n", 1U);
    refused(path, "trap_count 0\nexit_code 1\nexit_code 2\n", 3U);
    refused(path, "trap_count 0\ncolor 1\n", 2U);
    refused(path, "failure\n", 1U);
    refused(path, "failures_cut 2\n", 1U);
    char text[4096] = "trap_count 17\n";
    for (unsigned index = 0U; index <= PP_RESULT_ITEMS; index++) {
        const size_t used = strlen(text);
        (void)snprintf(text + used, sizeof text - used, "trap call %u\n", index);
    }
    refused(path, text, PP_RESULT_ITEMS + 2U);
    remove_path(folder, path);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"round_trip", round_trip},
        {"exit_codes", exit_codes},
        {"texts_stay_on_their_lines", texts_stay_on_their_lines},
        {"missing_file_named", missing_file_named},
        {"malformed_files_refused", malformed_files_refused},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
