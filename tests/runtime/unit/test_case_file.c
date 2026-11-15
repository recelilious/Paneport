// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: the reader of the harness's case files.

#include <stdbool.h>
#include <string.h>

#include "case_file.h"
#include "pp_check.h"

static bool parse(const char *text, pp_case_t *result, char *error, size_t error_size) {
    return pp_case_parse(text, strlen(text), result, error, error_size);
}

// Checks that a text fails to parse with a message that holds part.
static void refused(const char *text, const char *part) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(!parse(text, &result, error, sizeof error));
    if (strstr(error, part) == NULL) {
        (void)fprintf(stderr, "the message \"%s\" does not hold \"%s\"\n", error, part);
        PP_CHECK(false);
    }
    PP_CHECK(result.program == NULL && result.expected == NULL && result.argument_count == 0U);
}

static void valid_case(void) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(parse("{\n  \"program\": \"console.exe\",\n  \"arguments\": [\"3\", \"a b\"],\n"
                   "  \"timeout_seconds\": 120,\n  \"exit_code\": 3,\n"
                   "  \"expected\": \"console.txt\"\n}\n",
                   &result, error, sizeof error));
    PP_CHECK_TEXT(result.program, "console.exe");
    PP_CHECK_EQUAL(result.argument_count, 2);
    PP_CHECK_TEXT(result.arguments[0], "3");
    PP_CHECK_TEXT(result.arguments[1], "a b");
    PP_CHECK_EQUAL(result.timeout_seconds, 120);
    PP_CHECK_EQUAL(result.exit_code, 3);
    PP_CHECK_TEXT(result.expected, "console.txt");
    pp_case_free(&result);
    PP_CHECK(result.program == NULL);
}

static void arguments_are_optional(void) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(parse("{\"expected\": \"seh.txt\", \"exit_code\": 0, \"timeout_seconds\": 1, "
                   "\"program\": \"seh.exe\", \"arguments\": []}",
                   &result, error, sizeof error));
    PP_CHECK_EQUAL(result.argument_count, 0);
    pp_case_free(&result);
    PP_CHECK(parse("{\"expected\": \"seh.txt\", \"exit_code\": 0, \"timeout_seconds\": 3600, "
                   "\"program\": \"seh.exe\"}",
                   &result, error, sizeof error));
    PP_CHECK_EQUAL(result.timeout_seconds, 3600);
    pp_case_free(&result);
}

static void missing_or_repeated_member(void) {
    refused("{\"program\": \"a.exe\", \"timeout_seconds\": 5, \"exit_code\": 0}", "needs");
    refused("{}", "needs");
    refused("{\"program\": \"a.exe\", \"program\": \"b.exe\", \"timeout_seconds\": 5, "
            "\"exit_code\": 0, \"expected\": \"a.txt\"}",
            "twice");
}

static void unknown_member(void) {
    refused("{\"programm\": \"a.exe\", \"timeout_seconds\": 5, \"exit_code\": 0, "
            "\"expected\": \"a.txt\"}",
            "unknown member");
}

static void wrong_type_or_range(void) {
    refused("{\"program\": \"a.exe\", \"timeout_seconds\": 5, \"exit_code\": \"0\", "
            "\"expected\": \"a.txt\"}",
            "integer");
    refused("{\"program\": 7, \"timeout_seconds\": 5, \"exit_code\": 0, \"expected\": \"a.txt\"}",
            "string");
    refused("{\"program\": \"a.exe\", \"timeout_seconds\": 0, \"exit_code\": 0, "
            "\"expected\": \"a.txt\"}",
            "from 1 to 3600");
    refused("{\"program\": \"a.exe\", \"timeout_seconds\": 5, \"exit_code\": 256, "
            "\"expected\": \"a.txt\"}",
            "from 0 to 255");
    refused("{\"program\": \"a.exe\", \"arguments\": [1], \"timeout_seconds\": 5, "
            "\"exit_code\": 0, \"expected\": \"a.txt\"}",
            "string");
}

static void file_names_only(void) {
    refused("{\"program\": \"../a.exe\", \"timeout_seconds\": 5, \"exit_code\": 0, "
            "\"expected\": \"a.txt\"}",
            "file name");
    refused("{\"program\": \"a\", \"timeout_seconds\": 5, \"exit_code\": 0, "
            "\"expected\": \"a.txt\"}",
            "file name");
    refused("{\"program\": \"a.exe\", \"timeout_seconds\": 5, \"exit_code\": 0, "
            "\"expected\": \"sub/a.txt\"}",
            "file name");
}

static void malformed(void) {
    refused("[]", "object");
    refused("{\"program\": \"a.exe\",}", "string");
    refused("{\"program\": \"a.exe", "does not end");
    refused("{\"program\": \"a.exe\", \"timeout_seconds\": 5, \"exit_code\": 0, "
            "\"expected\": \"a.txt\"} x",
            "follows");
    refused("{\"program\": \"\\u0041.exe\"}", "code point");
    refused("{\"program\": \"\xc3\xa4.exe\"}", "ASCII");
    refused("{\"program\" \"a.exe\"}", "expected :");
    refused("{\"timeout_seconds\": 0123}", "integer");
}

static void error_names_the_line(void) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(!parse("{\n\"program\": \"a.exe\",\n\"color\": 1\n}", &result, error, sizeof error));
    PP_CHECK(strncmp(error, "line 3:", 7U) == 0);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"valid_case", valid_case},
        {"arguments_are_optional", arguments_are_optional},
        {"missing_or_repeated_member", missing_or_repeated_member},
        {"unknown_member", unknown_member},
        {"wrong_type_or_range", wrong_type_or_range},
        {"file_names_only", file_names_only},
        {"malformed", malformed},
        {"error_names_the_line", error_names_the_line},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
