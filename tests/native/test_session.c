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
#include "paneport/session.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "test_support.h"
#include "wine_session.h"

#ifndef FAKE_FORK_PATH
#error "CMake passes the path of the fake fork's module as FAKE_FORK_PATH."
#endif

enum { MAX_EVENTS = 16, MAX_LINES = 64, LINE_SIZE = 512 };

// What the callbacks saw. lines_before[i] counts the runtime and guest lines that had arrived
// when event i arrived.
static struct {
    pthread_mutex_t mutex;
    pthread_cond_t changed;
    pp_event_t events[MAX_EVENTS];
    uint32_t lines_before[MAX_EVENTS];
    uint32_t event_count;
    bool event_off_thread;
    char lines[MAX_LINES][LINE_SIZE];
    uint32_t sources[MAX_LINES];
    uint64_t processes[MAX_LINES];
    uint32_t line_count;
    bool line_off_thread;
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
    if (s_seen.event_count < (uint32_t)MAX_EVENTS) {
        s_seen.events[s_seen.event_count] = *event;
        s_seen.lines_before[s_seen.event_count] = s_seen.line_count;
        s_seen.event_count++;
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
    if (line->source != PP_LOG_SOURCE_HOST && s_seen.line_count < (uint32_t)MAX_LINES) {
        (void)snprintf(s_seen.lines[s_seen.line_count], LINE_SIZE, "%s", line->text);
        s_seen.sources[s_seen.line_count] = line->source;
        s_seen.processes[s_seen.line_count] = line->process;
        s_seen.line_count++;
    }
    (void)pthread_cond_broadcast(&s_seen.changed);
    (void)pthread_mutex_unlock(&s_seen.mutex);
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

// The fork receives a complete bridge of this version (C-0005 section 3).
static void fork_receives_the_bridge(void) {
    fixture_t fixture;
    make_fixture(&fixture);
    start_host();
    start_session(&fixture, NULL);
    void *module = dlopen(fixture.library, RTLD_NOW | RTLD_NOLOAD);
    PP_CHECK(module != NULL);
    void *symbol = dlsym(module, "fake_fork_bridge_ok");
    PP_CHECK(symbol != NULL);
    int (*bridge_ok)(void) = NULL;
    memcpy(&bridge_ok, &symbol, sizeof bridge_ok);
    PP_CHECK_EQUAL(bridge_ok(), 1);
    PP_CHECK_EQUAL(dlclose(module), 0);
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
    stop_host();
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

    static const char *const k_output[] = {"first line", "second line",        NULL,
                                           NULL,         FAKE_FORK_BYTES_LINE, "last line"};
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
    PP_CHECK_EQUAL(pp_process_terminate(process, 8U), PP_OK);
    PP_CHECK_EQUAL(pp_process_terminate(process + 1U, 8U), PP_E_INVALID_ARGUMENT);
    pp_process_state_t state = {.struct_size = (uint32_t)sizeof(pp_process_state_t)};
    PP_CHECK_EQUAL(pp_process_get_state(process + 1U, &state), PP_E_INVALID_ARGUMENT);
    state.struct_size = 4U;
    PP_CHECK_EQUAL(pp_process_get_state(process, &state), PP_E_INVALID_ARGUMENT);
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
    static const char *const k_bad_environments[] = {"lower=1\0", "=value\0", "NO_EQUALS\0",
                                                     "9NAME=1\0"};
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
        {"started_hook_runs_before_check", started_hook_runs_before_check},
        {"runtime_refusal_fails_session", runtime_refusal_fails_session},
        {"launch_needs_a_session", launch_needs_a_session},
        {"terminate_sets_the_exit_code", terminate_sets_the_exit_code},
        {"stop_ends_a_running_process", stop_ends_a_running_process},
        {"failed_start_is_reported", failed_start_is_reported},
        {"launch_refusal_is_reported", launch_refusal_is_reported},
        {"configuration_checks", configuration_checks},
        {"wrong_thread", wrong_thread},
    };
    return pp_run_case(argc, argv, k_cases, PP_CASE_COUNT(k_cases));
}
