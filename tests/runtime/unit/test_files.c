// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: every case and expected output that tests/runtime holds parses, and every case names
// a program of tests/win32 and an expected output that exists, so that a mistake in them fails
// here, without Wine. A session case names neither; every kind of session case is there once at
// least (S4.3.2, S4.3.4 and the session cases of M4.3's verification).

#include <dirent.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>

#include "case_file.h"
#include "expected.h"
#include "pp_check.h"

#ifndef PP_REPOSITORY_ROOT
#error "tests/runtime/CMakeLists.txt defines PP_REPOSITORY_ROOT."
#endif

#define PP_PATH_SIZE 1024U

static bool has_suffix(const char *name, const char *suffix) {
    size_t length = strlen(name);
    size_t suffix_length = strlen(suffix);
    if (length <= suffix_length) {
        return false;
    }
    return strcmp(name + length - suffix_length, suffix) == 0;
}

static bool exists(const char *path) {
    struct stat status;
    if (stat(path, &status) != 0) {
        return false;
    }
    return S_ISREG(status.st_mode) != 0;
}

// The test runs on one thread, so readdir's static state is no concern.
static struct dirent *next_entry(DIR *directory) {
    return readdir(directory); // NOLINT(concurrency-mt-unsafe)
}

static void join(char *path, const char *folder, const char *name) {
    int length = snprintf(path, PP_PATH_SIZE, "%s/%s/%s", PP_REPOSITORY_ROOT, folder, name);
    PP_CHECK(length > 0 && (size_t)length < PP_PATH_SIZE);
}

// Checks that a program case names a program of tests/win32 and, unless it is planted, an
// expected output that exists. Planted cases may name an expected output that
// eng/mac/run-harness.sh derives from a real one.
static void check_program_case(const char *path, const pp_case_t *test, bool planted) {
    char program[PP_PATH_SIZE];
    char source[256];
    (void)snprintf(source, sizeof source, "%.*s.c", (int)(strlen(test->program) - 4U),
                   test->program);
    join(program, "tests/win32/programs", source);
    if (!exists(program)) {
        (void)fprintf(stderr, "%s names %s, but %s does not exist\n", path, test->program, program);
        PP_CHECK(false);
    }
    char expected[PP_PATH_SIZE];
    join(expected, "tests/runtime/expected", test->expected);
    if (!planted && !exists(expected)) {
        (void)fprintf(stderr, "%s names %s, which does not exist\n", path, test->expected);
        PP_CHECK(false);
    }
}

// Checks every case file in folder and returns how many there were; kinds receives a bit for
// each kind of case among them, 1 << kind.
static unsigned check_cases(const char *folder, bool planted, unsigned *kinds) {
    char path[PP_PATH_SIZE];
    join(path, folder, "");
    DIR *directory = opendir(path);
    PP_CHECK(directory != NULL);
    unsigned count = 0;
    *kinds = 0U;
    for (struct dirent *entry = next_entry(directory); entry != NULL;
         entry = next_entry(directory)) {
        if (!has_suffix(entry->d_name, ".json")) {
            continue;
        }
        join(path, folder, entry->d_name);
        pp_case_t test;
        char error[256] = "";
        if (!pp_case_read(path, &test, error, sizeof error)) {
            (void)fprintf(stderr, "%s: %s\n", path, error);
            PP_CHECK(false);
        }
        if (test.kind == PP_CASE_PROGRAM) {
            check_program_case(path, &test, planted);
        }
        *kinds |= 1U << (unsigned)test.kind;
        pp_case_free(&test);
        count++;
    }
    (void)closedir(directory);
    return count;
}

static void cases_are_valid(void) {
    unsigned kinds = 0U;
    PP_CHECK(check_cases("tests/runtime/cases", false, &kinds) >= 14U);
    const unsigned every_kind =
        (1U << (unsigned)PP_CASE_PROGRAM) | (1U << (unsigned)PP_CASE_START_STOP) |
        (1U << (unsigned)PP_CASE_SECOND_START) | (1U << (unsigned)PP_CASE_JIT_NOT_READY) |
        (1U << (unsigned)PP_CASE_DISPATCHER_CHECK);
    PP_CHECK_EQUAL(kinds, every_kind);
    // The planted cases run a program, whose wrong line and wrong exit code the runtime job
    // looks for in their lines.
    PP_CHECK_EQUAL(check_cases("tests/runtime/planted", true, &kinds), 2);
    PP_CHECK_EQUAL(kinds, 1U << (unsigned)PP_CASE_PROGRAM);
}

static void expected_outputs_are_valid(void) {
    char path[PP_PATH_SIZE];
    join(path, "tests/runtime/expected", "");
    DIR *directory = opendir(path);
    PP_CHECK(directory != NULL);
    unsigned count = 0;
    for (struct dirent *entry = next_entry(directory); entry != NULL;
         entry = next_entry(directory)) {
        if (!has_suffix(entry->d_name, ".txt")) {
            continue;
        }
        join(path, "tests/runtime/expected", entry->d_name);
        pp_expected_t expected;
        char error[256] = "";
        if (!pp_expected_read(path, &expected, error, sizeof error)) {
            (void)fprintf(stderr, "%s: %s\n", path, error);
            PP_CHECK(false);
        }
        // Every program ends with its result line.
        PP_CHECK(expected.count >= 2U);
        PP_CHECK_TEXT(expected.lines[expected.count - 1U].text, "result: pass");
        pp_expected_free(&expected);
        count++;
    }
    (void)closedir(directory);
    PP_CHECK(count >= 6U);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"cases_are_valid", cases_are_valid},
        {"expected_outputs_are_valid", expected_outputs_are_valid},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
