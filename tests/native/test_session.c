// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.3.8: the session and process groups of paneport/session.h, run against the fake of the
// Wine fork's Unix library in fake_wine_fork.c, which the host loads from a runtime folder as
// it loads the fork. The fake receives the bridge, the events arrive on the host event thread in
// their order, a program's output arrives line by line before its exit event however it is
// chunked, and the session's rules hold: the JIT state, one session per launch, the check of the
// signal dispatcher, one process per session, termination and the stop.

#include <dlfcn.h>
#include <errno.h>
#include <limits.h>
#include <pthread.h>
#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#include "event_thread.h"
#include "fake_wine_fork.h"
#include "guest_output.h"
#include "paneport/host.h"
#include "paneport/jit.h"
#include "paneport/session.h"
#include "paneport/testing.h"
#include "paneport/wine_bridge.h"
#include "pp_check.h"
#include "test_support.h"
#include "wine_session.h"

#ifndef FAKE_FORK_PATH
#error "CMake passes the path of the fake fork's module as FAKE_FORK_PATH."
#endif

enum { MAX_EVENTS = 16, MAX_LINES = 64, LINE_SIZE = 512 };

// The kind of the marker event with which settle() waits for the events posted before it.
#define MARKER_EVENT 99U

// What the callbacks saw. lines_before[i] counts the runtime and guest lines that had arrived
// when event i arrived; the marker events are only counted. The lines of the mode flood are
// counted by source, and slow_lines makes the log callback take 20 us a line. sigsegv_errors
// counts the host's error lines that name SIGSEGV.
static struct {
    pthread_mutex_t mutex;
    pthread_cond_t changed;
    pp_event_t events[MAX_EVENTS];
    uint32_t lines_before[MAX_EVENTS];
    uint32_t event_count;
    uint32_t markers;
    bool event_off_thread;
    char lines[MAX_LINES][LINE_SIZE];
    uint32_t sources[MAX_LINES];
    uint64_t processes[MAX_LINES];
    uint32_t line_count;
    bool line_off_thread;
    bool slow_lines;
    uint32_t flood_output;
    uint32_t flood_runtime;
    uint32_t flood_output_at_exit;
    uint32_t sigsegv_errors;
} s_seen = {
    .mutex = PTHREAD_MUTEX_INITIALIZER,
    .changed = PTHREAD_COND_INITIALIZER,
};

static void record_event(const pp_event_t *event, void *user_data) {
    (void)user_data;
    (void)pthread_mutex_lock(&s_seen.mutex);
    if (!pp_event_thread_is_current()) {
        s_seen.event_off_thread = true;
    }
    if (event->kind == MARKER_EVENT) {
        s_seen.markers++;
    } else if (s_seen.event_count < (uint32_t)MAX_EVENTS) {
        s_seen.events[s_seen.event_count] = *event;
        s_seen.lines_before[s_seen.event_count] = s_seen.line_count;
        s_seen.event_count++;
    }
    if (event->kind == PP_EVENT_PROCESS_EXITED) {
        s_seen.flood_output_at_exit = s_seen.flood_output;
    }
    (void)pthread_cond_broadcast(&s_seen.changed);
    (void)pthread_mutex_unlock(&s_seen.mutex);
}

// Keeps the lines of the runtime and of the program; the host's own lines vary.
static void record_line(const pp_log_line_t *line, void *user_data) {
    (void)user_data;
    (void)pthread_mutex_lock(&s_seen.mutex);
    if (!pp_event_thread_is_current()) {
        s_seen.line_off_thread = true;
    }
    if (line->source == PP_LOG_SOURCE_HOST && line->level == PP_LOG_ERROR &&
        strstr(line->text, "SIGSEGV") != NULL) {
        s_seen.sigsegv_errors++;
    }
    if (strcmp(line->text, FAKE_FORK_FLOOD_TEXT) == 0) {
        if (line->source == PP_LOG_SOURCE_GUEST_OUTPUT) {
            s_seen.flood_output++;
        } else if (line->source == PP_LOG_SOURCE_RUNTIME) {
            s_seen.flood_runtime++;
        }
    } else if (line->source != PP_LOG_SOURCE_HOST && s_seen.line_count < (uint32_t)MAX_LINES) {
        (void)snprintf(s_seen.lines[s_seen.line_count], LINE_SIZE, "%s", line->text);
        s_seen.sources[s_seen.line_count] = line->source;
        s_seen.processes[s_seen.line_count] = line->process;
        s_seen.line_count++;
    }
    const bool slow = s_seen.slow_lines;
    (void)pthread_cond_broadcast(&s_seen.changed);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    if (slow) {
        const struct timespec pause = {.tv_sec = 0, .tv_nsec = 20000L};
        (void)nanosleep(&pause, NULL);
    }
}

// Waits up to a minute for an event of the kind, and returns it.
static pp_event_t wait_for_event(uint32_t kind) {
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += 60;
    pp_event_t found = {.struct_size = 0U};
    (void)pthread_mutex_lock(&s_seen.mutex);
    for (;;) {
        for (uint32_t index = 0U; index < s_seen.event_count && found.struct_size == 0U; index++) {
            if (s_seen.events[index].kind == kind) {
                found = s_seen.events[index];
            }
        }
        if (found.struct_size != 0U ||
            pthread_cond_timedwait(&s_seen.changed, &s_seen.mutex, &until) == ETIMEDOUT) {
            break;
        }
    }
    (void)pthread_mutex_unlock(&s_seen.mutex);
    if (found.struct_size == 0U) {
        pp_check_failed(__FILE__, __LINE__, "the event arrived within a minute");
    }
    return found;
}

// Waits until every event and line posted before the call has arrived: a marker event posted
// after them arrives after them (native-host.md section 5).
static void settle(void) {
    (void)pthread_mutex_lock(&s_seen.mutex);
    const uint32_t expected = s_seen.markers + 1U;
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(pp_test_post_event(MARKER_EVENT, 0), PP_OK);
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += 60;
    (void)pthread_mutex_lock(&s_seen.mutex);
    while (s_seen.markers < expected) {
        if (pthread_cond_timedwait(&s_seen.changed, &s_seen.mutex, &until) == ETIMEDOUT) {
            break;
        }
    }
    const uint32_t markers = s_seen.markers;
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(markers, expected);
}

// The folders that a session needs: a runtime folder whose ntdll.so is the fake, and a prefix.
typedef struct fixture_t {
    char runtime[PATH_MAX];
    char prefix[PATH_MAX];
    char library[PATH_MAX];
} fixture_t;

static void make_fixture(fixture_t *fixture) {
    pp_make_folder(fixture->runtime, sizeof fixture->runtime);
    pp_make_folder(fixture->prefix, sizeof fixture->prefix);
    static const char *const k_folders[] = {"lib", "lib/wine", "lib/wine/aarch64-unix"};
    for (size_t index = 0U; index < sizeof k_folders / sizeof k_folders[0]; index++) {
        char path[PATH_MAX];
        PP_CHECK(snprintf(path, sizeof path, "%s/%s", fixture->runtime, k_folders[index]) > 0);
        PP_CHECK_EQUAL(mkdir(path, 0755), 0);
    }
    PP_CHECK(snprintf(fixture->library, sizeof fixture->library, "%s/%s", fixture->runtime,
                      PP_WINE_LIBRARY_PATH) > 0);
    PP_CHECK_EQUAL(symlink(FAKE_FORK_PATH, fixture->library), 0);
}

static pp_session_config_t session_config(const fixture_t *fixture, const char *environment) {
    const pp_session_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_session_config_t),
        .architecture = PP_ARCHITECTURE_X86_64,
        .prefix_folder = fixture->prefix,
        .runtime_folder = fixture->runtime,
        .environment = environment,
    };
    return config;
}

static pp_process_config_t process_config(uint8_t capture_output) {
    const pp_process_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_process_config_t),
        .capture_output = capture_output,
        .program_folder = NULL,
        .program = "C:\\tests\\fake.exe",
        .arguments = "one\0two\0",
    };
    return config;
}

static void start_host(void) {
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(record_event, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_set_log_callback(record_line, NULL), PP_OK);
}

static void start_session(const fixture_t *fixture, const char *environment) {
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    const pp_session_config_t config = session_config(fixture, environment);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_OK);
}

static uint64_t launch(uint8_t capture_output, pp_status_t expected) {
    const pp_process_config_t config = process_config(capture_output);
    uint64_t process = 0U;
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), expected);
    return process;
}

static void check_state(uint64_t process, uint32_t state, uint32_t exit_code) {
    pp_process_state_t read = {.struct_size = (uint32_t)sizeof(pp_process_state_t)};
    PP_CHECK_EQUAL(pp_process_get_state(process, &read), PP_OK);
    PP_CHECK_EQUAL(read.state, state);
    PP_CHECK_EQUAL(read.exit_code, exit_code);
}

static void stop_host(void) {
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// The session needs a running host and the JIT state ready (C-0005 section 2).
static void start_needs_host_and_jit(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    const pp_session_config_t config = session_config(&fixture, NULL);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_NOT_INITIALIZED);
    start_host();
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_JIT_NOT_READY);
    PP_CHECK(strstr(pp_last_error(), "JIT") != NULL);
    PP_CHECK_EQUAL(pp_test_jit_set_ready(2U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_OK);
    stop_host();
}

// Calls a function of the fake that the host loaded, which takes nothing and returns an int.
static int call_fake(const fixture_t *fixture, const char *name) {
    void *module = dlopen(fixture->library, RTLD_NOW | RTLD_NOLOAD);
    PP_CHECK(module != NULL);
    void *symbol = dlsym(module, name);
    PP_CHECK(symbol != NULL);
    int (*function)(void) = NULL;
    memcpy((void *)&function, (const void *)&symbol, sizeof function);
    const int result = function();
    PP_CHECK_EQUAL(dlclose(module), 0);
    return result;
}

// The fork receives a complete bridge of this version (C-0005 section 3).
static void fork_receives_the_bridge(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, NULL);
    PP_CHECK_EQUAL(call_fake(&fixture, "fake_fork_bridge_ok"), 1);
    // The host gave the server's thread the stack of a main thread, 8 MiB (paneport/wine_bridge.h);
    // the default stack of a thread on Darwin is 512 KiB.
    PP_CHECK(call_fake(&fixture, "fake_fork_server_stack_kib") >= 8 * 1024);
    stop_host();
}

// The events of a process and its session arrive on the event thread, in order.
static void events_arrive_on_event_thread(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, NULL);
    const uint64_t process = launch(1U, PP_OK);
    PP_CHECK(process != 0U);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);

    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 3U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_STARTED);
    PP_CHECK_EQUAL(s_seen.events[0].handle, process);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[1].handle, process);
    PP_CHECK_EQUAL(s_seen.events[1].code, FAKE_FORK_EXIT_CODE);
    PP_CHECK_EQUAL(s_seen.events[2].kind, PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(s_seen.events[2].handle, 0U);
    PP_CHECK_EQUAL(s_seen.events[2].code, PP_OK);
    PP_CHECK(!s_seen.event_off_thread);
    PP_CHECK(!s_seen.line_off_thread);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_EXIT_CODE);
    // The session ended by itself, so its stop reports no second end.
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 3U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// The program's lines arrive whole, marked with their source and process, before its exit
// event, though the fake writes them in chunks of 1 to 13 bytes.
static void output_lines_arrive_before_the_exit(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, NULL);
    const uint64_t process = launch(1U, PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);

    static const char *const k_output[] = {
        "first line", "second line", NULL, NULL, FAKE_FORK_BYTES_LINE, "last line",
    };
    (void)pthread_mutex_lock(&s_seen.mutex);
    uint32_t output = 0U;
    uint32_t errors = 0U;
    uint32_t runtime = 0U;
    for (uint32_t index = 0U; index < s_seen.line_count; index++) {
        const char *text = s_seen.lines[index];
        if (s_seen.sources[index] == PP_LOG_SOURCE_RUNTIME) {
            PP_CHECK_EQUAL(s_seen.processes[index], 0U);
            PP_CHECK(runtime < 2U);
            PP_CHECK_TEXT(text,
                          runtime == 0U ? "fake runtime: process started" : "second runtime line");
            runtime++;
            continue;
        }
        PP_CHECK_EQUAL(s_seen.processes[index], process);
        if (s_seen.sources[index] == PP_LOG_SOURCE_GUEST_ERRORS) {
            PP_CHECK_TEXT(text, "an error line");
            errors++;
            continue;
        }
        PP_CHECK_EQUAL(s_seen.sources[index], PP_LOG_SOURCE_GUEST_OUTPUT);
        PP_CHECK(output < 6U);
        if (k_output[output] != NULL) {
            PP_CHECK_TEXT(text, k_output[output]);
        } else {
            // The line of 600 bytes arrives as a full piece and the rest.
            const size_t expected = output == 2U ? PP_GUEST_LINE_MAX : 600U - PP_GUEST_LINE_MAX;
            PP_CHECK_EQUAL(strlen(text), expected);
            PP_CHECK(strspn(text, "x") == expected);
        }
        output++;
    }
    PP_CHECK_EQUAL(output, 6U);
    PP_CHECK_EQUAL(errors, 1U);
    PP_CHECK_EQUAL(runtime, 2U);
    // Every line came before the exit event.
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.lines_before[1], s_seen.line_count);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

// A session that ran is the launch's only one, also after it was stopped (C-0004 section 1).
static void second_session_refused(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, NULL);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    const pp_session_config_t config = session_config(&fixture, NULL);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SESSION_EXISTS);
    PP_CHECK(strstr(pp_last_error(), "relaunch") != NULL);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A runtime that installs a SIGSEGV handler of its own fails the session in test builds, which
// counts as run (C-0005 section 4).
static void replaced_handler_fails_session(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    const pp_session_config_t config = session_config(&fixture, "FAKE_FORK_MODE=sigsegv\0");
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SYSTEM);
    PP_CHECK(strstr(pp_last_error(), "SIGSEGV") != NULL);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_SYSTEM);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SESSION_EXISTS);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// In single mode Wine sets up its signal handling when a process starts, inside the launch, so
// test builds check the dispatcher again then: a process whose start installs a SIGSEGV handler
// of its own fails the launch, is terminated with exit code 1, and its session ends with
// PP_E_SYSTEM (C-0005 section 4). The launch, which waited for the start, names the signal; the
// report of the start leaves the process to it and logs no error of its own.
static void launch_check_fails_the_session(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=launch_sigsegv\0");
    const uint64_t process = launch(1U, PP_E_SYSTEM);
    PP_CHECK(process != 0U);
    PP_CHECK(strstr(pp_last_error(), "SIGSEGV") != NULL);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 1);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_SYSTEM);
    check_state(process, PP_PROCESS_EXITED, 1U);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.sigsegv_errors, 0U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

// The check runs when the runtime reports the process's start, before the process can end, so it
// fails the session also for a program that ends before the launch looks at it: the launch fails
// and names the signal, the program keeps its own exit code, since it ended before the launch
// could terminate it, and the session's end carries PP_E_SYSTEM.
static void launch_check_fails_a_quick_program(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=quick_sigsegv\0");
    const uint64_t process = launch(0U, PP_E_SYSTEM);
    PP_CHECK(process != 0U);
    PP_CHECK(strstr(pp_last_error(), "SIGSEGV") != NULL);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_SYSTEM);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 3U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_STARTED);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[1].code, FAKE_FORK_EXIT_CODE);
    PP_CHECK_EQUAL(s_seen.events[2].kind, PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_EXIT_CODE);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.sigsegv_errors, 0U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

static void plant_sigbus_handler(void *argument) {
    (void)argument;
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_IGN;
    (void)sigemptyset(&action.sa_mask);
    PP_CHECK_EQUAL(sigaction(SIGBUS, &action, NULL), 0);
}

// The hook of the test builds runs between the runtime's start and the check, which then names
// the replaced handler: the harness plants its handler there.
static void started_hook_runs_before_check(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    pp_session_set_started_hook(plant_sigbus_handler, NULL);
    const pp_session_config_t config = session_config(&fixture, NULL);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SYSTEM);
    PP_CHECK(strstr(pp_last_error(), "SIGBUS") != NULL);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_SYSTEM);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A runtime that refuses the session fails it, its log line arrives, and the session counts as
// run.
static void runtime_refusal_fails_session(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    const pp_session_config_t config = session_config(&fixture, "FAKE_FORK_MODE=refuse\0");
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SYSTEM);
    PP_CHECK(strstr(pp_last_error(), "refused") != NULL);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_SYSTEM);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.line_count, 1U);
    PP_CHECK_EQUAL(s_seen.sources[0], PP_LOG_SOURCE_RUNTIME);
    PP_CHECK_TEXT(s_seen.lines[0], "fake runtime: refusing the session");
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SESSION_EXISTS);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A launch needs a running session, a valid configuration, and the session's process slot.
static void launch_needs_a_session(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    (void)launch(1U, PP_E_NO_SESSION);
    start_session(&fixture, "FAKE_FORK_MODE=wait\0");

    uint64_t process = 0U;
    PP_CHECK_EQUAL(pp_process_launch(NULL, &process), PP_E_INVALID_ARGUMENT);
    pp_process_config_t config = process_config(1U);
    PP_CHECK_EQUAL(pp_process_launch(&config, NULL), PP_E_INVALID_ARGUMENT);
    config.struct_size = 4U;
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), PP_E_INVALID_ARGUMENT);
    config = process_config(2U);
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), PP_E_INVALID_ARGUMENT);
    config = process_config(1U);
    config.program = "";
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), PP_E_INVALID_ARGUMENT);
    config = process_config(1U);
    config.program_folder = "relative/folder";
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), PP_E_INVALID_ARGUMENT);
    config.program_folder = "/nonexistent-paneport-folder";
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), PP_E_INVALID_ARGUMENT);
    // An arguments block whose empty string does not come within 32 KiB.
    char arguments[(32U * 1024U) + 1U];
    memset(arguments, 'a', sizeof arguments - 1U);
    arguments[sizeof arguments - 1U] = '\0';
    config = process_config(1U);
    config.arguments = arguments;
    PP_CHECK_EQUAL(pp_process_launch(&config, &process), PP_E_INVALID_ARGUMENT);

    process = launch(1U, PP_OK);
    PP_CHECK(process != 0U);
    check_state(process, PP_PROCESS_RUNNING, 0U);
    (void)launch(1U, PP_E_UNSUPPORTED);
    PP_CHECK_EQUAL(pp_process_terminate(process, 0U), PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    stop_host();
}

// Termination gives the process the exit code it names, and the session ends after it.
static void terminate_sets_the_exit_code(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=wait\0");
    const uint64_t process = launch(0U, PP_OK);
    PP_CHECK_EQUAL(pp_process_terminate(process, 7U), PP_OK);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 7);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    check_state(process, PP_PROCESS_EXITED, 7U);
    // A process that ended is not terminated again: the fake's terminate would refuse it.
    PP_CHECK_EQUAL(pp_process_terminate(process, 8U), PP_OK);
    PP_CHECK_EQUAL(pp_process_terminate(process + 1U, 8U), PP_E_INVALID_ARGUMENT);
    pp_process_state_t state = {.struct_size = (uint32_t)sizeof(pp_process_state_t)};
    PP_CHECK_EQUAL(pp_process_get_state(process + 1U, &state), PP_E_INVALID_ARGUMENT);
    state.struct_size = 4U;
    PP_CHECK_EQUAL(pp_process_get_state(process, &state), PP_E_INVALID_ARGUMENT);
    // A caller that knows 8 bytes of the struct gets no exit code written past them.
    state = (pp_process_state_t){.struct_size = 8U, .state = 0U, .exit_code = 0xFFFFFFFFU};
    PP_CHECK_EQUAL(pp_process_get_state(process, &state), PP_OK);
    PP_CHECK_EQUAL(state.state, PP_PROCESS_EXITED);
    PP_CHECK_EQUAL(state.exit_code, 0xFFFFFFFFU);
    stop_host();
}

// Until the host recorded the process's end, pp_process_get_state asks the runtime's query, whose
// answer here, an end with an exit code that the bridge never reported, differs from the host's
// record; once the bridge reported the end, the host's record answers.
static void get_state_asks_the_fork(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=query_exited\0");
    const uint64_t process = launch(0U, PP_OK);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_QUERY_EXIT_CODE);
    PP_CHECK_EQUAL(pp_process_terminate(process, 7U), PP_OK);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 7);
    check_state(process, PP_PROCESS_EXITED, 7U);
    stop_host();
}

// Stopping a session ends its running process first; the exit event comes before the session's.
static void stop_ends_a_running_process(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=wait\0");
    const uint64_t process = launch(1U, PP_OK);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 3U);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[1].code, FAKE_FORK_STOPPED_EXIT_CODE);
    PP_CHECK_EQUAL(s_seen.events[2].kind, PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_STOPPED_EXIT_CODE);
    (void)launch(1U, PP_E_NO_SESSION);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A launch on a thread of its own, with or without the capture of the output: what it returned,
// and for a launch that the launch hook holds, whether the stop has returned.
typedef struct held_launch_t {
    _Atomic(bool) in_hook;
    _Atomic(bool) stopped;
    _Atomic(int) status;
    uint8_t capture_output;
    uint64_t process;
} held_launch_t;

static void hold_launch(void *argument) {
    held_launch_t *held = argument;
    atomic_store(&held->in_hook, true);
    pp_wait_for_flag(&held->stopped);
}

static void *launch_held(void *argument) {
    held_launch_t *held = argument;
    const pp_process_config_t config = process_config(held->capture_output);
    atomic_store(&held->status, pp_process_launch(&config, &held->process));
    return NULL;
}

// A stop that overtakes a launch between the start of its output reader and its call into the
// runtime leaves the launch to end its process: the launch fails with PP_E_NO_SESSION, the
// runtime never hears of the process, the launch joins its reader, and the exit event comes
// before the session's end.
static void stop_during_a_launch(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=wait\0");
    held_launch_t held = {.capture_output = 1U, .process = 0U};
    atomic_init(&held.in_hook, false);
    atomic_init(&held.stopped, false);
    atomic_init(&held.status, 1);
    pp_session_set_launch_hook(hold_launch, &held);
    const pthread_t thread = pp_start_thread(launch_held, &held);
    pp_wait_for_flag(&held.in_hook);
    pp_session_set_stop_wait(200U);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    atomic_store(&held.stopped, true);
    pp_join_thread(thread);
    pp_session_set_launch_hook(NULL, NULL);
    PP_CHECK_EQUAL(atomic_load(&held.status), PP_E_NO_SESSION);
    PP_CHECK(held.process != 0U);
    check_state(held.process, PP_PROCESS_EXITED, 1U);
    PP_CHECK_EQUAL(call_fake(&fixture, "fake_fork_launches"), 0);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 2U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[0].code, 1);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A process that the runtime never reports as ended counts as terminated by the stop, with exit
// code 1, once the stop's wait is over.
static void stop_counts_an_unreported_end(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=silent_exit\0");
    const uint64_t process = launch(1U, PP_OK);
    pp_session_set_stop_wait(200U);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 1);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_OK);
    check_state(process, PP_PROCESS_EXITED, 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A launch that keeps copies of the pipes' write ends and fails, as the fork's branch for a
// thread that it cannot create did, leaves the reader of the output waiting for an end that never
// comes, and the exit event waits for the reader. pp_session_stop wakes the reader once its wait
// is over, and returns, and the exit event and the session's end follow in their order.
static void stop_wakes_a_reader_without_an_end(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=launch_refuse_keep\0");
    const uint64_t process = launch(1U, PP_E_SYSTEM);
    check_state(process, PP_PROCESS_EXITED, 1U);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 0U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    pp_session_set_stop_wait(200U);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 2U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[0].code, 1);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A process that ends before it finishes its start fails the launch, keeps its exit code, and
// never reports a start.
static void failed_start_is_reported(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=fail_start\0");
    const uint64_t process = launch(1U, PP_E_SYSTEM);
    PP_CHECK(process != 0U);
    PP_CHECK(strstr(pp_last_error(), "before it finished its start") != NULL);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_START_FAILURE);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 2U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[0].code, FAKE_FORK_START_FAILURE);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

// A launch that the runtime refuses fails, and its process counts as ended with exit code 1.
static void launch_refusal_is_reported(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=launch_refuse\0");
    const uint64_t process = launch(1U, PP_E_SYSTEM);
    PP_CHECK(process != 0U);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 1);
    check_state(process, PP_PROCESS_EXITED, 1U);
    stop_host();
}

// A runtime may report the end of a process whose launch it then refuses, with an exit code of
// its own, here 0, as the fork's server may for a process whose first thread could not be
// created. The host holds the exit event while the runtime's launch runs, and the refused process
// counts as ended with exit code 1 whatever the runtime reported.
static void check_reported_refusal(uint8_t capture_output) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=launch_refuse_reported\0");
    const uint64_t process = launch(capture_output, PP_E_SYSTEM);
    PP_CHECK(process != 0U);
    PP_CHECK(strstr(pp_last_error(), "refused to launch") != NULL);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 1);
    check_state(process, PP_PROCESS_EXITED, 1U);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 1U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

static void refusal_overrides_a_reported_exit(void) { check_reported_refusal(0U); }

// The same with the capture of the output, whose reader ends once the launch returned.
static void refusal_overrides_a_reported_exit_captured(void) { check_reported_refusal(1U); }

// Starts a launch, on a thread of its own, that the runtime keeps inside its launch after it
// reported the process's end with exit code 0 (the fake's mode launch_hold_reported), and waits
// until the runtime's launch holds it there.
static pthread_t start_held_launch(const fixture_t *fixture, held_launch_t *held,
                                   uint8_t capture_output) {
    held->capture_output = capture_output;
    held->process = 0U;
    atomic_init(&held->in_hook, false);
    atomic_init(&held->stopped, false);
    atomic_init(&held->status, 1);
    const pthread_t thread = pp_start_thread(launch_held, held);
    PP_CHECK_EQUAL(call_fake(fixture, "fake_fork_reached_gate"), 1);
    return thread;
}

// A launch thread that the system deschedules inside the runtime's launch, after the runtime's
// report of the process's end, holds the exit event until the launch returns. pp_session_stop
// waits for it a while and returns, and pp_shutdown refuses with PP_E_BUSY until the launch has
// returned: then the process-exited event, with exit code 1 for the refused launch, and the
// session-ended event still arrive, and pp_shutdown stops the host.
static void shutdown_refused_while_a_launch_runs(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=launch_hold_reported\0");
    held_launch_t held;
    const pthread_t thread = start_held_launch(&fixture, &held, 1U);
    pp_session_set_stop_wait(200U);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 0U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_BUSY);
    PP_CHECK(strstr(pp_last_error(), "pp_process_launch") != NULL);
    PP_CHECK_EQUAL(call_fake(&fixture, "fake_fork_open_gate"), 1);
    pp_join_thread(thread);
    PP_CHECK_EQUAL(atomic_load(&held.status), PP_E_SYSTEM);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 1);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_OK);
    check_state(held.process, PP_PROCESS_EXITED, 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// Opens the fake's gate 100 ms after it starts, while pp_session_stop waits.
static void *open_gate_later(void *argument) {
    const fixture_t *fixture = argument;
    pp_sleep_milliseconds(100);
    (void)call_fake(fixture, "fake_fork_open_gate");
    return NULL;
}

// The same launch, whose runtime's launch returns while pp_session_stop waits, here without the
// capture of the output, whose reader the stop would wait for too: the stop waits for the launch,
// so the process-exited event, with exit code 1, and the session-ended event come before the stop
// returns.
static void stop_waits_for_a_launch_in_the_runtime(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=launch_hold_reported\0");
    held_launch_t held;
    const pthread_t thread = start_held_launch(&fixture, &held, 0U);
    const pthread_t opener = pp_start_thread(open_gate_later, &fixture);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 2U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[0].code, 1);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(s_seen.events[1].code, PP_OK);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    pp_join_thread(opener);
    pp_join_thread(thread);
    PP_CHECK_EQUAL(atomic_load(&held.status), PP_E_SYSTEM);
    check_state(held.process, PP_PROCESS_EXITED, 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A program that starts and ends before the launch looks at it still started: the launch
// succeeds, and both events and the exit code arrive. This is the launch race, in which the
// process started and ended before the launch saw its start.
static void quick_program_still_started(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=quick\0");
    const uint64_t process = launch(1U, PP_OK);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, FAKE_FORK_EXIT_CODE);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_STARTED);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_EXIT_CODE);
    stop_host();
}

// A program and the runtime that write more lines than the log queue holds, faster than the log
// callback takes them, lose none, since the reader and the bridge's log wait for room, and every
// line of the program arrives before its exit event.
static void a_flood_of_lines_loses_none(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    (void)pthread_mutex_lock(&s_seen.mutex);
    s_seen.slow_lines = true;
    (void)pthread_mutex_unlock(&s_seen.mutex);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=flood\0");
    (void)launch(1U, PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.flood_output, FAKE_FORK_FLOOD_LINES);
    PP_CHECK_EQUAL(s_seen.flood_runtime, FAKE_FORK_FLOOD_LINES);
    PP_CHECK_EQUAL(s_seen.flood_output_at_exit, FAKE_FORK_FLOOD_LINES);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

// The runtime may report a process's end before the process's last line reached its pipe, as the
// fork's server does for a process that it terminates: the exit event still waits for the reader
// of the output, so the line arrives before it.
static void exit_waits_for_the_last_line(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=late_output\0");
    (void)launch(1U, PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_lock(&s_seen.mutex);
    bool found = false;
    for (uint32_t index = 0U; index < s_seen.line_count; index++) {
        if (strcmp(s_seen.lines[index], FAKE_FORK_LATE_LINE) == 0) {
            found = true;
        }
    }
    PP_CHECK(found);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.lines_before[1], s_seen.line_count);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    stop_host();
}

// The host creates at most four threads for the fork's session: the fake has it create four, and
// the start fails unless the fifth is refused with PP_WINE_E_BUSY.
static void four_threads_at_most(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=five_threads\0");
    stop_host();
}

// A session that stops gets no more threads: the fake's stop asks the bridge for one, as a fork
// might while the host stops its session, and start_thread refuses it with PP_WINE_E_SYSTEM.
static void stopping_session_gets_no_thread(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=late_thread\0");
    PP_CHECK_EQUAL(call_fake(&fixture, "fake_fork_late_thread_status"), 1);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    PP_CHECK_EQUAL(call_fake(&fixture, "fake_fork_late_thread_status"), PP_WINE_E_SYSTEM);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A program that never finishes its start fails the launch once the launch's wait for the start
// is over, 120 s, which the test builds shorten: the process is still starting, without an exit
// code, and its exit event follows once the runtime reports its end.
static void unfinished_start_fails_the_launch(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=never_started\0");
    pp_session_set_launch_wait(200U);
    const uint64_t process = launch(0U, PP_E_SYSTEM);
    PP_CHECK(process != 0U);
    PP_CHECK(strstr(pp_last_error(), "did not finish its start") != NULL);
    check_state(process, PP_PROCESS_STARTING, 0U);
    PP_CHECK_EQUAL(pp_process_terminate(process, 5U), PP_OK);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 5);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 2U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_EXITED);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, 5U);
    stop_host();
}

// A start that the runtime reports after the launch's wait for it ended still fails the session
// when it replaced a handler of the dispatcher. No launch is left to fail for it, so the report
// itself logs an error that names the signal and terminates the process with exit code 1.
static void late_start_check_ends_the_process(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=late_sigsegv\0");
    pp_session_set_launch_wait(200U);
    const uint64_t process = launch(0U, PP_E_SYSTEM);
    PP_CHECK(strstr(pp_last_error(), "did not finish its start") != NULL);
    check_state(process, PP_PROCESS_STARTING, 0U);
    PP_CHECK_EQUAL(call_fake(&fixture, "fake_fork_open_gate"), 1);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_PROCESS_EXITED).code, 1);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_SYSTEM);
    settle();
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 3U);
    PP_CHECK_EQUAL(s_seen.events[0].kind, PP_EVENT_PROCESS_STARTED);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[2].kind, PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(s_seen.sigsegv_errors, 1U);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, 1U);
    stop_host();
}

// Reads the JIT state until it is the one given, for up to a minute.
static void wait_for_jit_state(pp_jit_state_t expected) {
    pp_jit_status_t status = {.struct_size = (uint32_t)sizeof(pp_jit_status_t)};
    for (int round = 0; round < 60000; round++) {
        PP_CHECK_EQUAL(pp_jit_get_state(&status), PP_OK);
        if (status.state == expected || status.state == PP_JIT_STATE_FAILED) {
            break;
        }
        pp_sleep_milliseconds(1);
    }
    PP_CHECK_EQUAL(status.state, expected);
}

// M3.1's JIT state answers the session's precondition by itself: with the macOS backend's
// simulated broker the provider reaches ready, and a session starts without the answer of the
// test builds.
static void start_accepts_the_providers_ready_state(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    // One eighth of 8 GiB leaves room for the default pool (C-0006 section 4).
    PP_CHECK_EQUAL(pp_test_set_memory_stub(UINT64_C(8192) * 1024U * 1024U, 0U), PP_OK);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    wait_for_jit_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB), PP_OK);
    wait_for_jit_state(PP_JIT_STATE_READY);
    const pp_session_config_t config = session_config(&fixture, NULL);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_OK);
    stop_host();
}

// pp_shutdown refuses while a session runs, which it could not stop afterwards, and leaves the
// host and the session as they were; once the session stopped, the host stops.
static void shutdown_refused_while_a_session_runs(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=wait\0");
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_BUSY);
    PP_CHECK(strstr(pp_last_error(), "pp_session_stop") != NULL);
    const uint64_t process = launch(1U, PP_OK);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_BUSY);
    check_state(process, PP_PROCESS_RUNNING, 0U);
    PP_CHECK_EQUAL(pp_session_stop(), PP_OK);
    (void)wait_for_event(PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// pp_shutdown after a session that ended by itself first collects it, as pp_session_stop would.
// Here the runtime keeps a copy of the output's write end, so the reader of the output never sees
// its end, and pp_shutdown stops it once the stop's wait is over: the program's lines, the last
// one without a line feed too, its exit event and the session's end all arrive before
// pp_shutdown returns.
static void shutdown_collects_a_session_that_ended(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, "FAKE_FORK_MODE=kept_output\0");
    const uint64_t process = launch(1U, PP_OK);
    // The fake's server ends after its process, and the session ends with it.
    for (int round = 0; round < 60000 && pp_session_is_active(); round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK(!pp_session_is_active());
    pp_session_set_stop_wait(200U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    (void)pthread_mutex_lock(&s_seen.mutex);
    PP_CHECK_EQUAL(s_seen.event_count, 3U);
    PP_CHECK_EQUAL(s_seen.events[1].kind, PP_EVENT_PROCESS_EXITED);
    PP_CHECK_EQUAL(s_seen.events[1].code, FAKE_FORK_EXIT_CODE);
    PP_CHECK_EQUAL(s_seen.events[2].kind, PP_EVENT_SESSION_ENDED);
    PP_CHECK_EQUAL(s_seen.events[2].code, PP_OK);
    // Six lines of output, one of errors and two of the runtime, all before the exit event.
    PP_CHECK_EQUAL(s_seen.line_count, 9U);
    PP_CHECK_EQUAL(s_seen.lines_before[1], s_seen.line_count);
    bool last_line = false;
    for (uint32_t index = 0U; index < s_seen.line_count; index++) {
        if (strcmp(s_seen.lines[index], "last line") == 0) {
            last_line = true;
        }
    }
    PP_CHECK(last_line);
    (void)pthread_mutex_unlock(&s_seen.mutex);
    check_state(process, PP_PROCESS_EXITED, FAKE_FORK_EXIT_CODE);
}

// A start whose runtime cannot be loaded counts as a session that ran: its end is reported with
// PP_E_IO, and a start with a runtime that loads is refused afterwards.
static void load_failure_counts_as_run(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    PP_CHECK_EQUAL(unlink(fixture.library), 0);
    FILE *file = fopen(fixture.library, "w");
    PP_CHECK(file != NULL);
    PP_CHECK(fputs("not a library\n", file) >= 0);
    PP_CHECK_EQUAL(fclose(file), 0);
    start_host();
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    const pp_session_config_t config = session_config(&fixture, NULL);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_IO);
    PP_CHECK_EQUAL(wait_for_event(PP_EVENT_SESSION_ENDED).code, PP_E_IO);
    PP_CHECK_EQUAL(unlink(fixture.library), 0);
    PP_CHECK_EQUAL(symlink(FAKE_FORK_PATH, fixture.library), 0);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_SESSION_EXISTS);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// A configuration that is wrong fails with its code and leaves the launch's session unused.
static void configuration_checks(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    PP_CHECK_EQUAL(pp_session_start(NULL), PP_E_INVALID_ARGUMENT);
    pp_session_config_t config = session_config(&fixture, NULL);
    config.struct_size = 4U;
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_INVALID_ARGUMENT);
    config = session_config(&fixture, NULL);
    config.architecture = PP_ARCHITECTURE_I686;
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_UNSUPPORTED);
    config.architecture = 7U;
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_INVALID_ARGUMENT);
    config = session_config(&fixture, NULL);
    config.prefix_folder = "relative/prefix";
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_INVALID_ARGUMENT);
    config.prefix_folder = "/nonexistent-paneport-prefix";
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_INVALID_ARGUMENT);
    config = session_config(&fixture, NULL);
    config.runtime_folder = fixture.prefix;
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_INVALID_ARGUMENT);
    static const char *const k_bad_environments[] = {
        "lower=1\0",
        "=value\0",
        "NO_EQUALS\0",
        "9NAME=1\0",
    };
    for (size_t index = 0U; index < sizeof k_bad_environments / sizeof k_bad_environments[0];
         index++) {
        config = session_config(&fixture, k_bad_environments[index]);
        PP_CHECK_EQUAL(pp_session_start(&config), PP_E_INVALID_ARGUMENT);
    }
    config = session_config(&fixture, "GOOD_NAME_1=value\0OTHER=\0");
    PP_CHECK_EQUAL(pp_session_start(&config), PP_OK);
    stop_host();
}

static void *start_elsewhere(void *argument) {
    const fixture_t *fixture = argument;
    const pp_session_config_t config = session_config(fixture, NULL);
    PP_CHECK_EQUAL(pp_session_start(&config), PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(pp_session_stop(), PP_E_WRONG_THREAD);
    return NULL;
}

// Starting and stopping run on the main thread only.
static void wrong_thread(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    PP_CHECK_EQUAL(pp_test_jit_set_ready(1U), PP_OK);
    pp_join_thread(pp_start_thread(start_elsewhere, &fixture));
    PP_CHECK_EQUAL(pp_session_stop(), PP_E_NO_SESSION);
    start_session(&fixture, NULL);
    stop_host();
}

int main(int argc, char **argv) {
    static const pp_test_case_t k_cases[] = {
        {"start_needs_host_and_jit", start_needs_host_and_jit},
        {"fork_receives_the_bridge", fork_receives_the_bridge},
        {"events_arrive_on_event_thread", events_arrive_on_event_thread},
        {"output_lines_arrive_before_the_exit", output_lines_arrive_before_the_exit},
        {"second_session_refused", second_session_refused},
        {"replaced_handler_fails_session", replaced_handler_fails_session},
        {"launch_check_fails_the_session", launch_check_fails_the_session},
        {"launch_check_fails_a_quick_program", launch_check_fails_a_quick_program},
        {"started_hook_runs_before_check", started_hook_runs_before_check},
        {"runtime_refusal_fails_session", runtime_refusal_fails_session},
        {"launch_needs_a_session", launch_needs_a_session},
        {"terminate_sets_the_exit_code", terminate_sets_the_exit_code},
        {"get_state_asks_the_fork", get_state_asks_the_fork},
        {"stop_ends_a_running_process", stop_ends_a_running_process},
        {"stop_during_a_launch", stop_during_a_launch},
        {"stop_counts_an_unreported_end", stop_counts_an_unreported_end},
        {"stop_wakes_a_reader_without_an_end", stop_wakes_a_reader_without_an_end},
        {"failed_start_is_reported", failed_start_is_reported},
        {"launch_refusal_is_reported", launch_refusal_is_reported},
        {"refusal_overrides_a_reported_exit", refusal_overrides_a_reported_exit},
        {"refusal_overrides_a_reported_exit_captured", refusal_overrides_a_reported_exit_captured},
        {"shutdown_refused_while_a_launch_runs", shutdown_refused_while_a_launch_runs},
        {"stop_waits_for_a_launch_in_the_runtime", stop_waits_for_a_launch_in_the_runtime},
        {"quick_program_still_started", quick_program_still_started},
        {"a_flood_of_lines_loses_none", a_flood_of_lines_loses_none},
        {"exit_waits_for_the_last_line", exit_waits_for_the_last_line},
        {"four_threads_at_most", four_threads_at_most},
        {"stopping_session_gets_no_thread", stopping_session_gets_no_thread},
        {"unfinished_start_fails_the_launch", unfinished_start_fails_the_launch},
        {"late_start_check_ends_the_process", late_start_check_ends_the_process},
        {"start_accepts_the_providers_ready_state", start_accepts_the_providers_ready_state},
        {"shutdown_refused_while_a_session_runs", shutdown_refused_while_a_session_runs},
        {"shutdown_collects_a_session_that_ended", shutdown_collects_a_session_that_ended},
        {"load_failure_counts_as_run", load_failure_counts_as_run},
        {"configuration_checks", configuration_checks},
        {"wrong_thread", wrong_thread},
    };
    return pp_run_case(argc, argv, k_cases, PP_CASE_COUNT(k_cases));
}
