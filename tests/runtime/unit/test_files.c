// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: every case and expected output that tests/runtime holds parses, and every case names
// a program of tests/win32 and an expected output that exists, so that a mistake in them fails
// here, without Wine.

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
    return length > suffix_length && strcmp(name + length - suffix_length, suffix) == 0;
}

static bool exists(const char *path) {
    struct stat status;
    return stat(path, &status) == 0 && S_ISREG(status.st_mode);
}

static void join(char *path, const char *folder, const char *name) {
    int length = snprintf(path, PP_PATH_SIZE, "%s/%s/%s", PP_REPOSITORY_ROOT, folder, name);
    PP_CHECK(length > 0 && (size_t)length < PP_PATH_SIZE);
}

// Checks every case file in folder and returns how many there were. Planted cases may name an
// expected output that eng/mac/run-harness.sh derives from a real one.
static unsigned check_cases(const char *folder, bool planted) {
    char path[PP_PATH_SIZE];
    join(path, folder, "");
    DIR *directory = opendir(path);
    PP_CHECK(directory != NULL);
    unsigned count = 0;
    for (struct dirent *entry = readdir(directory); entry != NULL; entry = readdir(directory)) {
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
        char program[PP_PATH_SIZE];
        char source[256];
        (void)snprintf(source, sizeof source, "%.*s.c", (int)(strlen(test.program) - 4U),
                       test.program);
        join(program, "tests/win32/programs", source);
        if (!exists(program)) {
            (void)fprintf(stderr, "%s names %s, but %s does not exist\n", path, test.program,
                          program);
            PP_CHECK(false);
        }
        char expected[PP_PATH_SIZE];
        join(expected, "tests/runtime/expected", test.expected);
        if (!planted && !exists(expected)) {
            (void)fprintf(stderr, "%s names %s, which does not exist\n", path, test.expected);
            PP_CHECK(false);
        }
        pp_case_free(&test);
        count++;
    }
    (void)closedir(directory);
    return count;
}

static void cases_are_valid(void) {
    PP_CHECK(check_cases("tests/runtime/cases", false) >= 7U);
    PP_CHECK_EQUAL(check_cases("tests/runtime/planted", true), 2);
}

static void expected_outputs_are_valid(void) {
    char path[PP_PATH_SIZE];
    join(path, "tests/runtime/expected", "");
    DIR *directory = opendir(path);
    PP_CHECK(directory != NULL);
    unsigned count = 0;
    for (struct dirent *entry = readdir(directory); entry != NULL; entry = readdir(directory)) {
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
