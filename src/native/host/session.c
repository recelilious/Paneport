// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The session and process groups of paneport/session.h, and the members of the Wine bridge of
// paneport/wine_bridge.h (C-0005 sections 2 and 3). wine_session.h says what it does.

#include "wine_session.h"

#include <dlfcn.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#include "error.h"
#include "event_thread.h"
#include "guest_output.h"
#include "host_state.h"
#include "jit_state.h"
#include "log.h"
#include "paneport/host.h"
#include "paneport/session.h"
#include "paneport/wine_bridge.h"
#include "platform.h"
#include "signals/signals.h"
#include "wine/paneport.h"

// The bridge's values are those of the host interface, which its header cannot include.
_Static_assert(PP_WINE_OK == PP_OK, "PP_WINE_OK differs from PP_OK.");
_Static_assert(PP_WINE_E_INVALID_ARGUMENT == PP_E_INVALID_ARGUMENT, "A status code differs.");
_Static_assert(PP_WINE_E_OUT_OF_MEMORY == PP_E_OUT_OF_MEMORY, "A status code differs.");
_Static_assert(PP_WINE_E_IO == PP_E_IO, "A status code differs.");
_Static_assert(PP_WINE_E_UNSUPPORTED == PP_E_UNSUPPORTED, "A status code differs.");
_Static_assert(PP_WINE_E_BUSY == PP_E_BUSY, "A status code differs.");
_Static_assert(PP_WINE_E_SYSTEM == PP_E_SYSTEM, "A status code differs.");
_Static_assert(PP_WINE_LOG_DEBUG == PP_LOG_DEBUG && PP_WINE_LOG_ERROR == PP_LOG_ERROR,
               "The bridge's log levels differ from PP_LOG_.");

// The life of the launch's one session.
enum {
    PP_SESSION_NONE = 0,
    PP_SESSION_STARTING = 1,
    PP_SESSION_RUNNING = 2,
    PP_SESSION_STOPPING = 3,
    PP_SESSION_ENDED = 4,
};

// How many threads the fork may have the host create for a session.
#define PP_SESSION_THREADS 4U

// The limits of the blocks of environment variables and arguments.
#define PP_ENVIRONMENT_MAX (64U * 1024U)
#define PP_ARGUMENTS_MAX (32U * 1024U)

// How long pp_process_launch waits for the process to finish its start, and pp_session_stop for
// the runtime to report the end of the process that it terminated.
#define PP_LAUNCH_WAIT_SECONDS 120
#define PP_STOP_WAIT_SECONDS 30

// A thread that the host created for the fork.
typedef struct session_thread_t {
    pthread_t thread;
    pp_wine_thread_body_t body;
    void *argument;
    char name[64];
    bool joined;
} session_thread_t;

// The session's process. Its handle is 0 until pp_process_launch starts one. Its exit event
// waits until the reader of its output queued the last line (output_done), so that the lines
// arrive before it (paneport/session.h).
typedef struct session_process_t {
    uint64_t handle;
    uint32_t state;
    uint32_t exit_code;
    pp_guest_output_t *output;
    bool output_done;
    bool exit_posted;
} session_process_t;

// The session: a process-wide singleton (C-0010 section 3). The mutex guards every member, and
// the condition variable announces every change of the session's or the process's state.
static struct {
    pthread_mutex_t mutex;
    pthread_cond_t changed;
    int state;
    void *library;
    wine_paneport_session_stop_t stop;
    wine_paneport_process_launch_t launch;
    wine_paneport_process_terminate_t terminate;
    session_thread_t threads[PP_SESSION_THREADS];
    uint32_t thread_count;
    uint32_t threads_running;
    bool ended_reported;
    bool end_pending;
    pp_status_t end_status;
    session_process_t process;
} s_session = {
    .mutex = PTHREAD_MUTEX_INITIALIZER,
    .changed = PTHREAD_COND_INITIALIZER,
};

#ifdef PANEPORT_TEST_BUILD
static pp_session_hook_t s_started_hook;
static void *s_started_hook_argument;

void pp_session_set_started_hook(pp_session_hook_t hook, void *argument) {
    (void)pthread_mutex_lock(&s_session.mutex);
    s_started_hook = hook;
    s_started_hook_argument = argument;
    (void)pthread_mutex_unlock(&s_session.mutex);
}
#endif

// --- Events ---------------------------------------------------------------------------------

static void post_event(uint32_t kind, uint64_t handle, int64_t code) {
    const pp_event_t event = {
        .struct_size = (uint32_t)sizeof(pp_event_t),
        .kind = kind,
        .code = code,
        .handle = handle,
        .address = 0U,
    };
    // A full queue counts the event and the event thread reports it; nothing is left to do.
    (void)pp_event_thread_post_event(&event);
}

// Reports the end of the session once, after the process's exit event. The caller holds the
// mutex.
static void report_session_ended(pp_status_t status) {
    if (s_session.ended_reported) {
        return;
    }
    if (s_session.process.handle != 0U && !s_session.process.exit_posted) {
        s_session.end_pending = true;
        s_session.end_status = status;
        return;
    }
    s_session.ended_reported = true;
    s_session.end_pending = false;
    post_event(PP_EVENT_SESSION_ENDED, 0U, status);
}

// Reports the process's exit once it ended and its output reader queued its last line, then a
// session end that waited for it. The caller holds the mutex.
static void report_exit_when_ready(void) {
    session_process_t *process = &s_session.process;
    if (process->handle == 0U || process->exit_posted || process->state != PP_PROCESS_EXITED ||
        !process->output_done) {
        return;
    }
    process->exit_posted = true;
    post_event(PP_EVENT_PROCESS_EXITED, process->handle, (int64_t)process->exit_code);
    if (s_session.end_pending) {
        report_session_ended(s_session.end_status);
    }
}

// --- The bridge -------------------------------------------------------------------------------

static void bridge_log(int32_t level, const char *text) {
    if (text == NULL) {
        return;
    }
    if (level < PP_LOG_DEBUG || level > PP_LOG_ERROR) {
        level = PP_LOG_INFO;
    }
    // A text with line breaks becomes one log line per line.
    const char *line = text;
    while (*line != '\0') {
        const char *end = strchr(line, '\n');
        const size_t length = end == NULL ? strlen(line) : (size_t)(end - line);
        pp_log_entry_t entry;
        pp_log_text(&entry, level, PP_LOG_SOURCE_RUNTIME, 0U, line, length);
        (void)pp_event_thread_post_log_waiting(&entry);
        if (end == NULL) {
            break;
        }
        line = end + 1;
    }
}

static void *run_session_thread(void *argument) {
    session_thread_t *slot = argument;
    pp_platform_name_thread(slot->name);
    slot->body(slot->argument);

    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.threads_running--;
    if (s_session.threads_running == 0U &&
        (s_session.state == PP_SESSION_RUNNING || s_session.state == PP_SESSION_STOPPING)) {
        // The fork's threads ended by themselves: the session is over.
        s_session.state = PP_SESSION_ENDED;
        report_session_ended(PP_OK);
    }
    (void)pthread_cond_broadcast(&s_session.changed);
    (void)pthread_mutex_unlock(&s_session.mutex);
    return NULL;
}

static int32_t bridge_start_thread(const char *name, pp_wine_thread_body_t body, void *argument) {
    if (name == NULL || body == NULL) {
        return PP_E_INVALID_ARGUMENT;
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    int32_t status = PP_OK;
    if (s_session.state != PP_SESSION_STARTING && s_session.state != PP_SESSION_RUNNING) {
        status = PP_E_NO_SESSION;
    } else if (s_session.thread_count == PP_SESSION_THREADS) {
        status = PP_E_BUSY;
    } else {
        session_thread_t *slot = &s_session.threads[s_session.thread_count];
        slot->body = body;
        slot->argument = argument;
        slot->joined = false;
        (void)snprintf(slot->name, sizeof slot->name, "%s", name);
        s_session.threads_running++;
        if (pthread_create(&slot->thread, NULL, run_session_thread, slot) != 0) {
            s_session.threads_running--;
            status = PP_E_SYSTEM;
        } else {
            s_session.thread_count++;
        }
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
    return status;
}

static int32_t bridge_mark_thread(uint32_t mark) { return pp_signals_mark_thread(mark); }

static int32_t bridge_unmark_thread(void) { return pp_signals_unmark_thread(); }

static int32_t bridge_add_reservation(uint64_t start, uint64_t size, uint64_t *handle) {
    return pp_signals_add_reservation(start, size, handle);
}

static int32_t bridge_remove_reservation(uint64_t handle) {
    return pp_signals_remove_reservation(handle);
}

static int32_t bridge_set_guest_fault_handler(pp_guest_fault_handler_t handler, void *user_data) {
    return pp_signals_set_guest_handler(handler, user_data);
}

static void bridge_process_started(uint64_t process) {
    (void)pthread_mutex_lock(&s_session.mutex);
    if (process != 0U && process == s_session.process.handle &&
        s_session.process.state == PP_PROCESS_STARTING) {
        s_session.process.state = PP_PROCESS_RUNNING;
        post_event(PP_EVENT_PROCESS_STARTED, process, 0);
        (void)pthread_cond_broadcast(&s_session.changed);
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
}

static void bridge_process_exited(uint64_t process, uint32_t exit_code) {
    (void)pthread_mutex_lock(&s_session.mutex);
    if (process != 0U && process == s_session.process.handle &&
        s_session.process.state != PP_PROCESS_EXITED) {
        s_session.process.state = PP_PROCESS_EXITED;
        s_session.process.exit_code = exit_code;
        report_exit_when_ready();
        (void)pthread_cond_broadcast(&s_session.changed);
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
}

// The output reader's end: every line of the process is queued.
static void output_ended(uint64_t process) {
    (void)pthread_mutex_lock(&s_session.mutex);
    if (process == s_session.process.handle) {
        s_session.process.output_done = true;
        report_exit_when_ready();
        (void)pthread_cond_broadcast(&s_session.changed);
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
}

// The table that the fork receives; it stays valid until the process ends.
static const pp_wine_bridge_t k_bridge = {
    .struct_size = (uint32_t)sizeof(pp_wine_bridge_t),
    .version = PP_WINE_BRIDGE_VERSION,
    .log = bridge_log,
    .start_thread = bridge_start_thread,
    .mark_thread = bridge_mark_thread,
    .unmark_thread = bridge_unmark_thread,
    .add_reservation = bridge_add_reservation,
    .remove_reservation = bridge_remove_reservation,
    .set_guest_fault_handler = bridge_set_guest_fault_handler,
    .process_started = bridge_process_started,
    .process_exited = bridge_process_exited,
};

// --- Checks of the arguments -------------------------------------------------------------------

// Checks that a path is absolute, short enough and names a folder.
static pp_status_t check_folder(const char *function, const char *member, const char *path) {
    if (path == NULL || path[0] != '/') {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "%s needs an absolute path in %s.", function,
                            member);
    }
    if (strnlen(path, PATH_MAX) >= PATH_MAX) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "%s needs a %s shorter than %d bytes.", function,
                            member, PATH_MAX);
    }
    struct stat status;
    if (stat(path, &status) != 0 || !S_ISDIR(status.st_mode)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "%s needs an existing folder in %s.", function,
                            member);
    }
    return PP_OK;
}

static bool is_name_character(char character, bool first) {
    if (character >= 'A' && character <= 'Z') {
        return true;
    }
    if (character == '_') {
        return true;
    }
    return !first && character >= '0' && character <= '9';
}

// Checks a block of strings, each NUL-terminated, that ends with an empty string within limit
// bytes; with names, each string is NAME=value.
static pp_status_t check_block(const char *function, const char *member, const char *block,
                               size_t limit, bool names) {
    if (block == NULL) {
        return PP_OK;
    }
    size_t offset = 0U;
    while (offset < limit) {
        const size_t length = strnlen(block + offset, limit - offset);
        if (length == limit - offset) {
            break;
        }
        if (length == 0U) {
            return PP_OK;
        }
        if (names) {
            const char *entry = block + offset;
            size_t index = 0U;
            while (index < length && is_name_character(entry[index], index == 0U)) {
                index++;
            }
            if (index == 0U || index == length || entry[index] != '=') {
                return pp_error_set(PP_E_INVALID_ARGUMENT,
                                    "%s needs NAME=value strings in %s, with a name of capital "
                                    "letters, digits and underscores.",
                                    function, member);
            }
        }
        offset += length + 1U;
    }
    return pp_error_set(PP_E_INVALID_ARGUMENT,
                        "%s needs %s to end with an empty string within %zu bytes.", function,
                        member, limit);
}

// --- The session --------------------------------------------------------------------------------

// Joins the threads that the host created for the session. The caller holds no lock; only the
// main thread joins.
static void join_session_threads(void) {
    for (uint32_t index = 0U; index < PP_SESSION_THREADS; index++) {
        (void)pthread_mutex_lock(&s_session.mutex);
        const bool joinable = index < s_session.thread_count && !s_session.threads[index].joined;
        const pthread_t thread = s_session.threads[index].thread;
        if (joinable) {
            s_session.threads[index].joined = true;
        }
        (void)pthread_mutex_unlock(&s_session.mutex);
        if (joinable) {
            (void)pthread_join(thread, NULL);
        }
    }
}

// Ends a session whose start failed after the runtime was involved: it counts as having run.
static pp_status_t fail_started_session(pp_status_t status) {
    if (s_session.stop != NULL) {
        (void)s_session.stop();
    }
    join_session_threads();
    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.state = PP_SESSION_ENDED;
    report_session_ended(status);
    (void)pthread_cond_broadcast(&s_session.changed);
    (void)pthread_mutex_unlock(&s_session.mutex);
    return status;
}

_Static_assert(sizeof(void *) == sizeof(wine_paneport_session_start_t),
               "A function pointer does not fit a data pointer.");

// Finds an entry point of the runtime and copies it into *entry, a function pointer of size
// bytes: ISO C has no cast between a data pointer and a function pointer.
static void find_entry(void *library, const char *name, void *entry, size_t size,
                       pp_status_t *status) {
    void *symbol = dlsym(library, name);
    if (symbol == NULL) {
        if (*status == PP_OK) {
            *status = pp_error_set(PP_E_IO, "The Wine runtime has no entry point %s.", name);
        }
        return;
    }
    memcpy(entry, &symbol, size);
}

PP_EXPORT pp_status_t pp_session_start(const pp_session_config_t *config) {
    pp_error_clear();
    pp_status_t status = pp_host_check_main_call("pp_session_start");
    if (status != PP_OK) {
        return status;
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    const int state = s_session.state;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (state != PP_SESSION_NONE) {
        return pp_error_set(PP_E_SESSION_EXISTS, "A session has already run in this launch; "
                                                 "another one needs a relaunch of the app.");
    }
    if (!pp_jit_state_is_ready()) {
        return pp_error_set(PP_E_JIT_NOT_READY, "pp_session_start needs the JIT state ready.");
    }
    if (config == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_session_start needs a configuration.");
    }
    if (config->struct_size < sizeof(pp_session_config_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_session_start needs a struct_size of at least %zu, not %u.",
                            sizeof(pp_session_config_t), (unsigned)config->struct_size);
    }
    if (config->architecture == PP_ARCHITECTURE_I686) {
        return pp_error_set(PP_E_UNSUPPORTED, "32-bit programs do not run in this build.");
    }
    if (config->architecture != PP_ARCHITECTURE_X86_64) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_session_start does not know architecture %u.",
                            (unsigned)config->architecture);
    }
    status = check_folder("pp_session_start", "prefix_folder", config->prefix_folder);
    if (status == PP_OK) {
        status = check_folder("pp_session_start", "runtime_folder", config->runtime_folder);
    }
    if (status == PP_OK) {
        status = check_block("pp_session_start", "environment", config->environment,
                             PP_ENVIRONMENT_MAX, true);
    }
    if (status != PP_OK) {
        return status;
    }
    char library_path[PATH_MAX];
    const int length = snprintf(library_path, sizeof library_path, "%s/%s", config->runtime_folder,
                                PP_WINE_LIBRARY_PATH);
    struct stat library_status;
    if (length <= 0 || (size_t)length >= sizeof library_path ||
        stat(library_path, &library_status) != 0 || !S_ISREG(library_status.st_mode)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_session_start needs a runtime folder that holds %s.",
                            PP_WINE_LIBRARY_PATH);
    }

    (void)pthread_mutex_lock(&s_session.mutex);
    if (s_session.state != PP_SESSION_NONE) {
        (void)pthread_mutex_unlock(&s_session.mutex);
        return pp_error_set(PP_E_SESSION_EXISTS, "A session has already run in this launch; "
                                                 "another one needs a relaunch of the app.");
    }
    // From here on the session counts as having run, whatever happens.
    s_session.state = PP_SESSION_STARTING;
    (void)pthread_mutex_unlock(&s_session.mutex);

    // The runtime stays loaded until the process ends: its threads and state outlive the session.
    void *library = dlopen(library_path, RTLD_NOW | RTLD_LOCAL);
    if (library == NULL) {
        // dlerror names the path, which the host never logs.
        return fail_started_session(pp_error_set(PP_E_IO, "The Wine runtime could not be loaded."));
    }
    status = PP_OK;
    wine_paneport_session_start_t start = NULL;
    wine_paneport_session_stop_t stop = NULL;
    wine_paneport_process_launch_t launch = NULL;
    wine_paneport_process_terminate_t terminate = NULL;
    find_entry(library, WINE_PANEPORT_SESSION_START, &start, sizeof start, &status);
    find_entry(library, WINE_PANEPORT_SESSION_STOP, &stop, sizeof stop, &status);
    find_entry(library, WINE_PANEPORT_PROCESS_LAUNCH, &launch, sizeof launch, &status);
    find_entry(library, WINE_PANEPORT_PROCESS_TERMINATE, &terminate, sizeof terminate, &status);
    if (status != PP_OK) {
        return fail_started_session(status);
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.library = library;
    s_session.stop = stop;
    s_session.launch = launch;
    s_session.terminate = terminate;
    (void)pthread_mutex_unlock(&s_session.mutex);

    const struct wine_paneport_session session = {
        .struct_size = (uint32_t)sizeof(struct wine_paneport_session),
        .architecture = WINE_PANEPORT_ARCH_X86_64,
        .prefix_folder = config->prefix_folder,
        .environment = config->environment,
    };
    const int32_t started = start(&k_bridge, &session);
    if (started != PP_WINE_OK) {
        return fail_started_session(pp_error_set(
            started < 0 ? started : PP_E_SYSTEM,
            "The Wine runtime refused to start the session (%d); its log says why.", started));
    }

#ifdef PANEPORT_TEST_BUILD
    (void)pthread_mutex_lock(&s_session.mutex);
    const pp_session_hook_t hook = s_started_hook;
    void *const hook_argument = s_started_hook_argument;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (hook != NULL) {
        hook(hook_argument);
    }
    // Test builds check that the runtime left the dispatcher's six handlers in place (C-0005
    // section 4); the check's message names the first that it replaced.
    status = pp_signals_check_installed(NULL);
    if (status != PP_OK) {
        (void)pp_log(PP_LOG_ERROR, "The Wine session failed its check of the signal dispatcher.");
        return fail_started_session(status);
    }
#endif

    (void)pthread_mutex_lock(&s_session.mutex);
    if (s_session.state == PP_SESSION_STARTING) {
        s_session.state = PP_SESSION_RUNNING;
    }
    (void)pthread_cond_broadcast(&s_session.changed);
    (void)pthread_mutex_unlock(&s_session.mutex);
    (void)pp_log(PP_LOG_INFO, "The Wine session started.");
    return PP_OK;
}

PP_EXPORT pp_status_t pp_session_stop(void) {
    pp_error_clear();
    const pp_status_t status = pp_host_check_main_call("pp_session_stop");
    if (status != PP_OK) {
        return status;
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    const int state = s_session.state;
    if (state == PP_SESSION_RUNNING) {
        s_session.state = PP_SESSION_STOPPING;
    }
    const wine_paneport_session_stop_t stop = s_session.stop;
    pp_guest_output_t *const output = s_session.process.output;
    s_session.process.output = NULL;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (state == PP_SESSION_NONE) {
        return pp_error_set(PP_E_NO_SESSION, "pp_session_stop needs a session that started.");
    }

    if (state == PP_SESSION_RUNNING && stop != NULL && stop() != PP_WINE_OK) {
        (void)pp_log(PP_LOG_WARNING, "The Wine runtime reported a problem while it stopped.");
    }
    join_session_threads();
    // The reader ends once the runtime closed its ends of the pipes, with the process.
    pp_guest_output_join(output);

    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.state = PP_SESSION_ENDED;
    session_process_t *process = &s_session.process;
    // The runtime reports the end of the process that the stop terminated, perhaps after it
    // closed the process's pipes; wait a while for the report.
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += PP_STOP_WAIT_SECONDS;
    while (process->handle != 0U && process->state != PP_PROCESS_EXITED) {
        if (pthread_cond_timedwait(&s_session.changed, &s_session.mutex, &until) == ETIMEDOUT) {
            break;
        }
    }
    if (process->handle != 0U && !process->exit_posted) {
        // The reader has ended; a process that the runtime never reported as ended counts as
        // terminated by the stop.
        process->output_done = true;
        if (process->state != PP_PROCESS_EXITED) {
            (void)pp_log(PP_LOG_WARNING, "The Wine runtime stopped without reporting the end of "
                                         "its process.");
            process->state = PP_PROCESS_EXITED;
            process->exit_code = 1U;
        }
        report_exit_when_ready();
    }
    report_session_ended(PP_OK);
    (void)pthread_cond_broadcast(&s_session.changed);
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (state == PP_SESSION_RUNNING) {
        (void)pp_log(PP_LOG_INFO, "The Wine session stopped.");
    }
    return PP_OK;
}

// --- The process ------------------------------------------------------------------------------

// Opens a pipe whose ends do not reach a program that the host starts.
static bool open_pipe(int ends[2]) {
    if (pipe(ends) != 0) {
        return false;
    }
    if (fcntl(ends[0], F_SETFD, FD_CLOEXEC) != 0 || fcntl(ends[1], F_SETFD, FD_CLOEXEC) != 0) {
        (void)close(ends[0]);
        (void)close(ends[1]);
        return false;
    }
    return true;
}

static void close_if_open(int fd) {
    if (fd >= 0) {
        (void)close(fd);
    }
}

// Waits until the process left its start, or until the wait ends. The caller holds the mutex.
static void wait_for_start(void) {
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    until.tv_sec += PP_LAUNCH_WAIT_SECONDS;
    while (s_session.process.state == PP_PROCESS_STARTING) {
        if (pthread_cond_timedwait(&s_session.changed, &s_session.mutex, &until) == ETIMEDOUT) {
            break;
        }
    }
}

// Marks the process as one that never started: the runtime refused it. without_reader says that
// no reader of its output runs, so the exit event need not wait for one.
static void refuse_process(uint64_t handle, bool without_reader) {
    if (without_reader) {
        (void)pthread_mutex_lock(&s_session.mutex);
        s_session.process.output_done = true;
        (void)pthread_mutex_unlock(&s_session.mutex);
    }
    bridge_process_exited(handle, 1U);
}

PP_EXPORT pp_status_t pp_process_launch(const pp_process_config_t *config, uint64_t *process) {
    pp_error_clear();
    if (config == NULL || process == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_process_launch needs a configuration and an output.");
    }
    if (config->struct_size < sizeof(pp_process_config_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_process_launch needs a struct_size of at least %zu, not %u.",
                            sizeof(pp_process_config_t), (unsigned)config->struct_size);
    }
    if (config->capture_output > 1U) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_process_launch needs 0 or 1 in "
                                                   "capture_output.");
    }
    if (config->program == NULL || config->program[0] == '\0' ||
        strnlen(config->program, PATH_MAX) >= PATH_MAX) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_process_launch needs a program shorter than %d bytes.", PATH_MAX);
    }
    pp_status_t status = PP_OK;
    if (config->program_folder != NULL) {
        status = check_folder("pp_process_launch", "program_folder", config->program_folder);
    }
    if (status == PP_OK) {
        status = check_block("pp_process_launch", "arguments", config->arguments, PP_ARGUMENTS_MAX,
                             false);
    }
    if (status != PP_OK) {
        return status;
    }

    (void)pthread_mutex_lock(&s_session.mutex);
    if (s_session.state != PP_SESSION_RUNNING) {
        (void)pthread_mutex_unlock(&s_session.mutex);
        return pp_error_set(PP_E_NO_SESSION, "pp_process_launch needs a running session.");
    }
    if (s_session.process.handle != 0U) {
        (void)pthread_mutex_unlock(&s_session.mutex);
        return pp_error_set(PP_E_UNSUPPORTED,
                            "A session runs one process in this version of the host.");
    }
    // The session's one process gets handle 1; a later version numbers more.
    const uint64_t handle = 1U;
    s_session.process.handle = handle;
    s_session.process.state = PP_PROCESS_STARTING;
    s_session.process.exit_code = 0U;
    // Without a reader there is no output to wait for before the exit event.
    s_session.process.output_done = config->capture_output == 0U;
    s_session.process.exit_posted = false;
    const wine_paneport_process_launch_t launch = s_session.launch;
    (void)pthread_mutex_unlock(&s_session.mutex);
    *process = handle;

    int output_pipe[2] = {-1, -1};
    int errors_pipe[2] = {-1, -1};
    if (config->capture_output == 1U) {
        if (!open_pipe(output_pipe)) {
            refuse_process(handle, true);
            return pp_error_set(PP_E_SYSTEM, "The pipes for the program's output could not open.");
        }
        if (!open_pipe(errors_pipe)) {
            close_if_open(output_pipe[0]);
            close_if_open(output_pipe[1]);
            refuse_process(handle, true);
            return pp_error_set(PP_E_SYSTEM, "The pipes for the program's output could not open.");
        }
        pp_guest_output_t *output = NULL;
        status =
            pp_guest_output_start(output_pipe[0], errors_pipe[0], handle, output_ended, &output);
        if (status != PP_OK) {
            close_if_open(output_pipe[1]);
            close_if_open(errors_pipe[1]);
            refuse_process(handle, true);
            return pp_error_set(status, "The reader of the program's output could not start.");
        }
        (void)pthread_mutex_lock(&s_session.mutex);
        s_session.process.output = output;
        (void)pthread_mutex_unlock(&s_session.mutex);
    }

    const struct wine_paneport_launch request = {
        .struct_size = (uint32_t)sizeof(struct wine_paneport_launch),
        .stdin_fd = -1,
        .stdout_fd = output_pipe[1],
        .stderr_fd = errors_pipe[1],
        .process = handle,
        .program_folder = config->program_folder,
        .program = config->program,
        .arguments = config->arguments,
    };
    const int32_t launched = launch(&request);
    // The runtime duplicated what it keeps; the reader sees the end once the process closes them.
    close_if_open(output_pipe[1]);
    close_if_open(errors_pipe[1]);
    if (launched != PP_WINE_OK) {
        // The reader, if any, sees the end of both pipes now and ends by itself.
        refuse_process(handle, false);
        return pp_error_set(launched < 0 ? launched : PP_E_SYSTEM,
                            "The Wine runtime refused to launch the program (%d); its log says "
                            "why.",
                            launched);
    }

    (void)pthread_mutex_lock(&s_session.mutex);
    wait_for_start();
    const uint32_t state = s_session.process.state;
    const uint32_t exit_code = s_session.process.exit_code;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (state == PP_PROCESS_STARTING) {
        return pp_error_set(PP_E_SYSTEM, "The program did not finish its start within %d s.",
                            PP_LAUNCH_WAIT_SECONDS);
    }
    if (state == PP_PROCESS_EXITED) {
        return pp_error_set(PP_E_SYSTEM,
                            "The program ended before it finished its start, with exit code "
                            "0x%x.",
                            (unsigned)exit_code);
    }
#ifdef PANEPORT_TEST_BUILD
    // The process's start set up Wine's signal handling, so test builds check the dispatcher
    // again, and end the process when its handlers were replaced (C-0005 section 4).
    status = pp_signals_check_installed(NULL);
    if (status != PP_OK) {
        (void)pp_log(PP_LOG_ERROR,
                     "The Windows process failed the check of the signal dispatcher.");
        (void)pp_process_terminate(handle, 1U);
        return pp_error_set(PP_E_SYSTEM, "The process's start replaced a handler of the signal "
                                         "dispatcher, so it was terminated.");
    }
#endif
    return PP_OK;
}

PP_EXPORT pp_status_t pp_process_get_state(uint64_t process, pp_process_state_t *state) {
    pp_error_clear();
    if (state == NULL || state->struct_size < 2U * sizeof(uint32_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_process_get_state needs a state with a struct_size of at least 8.");
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    const bool known = process != 0U && process == s_session.process.handle;
    const uint32_t current = s_session.process.state;
    const uint32_t exit_code = current == PP_PROCESS_EXITED ? s_session.process.exit_code : 0U;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (!known) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "The handle names no process of this launch.");
    }
    state->state = current;
    if (state->struct_size >= offsetof(pp_process_state_t, exit_code) + sizeof state->exit_code) {
        state->exit_code = exit_code;
    }
    return PP_OK;
}

PP_EXPORT pp_status_t pp_process_terminate(uint64_t process, uint32_t exit_code) {
    pp_error_clear();
    (void)pthread_mutex_lock(&s_session.mutex);
    const bool known = process != 0U && process == s_session.process.handle;
    const bool exited = s_session.process.state == PP_PROCESS_EXITED;
    const wine_paneport_process_terminate_t terminate = s_session.terminate;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (!known) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "The handle names no process of this launch.");
    }
    if (exited || terminate == NULL) {
        return PP_OK;
    }
    const int32_t status = terminate(process, exit_code);
    if (status != PP_WINE_OK) {
        return pp_error_set(PP_E_SYSTEM, "The Wine runtime refused to terminate the process (%d).",
                            status);
    }
    return PP_OK;
}
