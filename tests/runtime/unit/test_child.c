// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.2.5: the harness's child process, with its output captured, its exit read, and a timeout
// that ends its whole process group.

#include <errno.h>
#include <signal.h>
#include <stdbool.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

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

// Waits up to five seconds for the process to be gone; a process that was killed may stay a
// zombie until its new parent reaps it.
static bool process_ends(pid_t process) {
    for (unsigned waited = 0; waited < 500U; waited++) {
        if (kill(process, 0) != 0 && errno == ESRCH) {
            return true;
        }
        const struct timespec pause = {0, 10000000L};
        (void)nanosleep(&pause, NULL);
    }
    return false;
}

// The process ID that a script printed on its first line.
static pid_t first_line_pid(const pp_child_result_t *result) {
    if (result->output == NULL) {
        return 0;
    }
    long value = strtol(result->output, NULL, 10);
    return value > 1L ? (pid_t)value : 0;
}

// The harness's own input stays open here, through a pipe whose writing end it holds, so that a
// child that inherited it instead of /dev/null would wait in cat until the timeout.
static void input_is_empty(void) {
    int held[2];
    PP_CHECK(pipe(held) == 0);
    int saved = dup(STDIN_FILENO);
    PP_CHECK(saved >= 0);
    PP_CHECK(dup2(held[0], STDIN_FILENO) == STDIN_FILENO);
    pp_child_result_t result = run_shell("cat; echo done", NULL, 5U, 4096U);
    PP_CHECK(dup2(saved, STDIN_FILENO) == STDIN_FILENO);
    (void)close(saved);
    (void)close(held[0]);
    (void)close(held[1]);
    PP_CHECK(!result.timed_out);
    PP_CHECK(equals(result.output, result.output_length, "done\n"));
    pp_child_result_free(&result);
}

static void directory_is_used(void) {
    pp_child_result_t result = run_shell("pwd -P", "/", 30U, 4096U);
    PP_CHECK(equals(result.output, result.output_length, "/\n"));
    pp_child_result_free(&result);
}

static void timeout_ends_the_group(void) {
    pp_child_result_t result = run_shell("sleep 60 & echo $!; sleep 60", NULL, 1U, 4096U);
    PP_CHECK(result.timed_out);
    PP_CHECK(!result.exited);
    PP_CHECK(result.seconds < 10.0);
    pid_t background = first_line_pid(&result);
    PP_CHECK(background != 0);
    PP_CHECK(process_ends(background));
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
    PP_CHECK(result.output_truncated);
    PP_CHECK(!result.errors_truncated);
    PP_CHECK_EQUAL(result.output_length, 1000);
    PP_CHECK(memcmp(result.output, "0123456789\n", 11U) == 0);
    PP_CHECK_EQUAL(result.exit_code, 0);
    pp_child_result_free(&result);
}

// The error stream keeps its end, where Wine reports how a program ended, and a long one does
// not cut the output.
static void errors_keep_their_tail(void) {
    pp_child_result_t result = run_shell("i=0; while [ $i -lt 2000 ]; do echo 0123456789 >&2; "
                                         "i=$((i+1)); done; echo last >&2; echo out",
                                         NULL, 60U, 1000U);
    PP_CHECK(result.errors_truncated);
    PP_CHECK(!result.output_truncated);
    PP_CHECK_EQUAL(result.errors_length, 1000);
    PP_CHECK(memcmp(result.errors + result.errors_length - 16U, "0123456789\nlast\n", 16U) == 0);
    PP_CHECK(equals(result.output, result.output_length, "out\n"));
    PP_CHECK_EQUAL(result.exit_code, 0);
    pp_child_result_free(&result);
}

// A cut on the error stream's last read, here its only one, leaves the length at the limit, so
// only the flag that the read itself sets reports the cut.
static void errors_cut_on_the_last_read(void) {
    pp_child_result_t result =
        run_shell("printf 'abcdefghijklmnopqrstuvwxyz0123' >&2", NULL, 30U, 10U);
    PP_CHECK(result.errors_truncated);
    PP_CHECK_EQUAL(result.errors_length, 10);
    PP_CHECK(equals(result.errors, result.errors_length, "uvwxyz0123"));
    PP_CHECK_EQUAL(result.exit_code, 0);
    pp_child_result_free(&result);
}

// A process that the child leaves behind with the same pipes does not hold the harness for
// longer than the drain time, and ends with the case.
static void leftover_does_not_hold(void) {
    pp_child_result_t result = run_shell("(sleep 30; echo late) & echo $!", NULL, 60U, 4096U);
    PP_CHECK(result.output_length > 0U && result.output[result.output_length - 1U] == '\n');
    PP_CHECK(result.exited);
    PP_CHECK(result.seconds < 10.0);
    pid_t leftover = first_line_pid(&result);
    PP_CHECK(leftover != 0);
    PP_CHECK(process_ends(leftover));
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
        {"errors_keep_their_tail", errors_keep_their_tail},
        {"errors_cut_on_the_last_read", errors_cut_on_the_last_read},
        {"leftover_does_not_hold", leftover_does_not_hold},
        {"missing_program", missing_program},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
