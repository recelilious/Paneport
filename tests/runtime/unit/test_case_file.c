// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: the reader of the harness's case files.

#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
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
    PP_CHECK_EQUAL(result.variable_count, 0);
    PP_CHECK_EQUAL(result.runs, 1);
    PP_CHECK_EQUAL(result.kind, PP_CASE_PROGRAM);
    pp_case_free(&result);
    PP_CHECK(result.program == NULL);
}

// A session case names its kind and runs no program, so it has no program, arguments, exit code
// or expected output; it may set variables and repeat.
static void session_cases(void) {
    static const struct {
        const char *name;
        pp_case_kind_t kind;
    } k_kinds[] = {
        {"start_stop", PP_CASE_START_STOP},
        {"second_start", PP_CASE_SECOND_START},
        {"jit_not_ready", PP_CASE_JIT_NOT_READY},
        {"dispatcher_check", PP_CASE_DISPATCHER_CHECK},
    };
    for (size_t index = 0; index < sizeof k_kinds / sizeof k_kinds[0]; index++) {
        char text[256];
        (void)snprintf(text, sizeof text, "{\"session\": \"%s\", \"timeout_seconds\": 60}",
                       k_kinds[index].name);
        pp_case_t result;
        char error[256] = "";
        PP_CHECK(parse(text, &result, error, sizeof error));
        PP_CHECK_EQUAL(result.kind, k_kinds[index].kind);
        PP_CHECK_TEXT(pp_case_kind_name(result.kind), k_kinds[index].name);
        PP_CHECK(result.program == NULL && result.expected == NULL);
        PP_CHECK_EQUAL(result.runs, 1);
        PP_CHECK_EQUAL(result.timeout_seconds, 60);
        pp_case_free(&result);
    }
    PP_CHECK_TEXT(pp_case_kind_name(PP_CASE_PROGRAM), "program");
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(parse("{\"session\": \"start_stop\", \"runs\": 20, \"environment\": "
                   "{\"WINEDEBUG\": \"-all\"}, \"timeout_seconds\": 120}",
                   &result, error, sizeof error));
    PP_CHECK_EQUAL(result.kind, PP_CASE_START_STOP);
    PP_CHECK_EQUAL(result.runs, 20);
    PP_CHECK_EQUAL(result.variable_count, 1);
    PP_CHECK_TEXT(result.variables[0], "WINEDEBUG=-all");
    pp_case_free(&result);
    PP_CHECK_EQUAL(result.kind, PP_CASE_PROGRAM);
}

static void session_cases_refused(void) {
    refused("{\"session\": \"restart\", \"timeout_seconds\": 5}", "\"session\" must be");
    refused("{\"session\": \"program\", \"timeout_seconds\": 5}", "\"session\" must be");
    refused("{\"session\": 1, \"timeout_seconds\": 5}", "string");
    refused("{\"session\": \"start_stop\", \"session\": \"start_stop\", \"timeout_seconds\": 5}",
            "twice");
    refused("{\"session\": \"start_stop\"}", "needs timeout_seconds");
    refused("{\"session\": \"start_stop\", \"program\": \"a.exe\", \"timeout_seconds\": 5}",
            "runs no program");
    refused("{\"session\": \"start_stop\", \"arguments\": [], \"timeout_seconds\": 5}",
            "runs no program");
    refused("{\"session\": \"start_stop\", \"exit_code\": 0, \"timeout_seconds\": 5}",
            "runs no program");
    refused("{\"session\": \"start_stop\", \"expected\": \"a.txt\", \"timeout_seconds\": 5}",
            "runs no program");
    refused("{\"timeout_seconds\": 5, \"exit_code\": 0, \"expected\": \"a.txt\"}", "needs");
}

static void environment_and_runs(void) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(parse("{\"program\": \"seh.exe\", \"environment\": {\"FEX_MULTIBLOCK\": \"0\", "
                   "\"A_1\": \"x y\", \"EMPTY\": \"\"}, \"runs\": 5, \"timeout_seconds\": 9, "
                   "\"exit_code\": 0, \"expected\": \"seh.txt\"}",
                   &result, error, sizeof error));
    PP_CHECK_EQUAL(result.variable_count, 3);
    PP_CHECK_TEXT(result.variables[0], "FEX_MULTIBLOCK=0");
    PP_CHECK_TEXT(result.variables[1], "A_1=x y");
    PP_CHECK_TEXT(result.variables[2], "EMPTY=");
    PP_CHECK_EQUAL(result.runs, 5);
    pp_case_free(&result);
    PP_CHECK(result.variable_count == 0U && result.variables[0] == NULL);
    PP_CHECK(parse("{\"program\": \"seh.exe\", \"environment\": {}, \"runs\": 100, "
                   "\"timeout_seconds\": 9, \"exit_code\": 0, \"expected\": \"seh.txt\"}",
                   &result, error, sizeof error));
    PP_CHECK_EQUAL(result.variable_count, 0);
    PP_CHECK_EQUAL(result.runs, 100);
    pp_case_free(&result);
}

// Wraps one environment member and one runs member in an otherwise valid case.
static void refused_with(const char *environment, const char *runs, const char *part) {
    char text[512];
    (void)snprintf(text, sizeof text,
                   "{\"program\": \"a.exe\", \"environment\": %s, \"runs\": %s, "
                   "\"timeout_seconds\": 5, \"exit_code\": 0, \"expected\": \"a.txt\"}",
                   environment, runs);
    refused(text, part);
}

static void environment_and_runs_refused(void) {
    refused_with("{\"fex\": \"0\"}", "1", "capital letters");
    refused_with("{\"1A\": \"0\"}", "1", "capital letters");
    refused_with("{\"A-B\": \"0\"}", "1", "capital letters");
    refused_with("{\"\": \"0\"}", "1", "capital letters");
    refused_with("{\"WINEPREFIX\": \"/tmp\"}", "1", "WINEPREFIX");
    refused_with("{\"A\": \"0\", \"A\": \"1\"}", "1", "twice");
    refused_with("{\"A\": 0}", "1", "string");
    refused_with("[\"A=0\"]", "1", "object");
    refused_with("{\"A\" \"0\"}", "1", "expected :");
    refused_with("{\"A\": \"0\" \"B\": \"1\"}", "1", "expected , or }");
    refused_with("{}", "0", "from 1 to 100");
    refused_with("{}", "101", "from 1 to 100");
    refused_with("{}", "\"2\"", "integer");
}

static void environment_of_the_runs(void) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(parse("{\"program\": \"seh.exe\", \"environment\": {\"FEX_MULTIBLOCK\": \"0\"}, "
                   "\"timeout_seconds\": 9, \"exit_code\": 0, \"expected\": \"seh.txt\"}",
                   &result, error, sizeof error));
    char path[] = "PATH=/bin";
    // A name that starts the case's own, or that the case's starts, is another variable.
    char shorter[] = "FEX=1";
    char inherited[] = "FEX_MULTIBLOCK=1";
    char longer[] = "FEX_MULTIBLOCKS=1";
    char prefix[] = "WINEPREFIX=/old";
    char *const base[] = {path, shorter, inherited, longer, prefix, NULL};
    char *owned = NULL;
    char **environment = pp_case_environment(&result, base, "/new", &owned);
    PP_CHECK(environment != NULL);
    if (environment != NULL) {
        PP_CHECK_TEXT(environment[0], "PATH=/bin");
        PP_CHECK_TEXT(environment[1], "FEX=1");
        PP_CHECK_TEXT(environment[2], "FEX_MULTIBLOCKS=1");
        PP_CHECK_TEXT(environment[3], "FEX_MULTIBLOCK=0");
        PP_CHECK_TEXT(environment[4], "WINEPREFIX=/new");
        PP_CHECK(environment[5] == NULL);
        PP_CHECK(environment[4] == owned);
    }
    free((void *)environment);
    free(owned);
    pp_case_free(&result);
}

// The child of single mode inherits neither WINEPREFIX nor the case's variables, which reach the
// session through its configuration instead.
static void inherited_environment(void) {
    pp_case_t result;
    char error[256] = "";
    PP_CHECK(parse("{\"program\": \"seh.exe\", \"environment\": {\"FEX_MULTIBLOCK\": \"0\"}, "
                   "\"timeout_seconds\": 9, \"exit_code\": 0, \"expected\": \"seh.txt\"}",
                   &result, error, sizeof error));
    char path[] = "PATH=/bin";
    char shorter[] = "FEX=1";
    char inherited[] = "FEX_MULTIBLOCK=1";
    char longer[] = "FEX_MULTIBLOCKS=1";
    char prefix[] = "WINEPREFIX=/old";
    char debug[] = "WINEDEBUG=err+all";
    char *const base[] = {path, shorter, inherited, longer, prefix, debug, NULL};
    char **environment = pp_case_inherited_environment(&result, base);
    PP_CHECK(environment != NULL);
    if (environment != NULL) {
        PP_CHECK_TEXT(environment[0], "PATH=/bin");
        PP_CHECK_TEXT(environment[1], "FEX=1");
        PP_CHECK_TEXT(environment[2], "FEX_MULTIBLOCKS=1");
        PP_CHECK_TEXT(environment[3], "WINEDEBUG=err+all");
        PP_CHECK(environment[4] == NULL);
    }
    free((void *)environment);
    pp_case_free(&result);
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
        {"environment_and_runs", environment_and_runs},
        {"environment_and_runs_refused", environment_and_runs_refused},
        {"environment_of_the_runs", environment_of_the_runs},
        {"inherited_environment", inherited_environment},
        {"session_cases", session_cases},
        {"session_cases_refused", session_cases_refused},
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
