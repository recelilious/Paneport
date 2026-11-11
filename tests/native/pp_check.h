// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The small assertion header of the native tests (C-0011 section 1). A failed check prints
// its file, line and expression, then aborts, which CTest reports as a failure. Each test
// program runs the one case that its command line names, in a process of its own, because
// pp_init runs once per process; tests/native/CMakeLists.txt registers every case.

#ifndef PANEPORT_TESTS_PP_CHECK_H
#define PANEPORT_TESTS_PP_CHECK_H

#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static inline void pp_check_failed(const char *file, int line, const char *expression) {
    (void)fprintf(stderr, "%s:%d: check failed: %s\n", file, line, expression);
    (void)fflush(stderr);
    abort();
}

static inline void pp_check_equal_failed(const char *file, int line, const char *expression,
                                         long long actual, long long expected) {
    (void)fprintf(stderr, "%s:%d: check failed: %s (found %lld, expected %lld)\n", file, line,
                  expression, actual, expected);
    (void)fflush(stderr);
    abort();
}

// Checks that a condition holds.
#define PP_CHECK(condition)                                                                        \
    do {                                                                                           \
        if (!(condition)) {                                                                        \
            pp_check_failed(__FILE__, __LINE__, #condition);                                       \
        }                                                                                          \
    } while (false)

// Checks that two integers are equal, and prints both when they are not. Unsigned 64-bit
// values compare correctly too, because the conversion keeps their bits.
#define PP_CHECK_EQUAL(actual, expected)                                                           \
    do {                                                                                           \
        const long long pp_actual_value = (long long)(actual);                                     \
        const long long pp_expected_value = (long long)(expected);                                 \
        if (pp_actual_value != pp_expected_value) {                                                \
            pp_check_equal_failed(__FILE__, __LINE__, #actual " == " #expected, pp_actual_value,   \
                                  pp_expected_value);                                              \
        }                                                                                          \
    } while (false)

// Checks that two NUL-terminated texts are equal.
#define PP_CHECK_TEXT(actual, expected) PP_CHECK(strcmp((actual), (expected)) == 0)

// One case of a test program.
typedef struct pp_test_case_t {
    const char *name;
    void (*run)(void);
} pp_test_case_t;

// Runs the case that argv[1] names. Returns the exit code for main.
static inline int pp_run_case(int argc, char **argv, const pp_test_case_t *cases, size_t count) {
    if (argc == 2) {
        for (size_t index = 0; index < count; index++) {
            if (strcmp(argv[1], cases[index].name) == 0) {
                cases[index].run();
                (void)printf("%s: passed\n", cases[index].name);
                return EXIT_SUCCESS;
            }
        }
    }
    (void)fprintf(stderr, "usage: %s <case>, where the case is one of:\n", argv[0]);
    for (size_t index = 0; index < count; index++) {
        (void)fprintf(stderr, "  %s\n", cases[index].name);
    }
    return 2;
}

#define PP_CASE_COUNT(cases) (sizeof(cases) / sizeof((cases)[0]))

#endif // PANEPORT_TESTS_PP_CHECK_H
