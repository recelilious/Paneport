// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: the harness's child process, with its output captured, its exit read, and a timeout
// that ends its whole process group.

#include <signal.h>
#include <stdbool.h>
#include <string.h>

#include "child.h"
#include "pp_check.h"

extern char **environ;

static pp_child_result_t run_shell(const char *script, const char *directory,
                                   unsigned timeout_seconds, size_t limit) {
    char *arguments[] = {"/bin/sh", "-c", (char *)script, NULL};
    pp_child_result_t result;
    PP_CHECK(pp_child_run(arguments, environ, directory, timeout_seconds, limit, &result));
    PP_CHECK(result.started);
    return result;
}

static bool equals(const char *data, size_t length, const char *expected) {
    if (data == NULL || length != strlen(expected)) {
        return false;
    }
    return memcmp(data, expected, length) == 0;
}

static void output_and_exit_code(void) {
    pp_child_result_t result =
        run_shell("printf 'one\\ntwo\\n'; printf 'err\\n' >&2; exit 3", NULL, 30U, 4096U);
    PP_CHECK(equals(result.output, result.output_length, "one\ntwo\n"));
    PP_CHECK(equals(result.errors, result.errors_length, "err\n"));
    PP_CHECK(result.exited && !result.timed_out);
    PP_CHECK_EQUAL(result.exit_code, 3);
    pp_child_result_free(&result);
}

static void input_is_empty(void) {
    pp_child_result_t result = run_shell("cat; echo done", NULL, 30U, 4096U);
    PP_CHECK(equals(result.output, result.output_length, "done\n"));
    pp_child_result_free(&result);
}

static void directory_is_used(void) {
    pp_child_result_t result = run_shell("pwd -P", "/", 30U, 4096U);
    PP_CHECK(equals(result.output, result.output_length, "/\n"));
    pp_child_result_free(&result);
}

static void timeout_ends_the_group(void) {
    pp_child_result_t result = run_shell("sleep 60 & sleep 60", NULL, 1U, 4096U);
    PP_CHECK(result.timed_out);
    PP_CHECK(!result.exited);
    PP_CHECK(result.seconds < 10.0);
    pp_child_result_free(&result);
}

static void signal_reported(void) {
    pp_child_result_t result = run_shell("kill -TERM $$", NULL, 30U, 4096U);
    PP_CHECK(!result.exited && !result.timed_out);
    PP_CHECK_EQUAL(result.signal, SIGTERM);
    pp_child_result_free(&result);
}

static void output_cut_at_the_limit(void) {
    pp_child_result_t result = run_shell(
        "i=0; while [ $i -lt 2000 ]; do echo 0123456789; i=$((i+1)); done", NULL, 60U, 1000U);
    PP_CHECK(result.truncated);
    PP_CHECK_EQUAL(result.output_length, 1000);
    PP_CHECK_EQUAL(result.exit_code, 0);
    pp_child_result_free(&result);
}

// A process that the child leaves behind with the same pipes does not hold the harness for
// longer than the drain time, and ends with the case.
static void leftover_does_not_hold(void) {
    pp_child_result_t result = run_shell("(sleep 30; echo late) & echo early", NULL, 60U, 4096U);
    PP_CHECK(equals(result.output, result.output_length, "early\n"));
    PP_CHECK(result.exited);
    PP_CHECK(result.seconds < 10.0);
    pp_child_result_free(&result);
}

static void missing_program(void) {
    char *arguments[] = {"/nonexistent/program", NULL};
    pp_child_result_t result;
    bool started = pp_child_run(arguments, environ, NULL, 5U, 4096U, &result);
    // posix_spawn reports a program that does not exist either as its own failure or as the
    // child's exit with 127, depending on the C library.
    PP_CHECK(!started || (result.exited && result.exit_code == 127));
    if (!started) {
        PP_CHECK(strstr(result.message, "cannot start") != NULL);
    }
    pp_child_result_free(&result);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"output_and_exit_code", output_and_exit_code},
        {"input_is_empty", input_is_empty},
        {"directory_is_used", directory_is_used},
        {"timeout_ends_the_group", timeout_ends_the_group},
        {"signal_reported", signal_reported},
        {"output_cut_at_the_limit", output_cut_at_the_limit},
        {"leftover_does_not_hold", leftover_does_not_hold},
        {"missing_program", missing_program},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
