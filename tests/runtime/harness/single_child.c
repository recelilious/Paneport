// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "single_child.h"

#include <errno.h>
#include <limits.h>
#include <pthread.h>
#include <signal.h>
#include <stdarg.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "case_file.h"
#include "paneport/host.h"
#include "paneport/session.h"
#include "paneport/testing.h"
#include "run_result.h"
#include "trap.h"
#include "wine_session.h"

// How many events a run keeps; a run has three at most.
#define PP_CHILD_EVENTS 16U

// The longest line that the child writes for a log line, with its prefix and its line feed.
#define PP_CHILD_LINE_SIZE 1024U

// How long a wait for an event sleeps between its checks of the deadline and of the parent.
#define PP_CHILD_WAIT_STEP_SECONDS 1

// What the host's event thread hands to the child's main thread: the events so far.
typedef struct child_t {
    pthread_mutex_t mutex;
    pthread_cond_t changed;
    pp_event_t events[PP_CHILD_EVENTS];
    uint32_t event_count;
    bool events_lost;
} child_t;

// What one run works with: its paths, its case, the session's block of variables, its deadline on
// the monotonic clock, the harness's parent, the events and the result that it builds.
typedef struct run_t {
    const pp_single_child_t *paths;
    const pp_case_t *test;
    const char *environment;
    struct timespec deadline;
    pid_t parent;
    child_t *child;
    pp_run_result_t *result;
} run_t;

static void write_all(int descriptor, const char *data, size_t length) {
    while (length > 0U) {
        const ssize_t written = write(descriptor, data, length);
        if (written < 0 && errno == EINTR) {
            continue;
        }
        if (written <= 0) {
            return;
        }
        data += written;
        length -= (size_t)written;
    }
}

// Writes prefix, text and a line feed with one write, so that lines from different threads stay
// whole; a text that does not fit is cut.
static void write_line(int descriptor, const char *prefix, const char *text) {
    char line[PP_CHILD_LINE_SIZE];
    size_t used = 0U;
    for (const char *part = prefix; *part != '\0' && used + 1U < sizeof line; part++) {
        line[used] = *part;
        used++;
    }
    for (const char *part = text; *part != '\0' && used + 1U < sizeof line; part++) {
        line[used] = *part;
        used++;
    }
    line[used] = '\n';
    used++;
    write_all(descriptor, line, used);
}

static void fail(run_t *run, const char *format, ...) __attribute__((format(printf, 2, 3)));

// Adds a failure to the run's result, and writes it to standard error at once, where it lands
// among the runtime's lines.
static void fail(run_t *run, const char *format, ...) {
    char text[PP_RESULT_TEXT_SIZE];
    va_list list;
    va_start(list, format);
    (void)vsnprintf(text, sizeof text, format, list);
    va_end(list);
    pp_run_result_add_failure(run->result, "%s", text);
    write_line(STDERR_FILENO, "paneport_harness: ", text);
}

static const char *status_name(pp_status_t status) {
    switch (status) {
    case PP_OK:
        return "PP_OK";
    case PP_E_INVALID_ARGUMENT:
        return "PP_E_INVALID_ARGUMENT";
    case PP_E_WRONG_THREAD:
        return "PP_E_WRONG_THREAD";
    case PP_E_NOT_INITIALIZED:
        return "PP_E_NOT_INITIALIZED";
    case PP_E_ALREADY_INITIALIZED:
        return "PP_E_ALREADY_INITIALIZED";
    case PP_E_OUT_OF_MEMORY:
        return "PP_E_OUT_OF_MEMORY";
    case PP_E_IO:
        return "PP_E_IO";
    case PP_E_UNSUPPORTED:
        return "PP_E_UNSUPPORTED";
    case PP_E_BUSY:
        return "PP_E_BUSY";
    case PP_E_SYSTEM:
        return "PP_E_SYSTEM";
    case PP_E_JIT_NOT_READY:
        return "PP_E_JIT_NOT_READY";
    case PP_E_SESSION_EXISTS:
        return "PP_E_SESSION_EXISTS";
    case PP_E_NO_SESSION:
        return "PP_E_NO_SESSION";
    default:
        return "an unknown status";
    }
}

// Checks the status that a call into the host returned against the one that the run expects,
// and fails the run otherwise, with the host's message. The caller calls it right after the
// call, before the thread's next call into the host replaces the message.
static bool expect(run_t *run, const char *call, pp_status_t status, pp_status_t expected) {
    if (status == expected) {
        return true;
    }
    const char *message = status == PP_OK ? "" : pp_last_error();
    fail(run, "%s returned %s, not %s%s%s", call, status_name(status), status_name(expected),
         message[0] == '\0' ? "" : ": ", message);
    return false;
}

// --- Events and lines from the host event thread ---------------------------------------------

static void on_event(const pp_event_t *event, void *user_data) {
    child_t *child = user_data;
    (void)pthread_mutex_lock(&child->mutex);
    if (child->event_count < PP_CHILD_EVENTS) {
        child->events[child->event_count] = *event;
        child->event_count++;
    } else {
        child->events_lost = true;
    }
    (void)pthread_cond_broadcast(&child->changed);
    (void)pthread_mutex_unlock(&child->mutex);
}

// The program's output goes to standard output, which the parent compares; everything else goes
// to standard error, the run's error output, where the host's own lines are marked.
static void on_log(const pp_log_line_t *line, void *user_data) {
    (void)user_data;
    const char *text = line->text == NULL ? "" : line->text;
    const size_t source_end = offsetof(pp_log_line_t, source) + sizeof line->source;
    const uint32_t source = line->struct_size >= source_end ? line->source : PP_LOG_SOURCE_HOST;
    if (source == PP_LOG_SOURCE_GUEST_OUTPUT) {
        write_line(STDOUT_FILENO, "", text);
    } else if (source == PP_LOG_SOURCE_HOST) {
        write_line(STDERR_FILENO, "paneport host: ", text);
    } else {
        write_line(STDERR_FILENO, "", text);
    }
}

static bool past(const struct timespec *deadline) {
    struct timespec now;
    (void)clock_gettime(CLOCK_MONOTONIC, &now);
    if (now.tv_sec != deadline->tv_sec) {
        return now.tv_sec > deadline->tv_sec;
    }
    return now.tv_nsec >= deadline->tv_nsec;
}

// Looks for an event of the kind among those so far. The caller holds the mutex.
static bool find_event(const child_t *child, uint32_t kind, pp_event_t *found) {
    for (uint32_t index = 0U; index < child->event_count; index++) {
        if (child->events[index].kind == kind) {
            *found = child->events[index];
            return true;
        }
    }
    return false;
}

// Waits for an event of the kind until the run's deadline, or until the harness's parent has
// gone, so that an orphaned child ends by itself; fails the run when the event did not arrive.
static bool wait_for_event(run_t *run, uint32_t kind, const char *name, pp_event_t *found) {
    child_t *child = run->child;
    bool arrived = false;
    bool orphaned = false;
    (void)pthread_mutex_lock(&child->mutex);
    for (;;) {
        arrived = find_event(child, kind, found);
        orphaned = getppid() != run->parent;
        if (arrived || orphaned || past(&run->deadline)) {
            break;
        }
        struct timespec until;
        (void)clock_gettime(CLOCK_REALTIME, &until);
        until.tv_sec += PP_CHILD_WAIT_STEP_SECONDS;
        (void)pthread_cond_timedwait(&child->changed, &child->mutex, &until);
    }
    (void)pthread_mutex_unlock(&child->mutex);
    if (arrived) {
        return true;
    }
    if (orphaned) {
        fail(run, "the harness's parent went away before the %s event arrived", name);
    } else {
        fail(run, "the %s event did not arrive within %u s", name, run->test->timeout_seconds);
    }
    return false;
}

// Waits for the session's end, which must come with code.
static void expect_session_end(run_t *run, int64_t code) {
    pp_event_t event;
    if (wait_for_event(run, PP_EVENT_SESSION_ENDED, "session-ended", &event) &&
        event.code != code) {
        fail(run, "the session ended with %lld, not %lld", (long long)event.code, (long long)code);
    }
}

// --- The kinds of run ------------------------------------------------------------------------

// Joins strings into a block of NUL-terminated strings that ends with an empty one, as
// pp_session_start and pp_process_launch take them. Returns NULL for no strings, with *failed
// false, and NULL with *failed true when memory ran out.
static char *make_block(char *const strings[], size_t count, bool *failed) {
    *failed = false;
    if (count == 0U) {
        return NULL;
    }
    size_t size = 1U;
    for (size_t index = 0; index < count; index++) {
        size += strlen(strings[index]) + 1U;
    }
    char *block = malloc(size);
    if (block == NULL) {
        *failed = true;
        return NULL;
    }
    size_t used = 0U;
    for (size_t index = 0; index < count; index++) {
        const size_t length = strlen(strings[index]) + 1U;
        memcpy(block + used, strings[index], length);
        used += length;
    }
    block[used] = '\0';
    return block;
}

static pp_status_t start_session(const run_t *run) {
    const pp_session_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_session_config_t),
        .architecture = PP_ARCHITECTURE_X86_64,
        .prefix_folder = run->paths->prefix,
        .runtime_folder = run->paths->runtime,
        .environment = run->environment,
    };
    return pp_session_start(&config);
}

// A program case: the session starts, the program runs in it with its output captured, and the
// session ends after the program's end, as it does after its one process.
static void run_program(run_t *run) {
    const pp_case_t *test = run->test;
    for (size_t index = 0; index < test->argument_count; index++) {
        if (test->arguments[index][0] == '\0') {
            fail(run, "argument %zu is empty, which the block of pp_process_launch cannot hold",
                 index + 1U);
            return;
        }
    }
    bool failed = false;
    char *arguments = make_block(test->arguments, test->argument_count, &failed);
    if (failed) {
        fail(run, "out of memory");
        return;
    }
    (void)pp_test_jit_set_ready(1U);
    pp_status_t status = start_session(run);
    if (!expect(run, "pp_session_start", status, PP_OK)) {
        free(arguments);
        return;
    }
    char program[PATH_MAX];
    (void)snprintf(program, sizeof program, "C:\\paneport-tests\\%s", test->program);
    const pp_process_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_process_config_t),
        .capture_output = 1U,
        .program_folder = NULL,
        .program = program,
        .arguments = arguments,
    };
    uint64_t process = 0U;
    status = pp_process_launch(&config, &process);
    (void)expect(run, "pp_process_launch", status, PP_OK);
    free(arguments);
    // A process that got its handle reports its end, also after a launch that failed. Its start
    // may come before the program fails to load its DLLs, and its exit code then says why, which
    // the comparison of exit codes names; the order of the two events says nothing about the run.
    if (process != 0U) {
        pp_event_t exited;
        if (wait_for_event(run, PP_EVENT_PROCESS_EXITED, "process-exited", &exited)) {
            run->result->has_exit_code = true;
            run->result->exit_code = (uint32_t)exited.code;
        }
        expect_session_end(run, PP_OK);
    }
    status = pp_session_stop();
    (void)expect(run, "pp_session_stop", status, PP_OK);
}

// start_stop: a session with no program starts, stops and ends with 0 (S4.3.2).
static void run_start_stop(run_t *run) {
    (void)pp_test_jit_set_ready(1U);
    pp_status_t status = start_session(run);
    if (!expect(run, "pp_session_start", status, PP_OK)) {
        return;
    }
    status = pp_session_stop();
    (void)expect(run, "pp_session_stop", status, PP_OK);
    expect_session_end(run, PP_OK);
}

// second_start: a launch runs one session, while it runs and after it stopped (C-0004 section 1).
static void run_second_start(run_t *run) {
    (void)pp_test_jit_set_ready(1U);
    pp_status_t status = start_session(run);
    if (!expect(run, "pp_session_start", status, PP_OK)) {
        return;
    }
    status = start_session(run);
    (void)expect(run, "a second pp_session_start while the session runs", status,
                 PP_E_SESSION_EXISTS);
    status = pp_session_stop();
    (void)expect(run, "pp_session_stop", status, PP_OK);
    expect_session_end(run, PP_OK);
    status = start_session(run);
    (void)expect(run, "a second pp_session_start after the session stopped", status,
                 PP_E_SESSION_EXISTS);
}

// jit_not_ready: the session needs the JIT state ready (C-0005 section 2).
static void run_jit_not_ready(run_t *run) {
    (void)pp_test_jit_set_ready(0U);
    pp_status_t status = start_session(run);
    (void)expect(run, "pp_session_start with the JIT test hook off", status, PP_E_JIT_NOT_READY);
    status = pp_session_stop();
    (void)expect(run, "pp_session_stop after the refused start", status, PP_E_NO_SESSION);
}

// The dispatcher's action for SIGSEGV while the dispatcher check's case replaces it: a
// process-wide singleton of the child (C-0010 section 3), saved before the replacement is
// installed and read only by the replacement, which hands every fault on to it, so that a fault
// in the short time until the session fails still reaches the dispatcher.
static struct sigaction s_dispatcher_segv;

static void forward_segv(int signal_number, siginfo_t *info, void *context) {
    s_dispatcher_segv.sa_sigaction(signal_number, info, context);
}

// The started hook of the host's test builds (wine_session.h): it replaces the dispatcher's
// SIGSEGV handler after the fork started the session and before the host checks the dispatcher.
static void plant_segv_handler(void *argument) {
    bool *planted = argument;
    if (sigaction(SIGSEGV, NULL, &s_dispatcher_segv) != 0 ||
        (s_dispatcher_segv.sa_flags & SA_SIGINFO) == 0) {
        return;
    }
    struct sigaction replacement = s_dispatcher_segv;
    replacement.sa_sigaction = forward_segv;
    *planted = sigaction(SIGSEGV, &replacement, NULL) == 0;
}

// dispatcher_check: a SIGSEGV handler planted after the session's start fails the start with
// PP_E_SYSTEM and a message that names SIGSEGV, and the failed session counts as run (S4.3.4).
// The dispatcher's handler comes back afterwards, for the child's own check.
static void run_dispatcher_check(run_t *run) {
    (void)pp_test_jit_set_ready(1U);
    bool planted = false;
    pp_session_set_started_hook(plant_segv_handler, &planted);
    const pp_status_t status = start_session(run);
    char message[PP_RESULT_TEXT_SIZE];
    (void)snprintf(message, sizeof message, "%s", pp_last_error());
    pp_session_set_started_hook(NULL, NULL);
    if (planted) {
        (void)sigaction(SIGSEGV, &s_dispatcher_segv, NULL);
    } else {
        fail(run, "the started hook could not replace the dispatcher's SIGSEGV handler");
    }
    if (status == PP_OK) {
        fail(run, "pp_session_start returned PP_OK although a SIGSEGV handler was planted after "
                  "the start");
        const pp_status_t stopped = pp_session_stop();
        (void)expect(run, "pp_session_stop", stopped, PP_OK);
        expect_session_end(run, PP_OK);
    } else if (status != PP_E_SYSTEM) {
        fail(run, "pp_session_start returned %s, not PP_E_SYSTEM: %s", status_name(status),
             message);
    } else {
        if (strstr(message, "SIGSEGV") == NULL) {
            fail(run, "pp_session_start failed, but its message does not name SIGSEGV: %s",
                 message);
        }
        expect_session_end(run, PP_E_SYSTEM);
    }
    const pp_status_t again = start_session(run);
    (void)expect(run, "pp_session_start after the failed session", again, PP_E_SESSION_EXISTS);
}

static void *idle(void *argument) { return argument; }

// After the session and the host's shutdown, the harness keeps running and checks itself: the
// dispatcher, which pp_shutdown leaves in place, still holds its six signals, and the process can
// still start and join a thread (S4.3.6).
static void check_self(run_t *run) {
    const pp_status_t status = pp_test_signals_check_installed(NULL);
    if (status != PP_OK) {
        fail(run, "after the session, the check of the signal dispatcher failed: %s",
             pp_last_error());
    }
    pthread_t thread;
    if (pthread_create(&thread, NULL, idle, NULL) != 0 || pthread_join(thread, NULL) != 0) {
        fail(run, "after the session, the harness could not start and join a thread");
    }
}

static void run_kind(run_t *run) {
    switch (run->test->kind) {
    case PP_CASE_START_STOP:
        run_start_stop(run);
        break;
    case PP_CASE_SECOND_START:
        run_second_start(run);
        break;
    case PP_CASE_JIT_NOT_READY:
        run_jit_not_ready(run);
        break;
    case PP_CASE_DISPATCHER_CHECK:
        run_dispatcher_check(run);
        break;
    case PP_CASE_PROGRAM:
    default:
        run_program(run);
        break;
    }
}

// Runs the host and the session for a case that was read, stops the host, then makes the child's
// own check.
static void run_case(run_t *run) {
    const pp_init_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_init_config_t),
        .crash_record_folder = run->paths->logs,
    };
    pp_status_t status = pp_init(&config);
    if (!expect(run, "pp_init", status, PP_OK)) {
        return;
    }
    status = pp_set_event_callback(on_event, run->child);
    bool callbacks = expect(run, "pp_set_event_callback", status, PP_OK);
    if (callbacks) {
        status = pp_set_log_callback(on_log, NULL);
        callbacks = expect(run, "pp_set_log_callback", status, PP_OK);
    }
    if (callbacks) {
        // From here on nothing in the process may fork, exec or spawn, or send SIGUSR1 or
        // SIGQUIT.
        pp_trap_arm();
        run_kind(run);
    }
    status = pp_shutdown();
    (void)expect(run, "pp_shutdown", status, PP_OK);
    if (run->child->events_lost) {
        fail(run, "more than %u events arrived", PP_CHILD_EVENTS);
    }
    check_self(run);
}

int pp_single_child_run(const pp_single_child_t *paths) {
    pp_run_result_t result;
    pp_run_result_init(&result);
    child_t child;
    memset(&child, 0, sizeof child);
    run_t run = {
        .paths = paths,
        .test = NULL,
        .environment = NULL,
        .parent = getppid(),
        .child = &child,
        .result = &result,
    };
    char result_path[PATH_MAX];
    const int length =
        snprintf(result_path, sizeof result_path, "%s/%s", paths->logs, PP_RESULT_FILE);
    if (length <= 0 || (size_t)length >= sizeof result_path) {
        write_line(STDERR_FILENO, "paneport_harness: ", "the path of the run's result is too long");
        return 1;
    }
    pp_case_t test;
    char error[512];
    if (pp_case_read(paths->case_path, &test, error, sizeof error)) {
        run.test = &test;
        bool failed = false;
        char *environment = make_block(test.variables, test.variable_count, &failed);
        run.environment = environment;
        (void)clock_gettime(CLOCK_MONOTONIC, &run.deadline);
        run.deadline.tv_sec += (time_t)test.timeout_seconds;
        if (failed) {
            fail(&run, "out of memory");
        } else if (pthread_mutex_init(&child.mutex, NULL) != 0 ||
                   pthread_cond_init(&child.changed, NULL) != 0) {
            fail(&run, "cannot make the lock of the run's events");
        } else {
            run_case(&run);
            (void)pthread_cond_destroy(&child.changed);
            (void)pthread_mutex_destroy(&child.mutex);
        }
        free(environment);
        pp_case_free(&test);
    } else {
        fail(&run, "cannot read the case: %s", error);
    }
    result.trap_count = pp_trap_count();
    for (uint32_t index = 0U; index < result.trap_count && index < PP_TRAP_REPORTS; index++) {
        char report[PP_TRAP_REPORT_SIZE];
        if (pp_trap_report(index, report, sizeof report)) {
            pp_run_result_add_trap(&result, report);
        }
    }
    if (!pp_run_result_write(&result, result_path)) {
        write_line(STDERR_FILENO, "paneport_harness: ", "cannot write the run's result");
        return 1;
    }
    return result.failure_count == 0U && result.trap_count == 0U ? 0 : 1;
}
