// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.3.9: the fork and exec trap. Armed, posix_spawn is refused and reported with its caller,
// this test's own image; disarmed, it runs and nothing is reported. The other calls are checked
// where a call that the trap missed could not harm the test: an exec of a program that does not
// exist, a fork whose child would exit at once, and signals that the test handles itself. The
// trap and this test are built without the sanitizers, whose runtimes interpose some of the same
// calls (tests/runtime/CMakeLists.txt).

#include <errno.h>
#include <pthread.h>
#include <signal.h>
#include <spawn.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>

#include "pp_check.h"
#include "trap.h"

extern char **environ;

// This test's image, as the trap names a caller in it.
#define PP_TEST_IMAGE "test_trap"

// Checks that the report with this number starts with "<call> from test_trap", and prints it.
static void check_report(uint32_t index, const char *call) {
    char report[PP_TRAP_REPORT_SIZE];
    PP_CHECK(pp_trap_report(index, report, sizeof report));
    char expected[128];
    (void)snprintf(expected, sizeof expected, "%s from %s", call, PP_TEST_IMAGE);
    if (strncmp(report, expected, strlen(expected)) != 0) {
        (void)fprintf(stderr, "the report \"%s\" does not start with \"%s\"\n", report, expected);
        PP_CHECK(false);
    }
    (void)printf("report %u: %s\n", (unsigned)index, report);
}

// Runs action with standard error going into a pipe, and returns what it wrote there.
static void capture_errors(void (*action)(void *), void *context, char *buffer, size_t size) {
    int ends[2];
    PP_CHECK_EQUAL(pipe(ends), 0);
    const int saved = dup(STDERR_FILENO);
    PP_CHECK(saved >= 0);
    PP_CHECK(dup2(ends[1], STDERR_FILENO) == STDERR_FILENO);
    action(context);
    PP_CHECK(dup2(saved, STDERR_FILENO) == STDERR_FILENO);
    (void)close(saved);
    (void)close(ends[1]);
    size_t used = 0U;
    while (used + 1U < size) {
        const ssize_t count = read(ends[0], buffer + used, size - 1U - used);
        if (count <= 0) {
            break;
        }
        used += (size_t)count;
    }
    buffer[used] = '\0';
    (void)close(ends[0]);
}

typedef struct spawn_t {
    int status;
    pid_t child;
} spawn_t;

static void spawn_shell(void *argument) {
    spawn_t *spawn = argument;
    char *arguments[] = {"/bin/sh", "-c", "exit 7", NULL};
    spawn->child = 0;
    spawn->status = posix_spawn(&spawn->child, "/bin/sh", NULL, NULL, arguments, environ);
}

// Armed, posix_spawn is refused with EPERM and starts nothing, and its report names this image,
// both in the report that the trap keeps and in the line that it wrote at once.
static void armed_spawn_is_refused(void) {
    pp_trap_arm();
    spawn_t spawn = {0, 0};
    char errors[1024];
    capture_errors(spawn_shell, &spawn, errors, sizeof errors);
    PP_CHECK_EQUAL(spawn.status, EPERM);
    PP_CHECK_EQUAL(spawn.child, 0);
    PP_CHECK_EQUAL(pp_trap_count(), 1);
    check_report(0U, "posix_spawn");
    char report[PP_TRAP_REPORT_SIZE];
    PP_CHECK(!pp_trap_report(1U, report, sizeof report));
    PP_CHECK(strstr(errors, "paneport trap: refused posix_spawn from " PP_TEST_IMAGE) != NULL);
}

// Disarmed, before the trap was ever armed and after it was armed and disarmed again,
// posix_spawn runs the program, and nothing is counted, kept or written.
static void disarmed_spawn_runs(void) {
    for (int round = 0; round < 2; round++) {
        if (round == 1) {
            pp_trap_arm();
            pp_trap_disarm();
        }
        spawn_t spawn = {0, 0};
        char errors[1024];
        capture_errors(spawn_shell, &spawn, errors, sizeof errors);
        PP_CHECK_EQUAL(spawn.status, 0);
        int status = 0;
        PP_CHECK(waitpid(spawn.child, &status, 0) == spawn.child);
        PP_CHECK(WIFEXITED(status));
        PP_CHECK_EQUAL(WEXITSTATUS(status), 7);
        PP_CHECK_TEXT(errors, "");
    }
    PP_CHECK_EQUAL(pp_trap_count(), 0);
    char report[PP_TRAP_REPORT_SIZE];
    PP_CHECK(!pp_trap_report(0U, report, sizeof report));
}

// Checks that a call of the exec family was refused: it returned -1 with errno EPERM, and it is
// the trap's next report.
static void check_refused(int result, const char *call, uint32_t *count) {
    const int reason = errno;
    PP_CHECK_EQUAL(result, -1);
    PP_CHECK_EQUAL(reason, EPERM);
    (*count)++;
    PP_CHECK_EQUAL(pp_trap_count(), *count);
    check_report(*count - 1U, call);
}

// Armed, every exec and posix_spawnp is refused with EPERM. An exec that the trap missed would
// fail with ENOENT instead, since the program does not exist.
static void armed_exec_is_refused(void) {
    static const char k_missing[] = "/nonexistent/paneport-missing";
    static const char k_missing_name[] = "paneport-missing-program";
    char name[] = "missing";
    char *arguments[] = {name, NULL};
    char *environment[] = {NULL};
    uint32_t count = 0U;
    pp_trap_arm();
    errno = 0;
    check_refused(execv(k_missing, arguments), "execv", &count);
    errno = 0;
    check_refused(execve(k_missing, arguments, environment), "execve", &count);
    errno = 0;
    check_refused(execvp(k_missing_name, arguments), "execvp", &count);
    errno = 0;
    check_refused(execl(k_missing, name, (char *)NULL), "execl", &count);
    errno = 0;
    check_refused(execle(k_missing, name, (char *)NULL, environment), "execle", &count);
    errno = 0;
    check_refused(execlp(k_missing_name, name, (char *)NULL), "execlp", &count);
#ifdef __APPLE__
    errno = 0;
    check_refused(execvP(k_missing_name, "/nonexistent", arguments), "execvP", &count);
#endif
    pid_t child = 0;
    PP_CHECK_EQUAL(posix_spawnp(&child, k_missing_name, NULL, NULL, arguments, environ), EPERM);
    PP_CHECK_EQUAL(child, 0);
    PP_CHECK_EQUAL(pp_trap_count(), count + 1U);
    check_report(count, "posix_spawnp");
}

// Runs one of the l forms of exec in a child of fork, which exits with 127 when the exec fails,
// and returns the child's exit status.
static int exit_status_of(int (*run)(void)) {
    const pid_t child = fork();
    PP_CHECK(child >= 0);
    if (child == 0) {
        (void)run();
        _exit(127);
    }
    int status = 0;
    PP_CHECK(waitpid(child, &status, 0) == child);
    PP_CHECK(WIFEXITED(status));
    return WEXITSTATUS(status);
}

static int run_execl(void) { return execl("/bin/sh", "sh", "-c", "exit 5", (char *)NULL); }

static int run_execle(void) {
    char variable[] = "PP_TRAP_CODE=6";
    char *environment[] = {variable, NULL};
    return execle("/bin/sh", "sh", "-c", "exit $PP_TRAP_CODE", (char *)NULL, environment);
}

static int run_execlp(void) { return execlp("sh", "sh", "-c", "exit 4", (char *)NULL); }

// Disarmed, the l forms of exec hand their arguments on, and execle its environment, which the
// programs' exit codes show.
static void disarmed_exec_forwards(void) {
    PP_CHECK_EQUAL(exit_status_of(run_execl), 5);
    PP_CHECK_EQUAL(exit_status_of(run_execle), 6);
    PP_CHECK_EQUAL(exit_status_of(run_execlp), 4);
    PP_CHECK_EQUAL(pp_trap_count(), 0);
}

// Armed, fork is refused with EPERM. A fork that the trap missed has a child that exits at once,
// and the test fails.
static void armed_fork_is_refused(void) {
    pp_trap_arm();
    errno = 0;
    const pid_t child = fork();
    const int reason = errno;
    if (child == 0) {
        _exit(0);
    }
    if (child > 0) {
        (void)waitpid(child, NULL, 0);
    }
    PP_CHECK_EQUAL(child, -1);
    PP_CHECK_EQUAL(reason, EPERM);
    PP_CHECK_EQUAL(pp_trap_count(), 1);
    check_report(0U, "fork");
}

// The signals that the test received: process-wide singletons of the test, which its handler
// counts in.
static atomic_int s_user1;
static atomic_int s_user2;
static atomic_int s_quit;

static void count_signal(int signal_number) {
    if (signal_number == SIGUSR1) {
        (void)atomic_fetch_add(&s_user1, 1);
    } else if (signal_number == SIGUSR2) {
        (void)atomic_fetch_add(&s_user2, 1);
    } else if (signal_number == SIGQUIT) {
        (void)atomic_fetch_add(&s_quit, 1);
    }
}

static void handle(int signal_number) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = count_signal;
    (void)sigemptyset(&action.sa_mask);
    PP_CHECK_EQUAL(sigaction(signal_number, &action, NULL), 0);
}

// Armed, SIGUSR1 and SIGQUIT are not sent, through kill, raise or pthread_kill, while another
// signal is; disarmed, SIGUSR1 is sent again. The test handles the signals itself, so that a send
// that the trap missed counts as received instead of ending the test.
static void signals_refused_while_armed(void) {
    handle(SIGUSR1);
    handle(SIGUSR2);
    handle(SIGQUIT);
    pp_trap_arm();
    errno = 0;
    PP_CHECK_EQUAL(kill(getpid(), SIGUSR1), -1);
    PP_CHECK_EQUAL(errno, EPERM);
    errno = 0;
    PP_CHECK(raise(SIGQUIT) != 0);
    PP_CHECK_EQUAL(errno, EPERM);
    PP_CHECK_EQUAL(pthread_kill(pthread_self(), SIGUSR1), EPERM);
    PP_CHECK_EQUAL(raise(SIGUSR2), 0);
    PP_CHECK_EQUAL(atomic_load(&s_user1), 0);
    PP_CHECK_EQUAL(atomic_load(&s_quit), 0);
    PP_CHECK_EQUAL(atomic_load(&s_user2), 1);
    PP_CHECK_EQUAL(pp_trap_count(), 3);
    check_report(0U, "kill(SIGUSR1)");
    check_report(1U, "raise(SIGQUIT)");
    check_report(2U, "pthread_kill(SIGUSR1)");
    pp_trap_disarm();
    PP_CHECK_EQUAL(raise(SIGUSR1), 0);
    PP_CHECK_EQUAL(atomic_load(&s_user1), 1);
    PP_CHECK_EQUAL(pp_trap_count(), 3);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"armed_spawn_is_refused", armed_spawn_is_refused},
        {"disarmed_spawn_runs", disarmed_spawn_runs},
        {"armed_exec_is_refused", armed_exec_is_refused},
        {"disarmed_exec_forwards", disarmed_exec_forwards},
        {"armed_fork_is_refused", armed_fork_is_refused},
        {"signals_refused_while_armed", signals_refused_while_armed},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
