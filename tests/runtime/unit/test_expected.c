// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: the harness's expected outputs and their comparison with a program's output.

#include <stdbool.h>
#include <string.h>

#include "expected.h"
#include "pp_check.h"

static pp_expected_t parsed(const char *text) {
    pp_expected_t expected;
    char error[256] = "";
    if (!pp_expected_parse(text, strlen(text), &expected, error, sizeof error)) {
        (void)fprintf(stderr, "the expected text does not parse: %s\n", error);
        PP_CHECK(false);
    }
    return expected;
}

static pp_comparison_t compared(const char *expected_text, const char *output) {
    pp_expected_t expected = parsed(expected_text);
    pp_comparison_t comparison;
    pp_expected_compare(&expected, output, strlen(output), &comparison);
    pp_expected_free(&expected);
    return comparison;
}

static void holds(const char *message, const char *part) {
    if (strstr(message, part) == NULL) {
        (void)fprintf(stderr, "the message \"%s\" does not hold \"%s\"\n", message, part);
        PP_CHECK(false);
    }
}

static void literal_and_pattern_lines(void) {
    pp_comparison_t comparison = compared(
        "= console: hello from an x86-64 build\n~ checksum: 0x[0-9a-f]{16}, [0-9]+ MiB/s\n",
        "console: hello from an x86-64 build\nchecksum: 0x5e75a40f80343ce2, 812 MiB/s\n");
    PP_CHECK(comparison.matches);
    PP_CHECK_EQUAL(comparison.first_difference, 0);
    PP_CHECK_EQUAL(comparison.expected_count, 2);
    PP_CHECK_EQUAL(comparison.output_count, 2);
}

static void carriage_returns(void) {
    PP_CHECK(compared("= a\n~ b [0-9]\n", "a\r\nb 7\r\n").matches);
}

static void pattern_matches_whole_line(void) {
    pp_comparison_t comparison = compared("~ b|c\n", "b and more\n");
    PP_CHECK(!comparison.matches);
    PP_CHECK_EQUAL(comparison.first_difference, 1);
    PP_CHECK(compared("~ b|c\n", "c\n").matches);
}

static void literal_is_not_a_pattern(void) {
    PP_CHECK(!compared("= a.c\n", "abc\n").matches);
    PP_CHECK(compared("= a.c (x)\n", "a.c (x)\n").matches);
}

static void first_difference_named(void) {
    pp_comparison_t comparison = compared("= a\n= b\n= c\n", "a\nB\nc\n");
    PP_CHECK(!comparison.matches);
    PP_CHECK_EQUAL(comparison.first_difference, 2);
    holds(comparison.message, "line 2 differs");
    holds(comparison.message, "printed B");
}

static void missing_lines(void) {
    pp_comparison_t comparison = compared("= a\n= result: pass\n", "a\n");
    PP_CHECK(!comparison.matches);
    PP_CHECK_EQUAL(comparison.first_difference, 2);
    holds(comparison.message, "ends after 1 lines");
    PP_CHECK(!compared("= a\n", "").matches);
}

static void extra_lines(void) {
    pp_comparison_t comparison = compared("= a\n", "a\nfixme: more\n");
    PP_CHECK(!comparison.matches);
    PP_CHECK_EQUAL(comparison.first_difference, 2);
    holds(comparison.message, "more than the 1 expected lines");
    PP_CHECK(compared("", "").matches);
}

static void control_characters_quoted(void) {
    pp_comparison_t comparison = compared("= a\n", "a\x1b[31mb\n");
    PP_CHECK(!comparison.matches);
    holds(comparison.message, "printed a.[31mb");
}

static void bad_expected_files(void) {
    pp_expected_t expected;
    char error[256] = "";
    PP_CHECK(!pp_expected_parse("a\n", 2U, &expected, error, sizeof error));
    holds(error, "line 1");
    PP_CHECK(!pp_expected_parse("= a\n=b\n", 7U, &expected, error, sizeof error));
    holds(error, "line 2");
    PP_CHECK(!pp_expected_parse("~ (\n", 4U, &expected, error, sizeof error));
    holds(error, "does not compile");
    PP_CHECK(!pp_expected_parse("= a", 3U, &expected, error, sizeof error));
    holds(error, "line feed");
    PP_CHECK(!pp_expected_parse("= a\n\n", 5U, &expected, error, sizeof error));
    holds(error, "line 2");
}

static void unhandled_reported(void) {
    char line[256] = "";
    const char *errors = "0024:fixme:ntdll:NtQuerySystemInformation info_class 0\n"
                         "wine: Unhandled page fault on write access to 0000000000000010 at "
                         "address 0000000140001234 (thread 0024), starting debugger...\n";
    PP_CHECK(pp_wine_reported_unhandled(errors, strlen(errors), line, sizeof line));
    holds(line, "wine: Unhandled page fault on write access");
    const char *quiet = "0024:fixme:ntdll:NtQuerySystemInformation info_class 0\n";
    PP_CHECK(!pp_wine_reported_unhandled(quiet, strlen(quiet), line, sizeof line));
    const char *debugger = "Unhandled exception: page fault on read access to 0x00000000\n";
    PP_CHECK(pp_wine_reported_unhandled(debugger, strlen(debugger), line, sizeof line));
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"literal_and_pattern_lines", literal_and_pattern_lines},
        {"carriage_returns", carriage_returns},
        {"pattern_matches_whole_line", pattern_matches_whole_line},
        {"literal_is_not_a_pattern", literal_is_not_a_pattern},
        {"first_difference_named", first_difference_named},
        {"missing_lines", missing_lines},
        {"extra_lines", extra_lines},
        {"control_characters_quoted", control_characters_quoted},
        {"bad_expected_files", bad_expected_files},
        {"unhandled_reported", unhandled_reported},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
