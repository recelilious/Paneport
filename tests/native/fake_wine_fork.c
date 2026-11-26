// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// A fake of the Wine fork's Unix library for the session tests (test_session.c): a module that
// exports the five entry points of the fork's header include/wine/paneport.h and plays a
// session through the bridge as the fork does, with a "wineserver" thread that the host creates
// and a process thread that writes scripted output in odd chunks. Its query reports what it
// recorded of its process, as the fork's does. FAKE_FORK_MODE in the session's environment
// chooses what it does:
//
//   normal         the process starts, writes its output and ends with exit code 3, and the
//                  server thread ends after it, which ends the session
//   wait           the process starts and waits until it is terminated or the session stops
//   refuse         the session start fails
//   sigsegv        the session start installs a SIGSEGV handler of its own
//   launch_refuse  the launch fails
//   fail_start     the process ends before it finishes its start
//   query_exited   as wait, but the query reports the process as ended, with exit code
//                  FAKE_FORK_QUERY_EXIT_CODE, which the bridge never reports
//   silent_exit    as wait, but the process ends without reporting its end through the bridge
//   launch_sigsegv as wait, but the process's start installs a SIGSEGV handler of its own, as
//                  Wine's signal_init_process does upstream
//   launch_refuse_keep
//                  the launch keeps copies of the pipes' write ends and fails, so that the
//                  host's reader of the output never sees their end
//   launch_refuse_reported
//                  the launch reports the end of the process with exit code 0 and then fails, as
//                  the fork's server may report the end of a process whose first thread could not
//                  be created before the launch returns
//   quick          as normal, but the launch returns only once the process started and ended
//   quick_sigsegv  as quick, but the process's start installs a SIGSEGV handler of its own, as in
//                  launch_sigsegv
//   flood          as normal, but the process writes FAKE_FORK_FLOOD_LINES lines and the runtime
//                  logs as many, faster than the host's log callback takes them
//   late_output    the process reports its end, then writes FAKE_FORK_LATE_LINE and closes its
//                  handles, as a process that the fork's server terminates may
//   kept_output    as normal, but a copy of the output's write end stays open after the process
//                  ended, so that the host's reader of the output never sees its end
//   five_threads   the session start has the host create four threads, and a fifth refused
//   late_thread    the session's stop asks the bridge for one more thread, as a fork might while
//                  the host stops its session
//   never_started  the process never reports its start, and waits until it is terminated or the
//                  session stops
//   launch_hold_reported
//                  as launch_refuse_reported, but the launch returns only once the test opened
//                  the fake's gate, as a launch thread that the system deschedules after the
//                  server's report would
//   late_sigsegv   the process reports its start only once the test opened the fake's gate, and
//                  then as in launch_sigsegv: its start installs a SIGSEGV handler of its own, and
//                  it waits until it is terminated or the session stops
//
// fake_fork_bridge_ok reports whether the bridge it received is complete,
// fake_fork_server_stack_kib the stack of the server's thread, fake_fork_launches how often the
// host called its launch, and fake_fork_late_thread_status what the bridge's start_thread
// returned during the stop of the mode late_thread, 1 until then. fake_fork_reached_gate waits up
// to a minute until the fake waits at its gate, and fake_fork_open_gate opens the gate. Its
// terminate refuses a process that is not running.

#include <pthread.h>
#include <signal.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "fake_wine_fork.h"
#include "fake_wine_fork_exports.h"
#include "paneport/wine_bridge.h"
#include "wine/paneport.h"

#define FAKE_EXPORT __attribute__((visibility("default")))

// The state of the fake: one session and one process at most.
static struct {
    pthread_mutex_t mutex;
    pthread_cond_t changed;
    const pp_wine_bridge_t *bridge;
    char mode[32];
    bool stop_server;
    bool process_running;
    bool terminate;
    uint32_t exit_code;
    uint64_t process;
    int output_fd;
    int errors_fd;
    // The copy of the output's write end that the mode kept_output never closes.
    int kept_fd;
    // What the query reports: 0 before a launch, then a WINE_PANEPORT_PROCESS_ state, and the
    // exit code that the bridge reported.
    uint32_t query_state;
    uint32_t query_exit_code;
    int launches;
    // The stack size of the server's thread, as the thread itself reads it.
    size_t server_stack;
    // What start_thread returned during the stop in the mode late_thread, 1 until then.
    int32_t late_thread_status;
    // The gate of the modes launch_hold_reported and late_sigsegv: whether the fake waits at it,
    // and whether the test opened it.
    bool at_gate;
    bool gate_open;
} s_fake = {
    .mutex = PTHREAD_MUTEX_INITIALIZER,
    .changed = PTHREAD_COND_INITIALIZER,
    .output_fd = -1,
    .errors_fd = -1,
    .kept_fd = -1,
    .late_thread_status = 1,
};

static void read_mode(const char *environment) {
    (void)snprintf(s_fake.mode, sizeof s_fake.mode, "%s", "normal");
    if (environment == NULL) {
        return;
    }
    static const char k_name[] = "FAKE_FORK_MODE=";
    for (const char *entry = environment; *entry != '\0'; entry += strlen(entry) + 1U) {
        if (strncmp(entry, k_name, sizeof k_name - 1U) == 0) {
            const char *value = entry + sizeof k_name - 1U;
            if (strlen(value) < sizeof s_fake.mode) {
                (void)snprintf(s_fake.mode, sizeof s_fake.mode, "%s", value);
            }
        }
    }
}

static bool mode_is(const char *mode) { return strcmp(s_fake.mode, mode) == 0; }

// Whether the process waits until it is terminated or the session stops.
static bool process_waits(void) {
    if (mode_is("wait") || mode_is("silent_exit") || mode_is("launch_sigsegv") ||
        mode_is("late_sigsegv")) {
        return true;
    }
    return mode_is("query_exited");
}

// Records what the query reports, before the bridge hears of it, as the fork records it.
static void record_state(uint32_t state, uint32_t exit_code) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.query_state = state;
    s_fake.query_exit_code = exit_code;
    (void)pthread_mutex_unlock(&s_fake.mutex);
}

static void on_segv(int signal) { (void)signal; }

static void install_segv_handler(void) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = on_segv;
    (void)sigemptyset(&action.sa_mask);
    (void)sigaction(SIGSEGV, &action, NULL);
}

static void wait_for_stop(void) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    while (!s_fake.stop_server) {
        (void)pthread_cond_wait(&s_fake.changed, &s_fake.mutex);
    }
    (void)pthread_mutex_unlock(&s_fake.mutex);
}

// Waits at the gate until the test opens it, for up to a minute.
static void wait_at_gate(void) {
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += 60;
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.at_gate = true;
    (void)pthread_cond_broadcast(&s_fake.changed);
    while (!s_fake.gate_open) {
        if (pthread_cond_timedwait(&s_fake.changed, &s_fake.mutex, &until) != 0) {
            break;
        }
    }
    (void)pthread_mutex_unlock(&s_fake.mutex);
}

// The "wineserver": it records the stack that the host gave its thread and waits for the stop.
static void run_server(void *argument) {
    (void)argument;
    const size_t stack = pthread_get_stacksize_np(pthread_self());
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.server_stack = stack;
    (void)pthread_cond_broadcast(&s_fake.changed);
    (void)pthread_mutex_unlock(&s_fake.mutex);
    wait_for_stop();
}

// A further thread of the mode five_threads, which waits for the stop.
static void run_helper(void *argument) {
    (void)argument;
    wait_for_stop();
}

// Writes text in chunks of 1, 2, 3, 5, 8 and 13 bytes, over and over, so that lines, carriage
// returns and characters of several bytes are cut at every kind of place.
static void write_chunked(int fd, const char *text) {
    static const size_t k_sizes[] = {1U, 2U, 3U, 5U, 8U, 13U};
    size_t left = strlen(text);
    size_t round = 0U;
    while (left > 0U) {
        size_t size = k_sizes[round % (sizeof k_sizes / sizeof k_sizes[0])];
        if (size > left) {
            size = left;
        }
        const ssize_t written = write(fd, text, size);
        if (written <= 0) {
            return;
        }
        text += written;
        left -= (size_t)written;
        round++;
    }
}

// Writes the scripted output, or in the mode flood FAKE_FORK_FLOOD_LINES lines of output and as
// many lines of the runtime, as fast as the host takes them; in the mode late_output nothing yet.
static void write_output(const pp_wine_bridge_t *bridge) {
    if (mode_is("flood")) {
        for (uint32_t index = 0U; index < FAKE_FORK_FLOOD_LINES; index++) {
            if (s_fake.output_fd >= 0) {
                write_chunked(s_fake.output_fd, FAKE_FORK_FLOOD_TEXT "\n");
            }
            bridge->log(PP_WINE_LOG_INFO, FAKE_FORK_FLOOD_TEXT);
        }
        return;
    }
    if (mode_is("late_output")) {
        return;
    }
    if (s_fake.output_fd >= 0) {
        write_chunked(s_fake.output_fd, FAKE_FORK_OUTPUT);
    }
    if (s_fake.errors_fd >= 0) {
        write_chunked(s_fake.errors_fd, FAKE_FORK_ERRORS);
    }
}

static void close_handles(void) {
    if (s_fake.output_fd >= 0) {
        (void)close(s_fake.output_fd);
    }
    if (s_fake.errors_fd >= 0) {
        (void)close(s_fake.errors_fd);
    }
}

// Waits until the process is terminated or the session stops, and returns the exit code that the
// process then gets.
static uint32_t wait_until_ended(void) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    while (!s_fake.terminate && !s_fake.stop_server) {
        (void)pthread_cond_wait(&s_fake.changed, &s_fake.mutex);
    }
    uint32_t exit_code = FAKE_FORK_STOPPED_EXIT_CODE;
    if (s_fake.terminate) {
        exit_code = s_fake.exit_code;
    }
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return exit_code;
}

static void *run_process(void *argument) {
    (void)argument;
    const pp_wine_bridge_t *bridge = s_fake.bridge;
    (void)bridge->mark_thread(PP_THREAD_MARK_GUEST);
    uint32_t exit_code = FAKE_FORK_EXIT_CODE;
    if (mode_is("fail_start")) {
        exit_code = FAKE_FORK_START_FAILURE;
    } else if (mode_is("never_started")) {
        // The start never ends, and the process is never reported as started.
        exit_code = wait_until_ended();
    } else {
        if (mode_is("late_sigsegv")) {
            // The start takes until the test opens the gate.
            wait_at_gate();
        }
        bridge->log(PP_WINE_LOG_INFO, "fake runtime: process started\nsecond runtime line");
        if (mode_is("launch_sigsegv") || mode_is("quick_sigsegv") || mode_is("late_sigsegv")) {
            install_segv_handler();
        }
        record_state(WINE_PANEPORT_PROCESS_RUNNING, 0U);
        bridge->process_started(s_fake.process);
        if (process_waits()) {
            exit_code = wait_until_ended();
        } else {
            write_output(bridge);
        }
    }
    if (mode_is("kept_output") && s_fake.output_fd >= 0) {
        // The copy stays open for the rest of the process.
        s_fake.kept_fd = dup(s_fake.output_fd);
    }
    // The process's handles go before it reports its end, as the fork closes them, except in the
    // mode late_output: the fork's server reports the end of a process that it terminates before
    // the process's threads closed theirs.
    if (!mode_is("late_output")) {
        close_handles();
    }
    (void)bridge->unmark_thread();
    if (!mode_is("silent_exit")) {
        record_state(WINE_PANEPORT_PROCESS_EXITED, exit_code);
        bridge->process_exited(s_fake.process, exit_code);
    }
    if (mode_is("late_output")) {
        if (s_fake.output_fd >= 0) {
            write_chunked(s_fake.output_fd, FAKE_FORK_LATE_LINE "\n");
        }
        close_handles();
    }
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.process_running = false;
    // The session's last process ended, so the server ends after it, as the fork's does.
    s_fake.stop_server = true;
    (void)pthread_cond_broadcast(&s_fake.changed);
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return NULL;
}

FAKE_EXPORT int32_t __wine_paneport_session_start(const pp_wine_bridge_t *bridge,
                                                  const struct wine_paneport_session *session) {
    if (bridge == NULL || session == NULL || bridge->version < PP_WINE_BRIDGE_VERSION ||
        session->struct_size < sizeof(struct wine_paneport_session) ||
        session->architecture != WINE_PANEPORT_ARCH_X86_64) {
        return PP_WINE_E_INVALID_ARGUMENT;
    }
    s_fake.bridge = bridge;
    read_mode(session->environment);
    if (mode_is("refuse")) {
        bridge->log(PP_WINE_LOG_ERROR, "fake runtime: refusing the session");
        return PP_WINE_E_SYSTEM;
    }
    if (mode_is("sigsegv")) {
        install_segv_handler();
    }
    const int32_t status = bridge->start_thread("fake.wineserver", run_server, NULL);
    if (status != PP_WINE_OK || !mode_is("five_threads")) {
        return status;
    }
    // The host keeps four threads for a session: three more start, and it refuses a fifth.
    for (int index = 0; index < 3; index++) {
        const int32_t started = bridge->start_thread("fake.helper", run_helper, NULL);
        if (started != PP_WINE_OK) {
            return started;
        }
    }
    if (bridge->start_thread("fake.fifth", run_helper, NULL) != PP_WINE_E_BUSY) {
        return PP_WINE_E_UNSUPPORTED;
    }
    return PP_WINE_OK;
}

FAKE_EXPORT int32_t __wine_paneport_session_stop(void) {
    if (mode_is("late_thread")) {
        // The host has begun to stop the session, which takes no more threads.
        const int32_t status = s_fake.bridge->start_thread("fake.late", run_helper, NULL);
        (void)pthread_mutex_lock(&s_fake.mutex);
        s_fake.late_thread_status = status;
        (void)pthread_mutex_unlock(&s_fake.mutex);
    }
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.stop_server = true;
    (void)pthread_cond_broadcast(&s_fake.changed);
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return PP_WINE_OK;
}

FAKE_EXPORT int32_t __wine_paneport_process_launch(const struct wine_paneport_launch *launch) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.launches++;
    (void)pthread_mutex_unlock(&s_fake.mutex);
    if (launch == NULL || launch->struct_size < sizeof(struct wine_paneport_launch) ||
        launch->program == NULL) {
        return PP_WINE_E_INVALID_ARGUMENT;
    }
    if (mode_is("launch_refuse")) {
        return PP_WINE_E_SYSTEM;
    }
    if (mode_is("launch_refuse_keep")) {
        // The copies stay open for the rest of the process.
        s_fake.output_fd = launch->stdout_fd >= 0 ? dup(launch->stdout_fd) : -1;
        s_fake.errors_fd = launch->stderr_fd >= 0 ? dup(launch->stderr_fd) : -1;
        return PP_WINE_E_SYSTEM;
    }
    if (mode_is("launch_refuse_reported")) {
        // The server knew the process and reports its end, with an exit code of its own, before
        // the launch returns, as the fork's server may on its own thread.
        (void)pthread_mutex_lock(&s_fake.mutex);
        s_fake.process = launch->process;
        (void)pthread_mutex_unlock(&s_fake.mutex);
        record_state(WINE_PANEPORT_PROCESS_EXITED, 0U);
        s_fake.bridge->process_exited(launch->process, 0U);
        return PP_WINE_E_SYSTEM;
    }
    if (mode_is("launch_hold_reported")) {
        // As launch_refuse_reported, once the test opened the gate.
        (void)pthread_mutex_lock(&s_fake.mutex);
        s_fake.process = launch->process;
        (void)pthread_mutex_unlock(&s_fake.mutex);
        record_state(WINE_PANEPORT_PROCESS_EXITED, 0U);
        s_fake.bridge->process_exited(launch->process, 0U);
        wait_at_gate();
        return PP_WINE_E_SYSTEM;
    }
    // The host closes its copies of the descriptors once the launch returns.
    s_fake.output_fd = launch->stdout_fd >= 0 ? dup(launch->stdout_fd) : -1;
    s_fake.errors_fd = launch->stderr_fd >= 0 ? dup(launch->stderr_fd) : -1;
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.process = launch->process;
    s_fake.process_running = true;
    s_fake.query_state = WINE_PANEPORT_PROCESS_STARTING;
    (void)pthread_mutex_unlock(&s_fake.mutex);
    pthread_t thread;
    if (pthread_create(&thread, NULL, run_process, NULL) != 0) {
        return PP_WINE_E_SYSTEM;
    }
    if (mode_is("quick") || mode_is("quick_sigsegv")) {
        // The process starts and ends before the host looks at it.
        (void)pthread_join(thread, NULL);
        return PP_WINE_OK;
    }
    (void)pthread_detach(thread);
    return PP_WINE_OK;
}

// Terminates the running process; a process that ended is no longer the server's to terminate.
FAKE_EXPORT int32_t __wine_paneport_process_terminate(uint64_t process, uint32_t exit_code) {
    int32_t status = PP_WINE_E_INVALID_ARGUMENT;
    (void)pthread_mutex_lock(&s_fake.mutex);
    if (process == s_fake.process && s_fake.process_running) {
        s_fake.terminate = true;
        s_fake.exit_code = exit_code;
        (void)pthread_cond_broadcast(&s_fake.changed);
        status = PP_WINE_OK;
    }
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return status;
}

FAKE_EXPORT int32_t __wine_paneport_process_query(uint64_t process,
                                                  struct wine_paneport_process_state *state) {
    if (state == NULL || state->struct_size < sizeof(struct wine_paneport_process_state)) {
        return PP_WINE_E_INVALID_ARGUMENT;
    }
    int32_t status = PP_WINE_E_INVALID_ARGUMENT;
    (void)pthread_mutex_lock(&s_fake.mutex);
    // Only a launched process is known, as the fork's server knows only the processes it took.
    if (process != 0U && process == s_fake.process && s_fake.query_state != 0U) {
        state->state = s_fake.query_state;
        state->exit_code =
            s_fake.query_state == WINE_PANEPORT_PROCESS_EXITED ? s_fake.query_exit_code : 0U;
        if (mode_is("query_exited")) {
            state->state = WINE_PANEPORT_PROCESS_EXITED;
            state->exit_code = FAKE_FORK_QUERY_EXIT_CODE;
        }
        status = PP_WINE_OK;
    }
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return status;
}

// Waits up to a minute for the server's thread to record its stack, which it does when it starts.
FAKE_EXPORT int fake_fork_server_stack_kib(void) {
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += 60;
    (void)pthread_mutex_lock(&s_fake.mutex);
    while (s_fake.server_stack == 0U) {
        if (pthread_cond_timedwait(&s_fake.changed, &s_fake.mutex, &until) != 0) {
            break;
        }
    }
    const size_t stack = s_fake.server_stack;
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return (int)(stack / 1024U);
}

FAKE_EXPORT int fake_fork_launches(void) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    const int launches = s_fake.launches;
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return launches;
}

FAKE_EXPORT int fake_fork_late_thread_status(void) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    const int32_t status = s_fake.late_thread_status;
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return (int)status;
}

FAKE_EXPORT int fake_fork_reached_gate(void) {
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += 60;
    (void)pthread_mutex_lock(&s_fake.mutex);
    while (!s_fake.at_gate) {
        if (pthread_cond_timedwait(&s_fake.changed, &s_fake.mutex, &until) != 0) {
            break;
        }
    }
    int reached = 0;
    if (s_fake.at_gate) {
        reached = 1;
    }
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return reached;
}

FAKE_EXPORT int fake_fork_open_gate(void) {
    (void)pthread_mutex_lock(&s_fake.mutex);
    s_fake.gate_open = true;
    (void)pthread_cond_broadcast(&s_fake.changed);
    (void)pthread_mutex_unlock(&s_fake.mutex);
    return 1;
}

FAKE_EXPORT int fake_fork_bridge_ok(void) {
    const pp_wine_bridge_t *bridge = s_fake.bridge;
    if (bridge == NULL || bridge->struct_size != sizeof(pp_wine_bridge_t) ||
        bridge->version != PP_WINE_BRIDGE_VERSION) {
        return 0;
    }
    const void *const members[] = {
        (const void *)bridge->log,
        (const void *)bridge->start_thread,
        (const void *)bridge->mark_thread,
        (const void *)bridge->unmark_thread,
        (const void *)bridge->add_reservation,
        (const void *)bridge->remove_reservation,
        (const void *)bridge->set_guest_fault_handler,
        (const void *)bridge->process_started,
        (const void *)bridge->process_exited,
    };
    for (size_t index = 0U; index < sizeof members / sizeof members[0]; index++) {
        if (members[index] == NULL) {
            return 0;
        }
    }
    return 1;
}
