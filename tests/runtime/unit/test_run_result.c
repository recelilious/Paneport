// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The result file of single mode, which the harness's child writes and its parent reads: what
// the child writes reads back the same, a text stays on its line, the items that do not fit are
// cut, a write that fails changes nothing, a file that is missing or malformed is refused, and
// the program's exit code is compared whole.

#include <limits.h>
#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/resource.h>
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

static void write_bytes(const char *path, const char *data, size_t length) {
    FILE *file = fopen(path, "wb");
    PP_CHECK(file != NULL);
    PP_CHECK_EQUAL(fwrite(data, 1U, length, file), length);
    PP_CHECK_EQUAL(fclose(file), 0);
}

static void write_raw(const char *path, const char *text) { write_bytes(path, text, strlen(text)); }

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
    pp_run_result_set_pool(&written, "regions 4 pool_fex_bytes 16842752 exec_pages_outside_pool 0");
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
    // The counters of the JIT code pool, which pool mode adds, and their reader.
    PP_CHECK(read.has_pool);
    PP_CHECK_TEXT(read.pool, "regions 4 pool_fex_bytes 16842752 exec_pages_outside_pool 0");
    uint64_t counter = 1U;
    PP_CHECK(pp_pool_counter(read.pool, "pool_fex_bytes", &counter));
    PP_CHECK(counter == 16842752U);
    PP_CHECK(pp_pool_counter(read.pool, "exec_pages_outside_pool", &counter));
    PP_CHECK(counter == 0U);
    PP_CHECK(!pp_pool_counter(read.pool, "regions 4", &counter));
    PP_CHECK(!pp_pool_counter(read.pool, "fex_bytes", &counter));
    PP_CHECK(!pp_pool_counter("regions four", "regions", &counter));
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

// Control characters become dots, so that a text stays on its line, and the failures and the
// reports of refused calls past PP_RESULT_ITEMS are cut: the file records that failures were
// cut, and the count of refused calls counts them all.
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
        pp_run_result_add_trap(&result, "fork from ntdll.so");
    }
    PP_CHECK_EQUAL(result.failure_count, PP_RESULT_ITEMS);
    PP_CHECK(result.failures_cut);
    PP_CHECK_EQUAL(result.trap_lines, PP_RESULT_ITEMS);
    PP_CHECK_TEXT(result.failures[0], "first.second.third");
    PP_CHECK_TEXT(result.traps[0], "a.b");
    result.trap_count = PP_RESULT_ITEMS + 4U;
    PP_CHECK(pp_run_result_write(&result, path));
    pp_run_result_t read;
    char error[256] = "";
    PP_CHECK(pp_run_result_read(path, &read, error, sizeof error));
    PP_CHECK_EQUAL(read.failure_count, PP_RESULT_ITEMS);
    PP_CHECK(read.failures_cut);
    PP_CHECK_TEXT(read.failures[0], "first.second.third");
    PP_CHECK_TEXT(read.failures[PP_RESULT_ITEMS - 1U], "failure 15");
    PP_CHECK_EQUAL(read.trap_count, PP_RESULT_ITEMS + 4U);
    PP_CHECK_EQUAL(read.trap_lines, PP_RESULT_ITEMS);
    PP_CHECK_TEXT(read.traps[0], "a.b");
    remove_path(folder, path);
}

// A write that fails, here at the file size limit, leaves the result that was there before as it
// was and nothing beside it: the writer writes another file and renames it, so that a reader
// finds a whole result or none, never a part of one, which could read as a run without failures.
static void failed_write_changes_nothing(void) {
    char folder[PATH_MAX];
    char path[PATH_MAX];
    make_path(folder, sizeof folder, path, sizeof path);
    pp_run_result_t first;
    pp_run_result_init(&first);
    first.has_exit_code = true;
    first.exit_code = 3U;
    PP_CHECK(pp_run_result_write(&first, path));
    pp_run_result_t second;
    pp_run_result_init(&second);
    char text[400];
    memset(text, 'x', sizeof text - 1U);
    text[sizeof text - 1U] = '\0';
    pp_run_result_add_failure(&second, "%s", text);
    // Ignored, SIGXFSZ lets the write fail with EFBIG instead of ending the test.
    struct sigaction ignore;
    memset(&ignore, 0, sizeof ignore);
    ignore.sa_handler = SIG_IGN;
    (void)sigemptyset(&ignore.sa_mask);
    struct sigaction saved_action;
    PP_CHECK_EQUAL(sigaction(SIGXFSZ, &ignore, &saved_action), 0);
    struct rlimit saved_limit;
    PP_CHECK_EQUAL(getrlimit(RLIMIT_FSIZE, &saved_limit), 0);
    struct rlimit limit = saved_limit;
    limit.rlim_cur = 64U;
    PP_CHECK_EQUAL(setrlimit(RLIMIT_FSIZE, &limit), 0);
    const bool written = pp_run_result_write(&second, path);
    PP_CHECK_EQUAL(setrlimit(RLIMIT_FSIZE, &saved_limit), 0);
    PP_CHECK_EQUAL(sigaction(SIGXFSZ, &saved_action, NULL), 0);
    PP_CHECK(!written);
    pp_run_result_t read;
    char error[256] = "";
    PP_CHECK(pp_run_result_read(path, &read, error, sizeof error));
    PP_CHECK(read.has_exit_code);
    PP_CHECK_EQUAL(read.exit_code, 3U);
    PP_CHECK_EQUAL(read.failure_count, 0);
    char temporary[PATH_MAX];
    PP_CHECK(snprintf(temporary, sizeof temporary, "%s.new", path) > 0);
    struct stat status;
    PP_CHECK(stat(temporary, &status) != 0);
    remove_path(folder, path);
}

// Single mode compares the program's whole Windows exit code: a code whose low 8 bits are the
// expected ones fails too, and a code above 255 is named in hexadecimal as well.
static void exit_code_compared_whole(void) {
    char reason[128] = "";
    PP_CHECK(pp_run_result_exit_code_matches(0U, 0, reason, sizeof reason));
    PP_CHECK(pp_run_result_exit_code_matches(3U, 3, reason, sizeof reason));
    PP_CHECK_TEXT(reason, "");
    PP_CHECK(!pp_run_result_exit_code_matches(3U, 4, reason, sizeof reason));
    PP_CHECK_TEXT(reason, "exited with 3, not 4");
    PP_CHECK(!pp_run_result_exit_code_matches(0x100U, 0, reason, sizeof reason));
    PP_CHECK_TEXT(reason, "exited with 256, not 0 (Windows exit code 0x100)");
    // STILL_ACTIVE, whose low 8 bits are 3.
    PP_CHECK(!pp_run_result_exit_code_matches(0x103U, 3, reason, sizeof reason));
    PP_CHECK_TEXT(reason, "exited with 259, not 3 (Windows exit code 0x103)");
    PP_CHECK(!pp_run_result_exit_code_matches(0xC0000100U, 0, reason, sizeof reason));
    PP_CHECK_TEXT(reason, "exited with 3221225728, not 0 (Windows exit code 0xc0000100)");
}

static void missing_file_named(void) {
    pp_run_result_t result;
    char error[256] = "";
    PP_CHECK(
        !pp_run_result_read("/nonexistent-paneport/run-result.txt", &result, error, sizeof error));
    PP_CHECK_TEXT(error, "there is no result file");
    PP_CHECK(!result.has_exit_code && result.failure_count == 0U);
}

// Checks that a file of these bytes is refused with the message, and leaves the result cleared.
static void refused_with(const char *path, const char *data, size_t length, const char *message) {
    write_bytes(path, data, length);
    pp_run_result_t result;
    char error[256] = "";
    PP_CHECK(!pp_run_result_read(path, &result, error, sizeof error));
    if (strcmp(error, message) != 0) {
        (void)fprintf(stderr, "for \"%s\": \"%s\", not \"%s\"\n", data, error, message);
        PP_CHECK(false);
    }
    PP_CHECK(!result.has_exit_code && result.trap_count == 0U && result.failure_count == 0U &&
             result.trap_lines == 0U);
}

// Checks that a file with this text is refused at the line.
static void refused(const char *path, const char *text, unsigned line) {
    char message[64];
    (void)snprintf(message, sizeof message, "line %u of the result file is malformed", line);
    refused_with(path, text, strlen(text), message);
}

// Writes into text, of size bytes, the line head followed by count lines "<item> <n>".
static void repeated_lines(char *text, size_t size, const char *head, const char *item,
                           unsigned count) {
    (void)snprintf(text, size, "%s", head);
    for (unsigned index = 0U; index < count; index++) {
        const size_t used = strlen(text);
        PP_CHECK(used + 1U < size);
        (void)snprintf(text + used, size - used, "%s %u\n", item, index);
    }
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
    // 2^64, which a reader without its limit of 10 digits would read as 0.
    refused(path, "exit_code 18446744073709551616\ntrap_count 0\n", 1U);
    refused(path, "trap_count 0\nexit_code 1\nexit_code 2\n", 3U);
    refused(path, "trap_count 0\ncolor 1\n", 2U);
    refused(path, "failure\n", 1U);
    refused(path, "failures_cut 2\n", 1U);
    refused(path, "trap_count x\n", 1U);
    refused(path, "trap_count 01\n", 1U);
    // trap_count comes once, and the trap lines after it, no more of them than it counts.
    refused(path, "trap_count 2\ntrap_count 0\n", 2U);
    refused(path, "trap fork from ntdll.so\ntrap_count 1\n", 1U);
    refused(path, "trap_count 0\ntrap fork from ntdll.so\n", 2U);
    refused(path, "trap_count 1\ntrap fork from ntdll.so\ntrap execv from ntdll.so\n", 3U);
    static const char k_no_count[] = "exit_code 0\nfailure the session ended with 1, not 0\n";
    refused_with(path, k_no_count, strlen(k_no_count), "the result file has no trap_count line");
    // A NUL would hide the rest of the file from a reader that stopped at it.
    static const char k_hidden[] = "trap_count 0\n\0failure the session ended with 1, not 0\n";
    refused_with(path, k_hidden, sizeof k_hidden - 1U, "line 1 of the result file is malformed");
    char text[4096];
    repeated_lines(text, sizeof text, "trap_count 17\n", "trap call", PP_RESULT_ITEMS + 1U);
    refused(path, text, PP_RESULT_ITEMS + 2U);
    repeated_lines(text, sizeof text, "trap_count 0\n", "failure", PP_RESULT_ITEMS + 1U);
    refused(path, text, PP_RESULT_ITEMS + 2U);
    remove_path(folder, path);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"round_trip", round_trip},
        {"exit_codes", exit_codes},
        {"texts_stay_on_their_lines", texts_stay_on_their_lines},
        {"failed_write_changes_nothing", failed_write_changes_nothing},
        {"exit_code_compared_whole", exit_code_compared_whole},
        {"missing_file_named", missing_file_named},
        {"malformed_files_refused", malformed_files_refused},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
