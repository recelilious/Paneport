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
#include <signal.h>
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
_Static_assert(PP_WINE_LOG_DEBUG == PP_LOG_DEBUG, "The bridge's log levels differ from PP_LOG_.");
_Static_assert(PP_WINE_LOG_ERROR == PP_LOG_ERROR, "The bridge's log levels differ from PP_LOG_.");

// The states that the fork's query reports are those of paneport/session.h.
_Static_assert(WINE_PANEPORT_PROCESS_STARTING == PP_PROCESS_STARTING, "A process state differs.");
_Static_assert(WINE_PANEPORT_PROCESS_RUNNING == PP_PROCESS_RUNNING, "A process state differs.");
_Static_assert(WINE_PANEPORT_PROCESS_EXITED == PP_PROCESS_EXITED, "A process state differs.");

// The life of the launch's one session.
enum {
    PP_SESSION_NONE = 0,
    PP_SESSION_STARTING = 1,
    PP_SESSION_RUNNING = 2,
    PP_SESSION_STOPPING = 3,
    PP_SESSION_ENDED = 4,
};

// How many threads the fork may have the host create for a session, and their stack size, that
// of a main thread (paneport/wine_bridge.h).
#define PP_SESSION_THREADS 4U
#define PP_SESSION_THREAD_STACK ((size_t)8U * 1024U * 1024U)

// The limits of the blocks of environment variables and arguments.
#define PP_ENVIRONMENT_MAX ((size_t)64U * 1024U)
#define PP_ARGUMENTS_MAX ((size_t)32U * 1024U)

// How long pp_process_launch waits for the process to finish its start, and pp_session_stop for
// the runtime to report the end of the process that it terminated and for the reader of the
// process's output to reach the end of its pipes.
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

// The session's process. Its handle is 0 until pp_process_launch starts one. started says that
// the runtime reported its start, which a quick program may follow with its end before the
// launch looks. Its exit event waits until the reader of its output queued the last line
// (output_done), so that the lines arrive before it (paneport/session.h), and while the runtime's
// launch runs (launching), so that a refused launch decides its exit code. start_awaited says
// that pp_process_launch still waits for the start, from its call into the runtime until its
// wait ends. start_check_failed says that the check of the test builds found a handler of the
// dispatcher replaced when the runtime reported the start, and replaced_signal names the first
// such signal.
typedef struct session_process_t {
    uint64_t handle;
    uint32_t state;
    uint32_t exit_code;
    pp_guest_output_t *output;
    int32_t replaced_signal;
    bool started;
    bool start_awaited;
    bool start_check_failed;
    bool launching;
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
    wine_paneport_process_query_t query;
    session_thread_t threads[PP_SESSION_THREADS];
    uint32_t thread_count;
    uint32_t threads_running;
    bool ended_reported;
    bool end_pending;
    pp_status_t end_status;
    // A failure of the running session, which its end reports instead of PP_OK.
    pp_status_t failure;
    session_process_t process;
} s_session = {
    .mutex = PTHREAD_MUTEX_INITIALIZER,
    .changed = PTHREAD_COND_INITIALIZER,
};

#ifdef PANEPORT_TEST_BUILD
// The hooks and the waits of the test builds, guarded by the session's mutex.
static pp_session_hook_t s_started_hook;
static void *s_started_hook_argument;
static pp_session_hook_t s_launch_hook;
static void *s_launch_hook_argument;
static uint32_t s_stop_wait_milliseconds;
static uint32_t s_launch_wait_milliseconds;

void pp_session_set_started_hook(pp_session_hook_t hook, void *argument) {
    (void)pthread_mutex_lock(&s_session.mutex);
    s_started_hook = hook;
    s_started_hook_argument = argument;
    (void)pthread_mutex_unlock(&s_session.mutex);
}

void pp_session_set_launch_hook(pp_session_hook_t hook, void *argument) {
    (void)pthread_mutex_lock(&s_session.mutex);
    s_launch_hook = hook;
    s_launch_hook_argument = argument;
    (void)pthread_mutex_unlock(&s_session.mutex);
}

void pp_session_set_stop_wait(uint32_t milliseconds) {
    (void)pthread_mutex_lock(&s_session.mutex);
    s_stop_wait_milliseconds = milliseconds;
    (void)pthread_mutex_unlock(&s_session.mutex);
}

void pp_session_set_launch_wait(uint32_t milliseconds) {
    (void)pthread_mutex_lock(&s_session.mutex);
    s_launch_wait_milliseconds = milliseconds;
    (void)pthread_mutex_unlock(&s_session.mutex);
}
#endif

// Returns how long pp_session_stop waits, in milliseconds. The caller holds the mutex.
static uint64_t stop_wait_milliseconds(void) {
#ifdef PANEPORT_TEST_BUILD
    if (s_stop_wait_milliseconds != 0U) {
        return s_stop_wait_milliseconds;
    }
#endif
    return (uint64_t)PP_STOP_WAIT_SECONDS * 1000U;
}

// Returns how long pp_process_launch waits for the process's start, in milliseconds. The caller
// holds the mutex.
static uint64_t launch_wait_milliseconds(void) {
#ifdef PANEPORT_TEST_BUILD
    if (s_launch_wait_milliseconds != 0U) {
        return s_launch_wait_milliseconds;
    }
#endif
    return (uint64_t)PP_LAUNCH_WAIT_SECONDS * 1000U;
}

// Returns when a wait of the given milliseconds that starts now ends, for
// pthread_cond_timedwait.
static struct timespec deadline_after(uint64_t milliseconds) {
    struct timespec until;
    (void)clock_gettime(CLOCK_REALTIME, &until);
    const uint64_t nanoseconds = (uint64_t)until.tv_nsec + ((milliseconds % 1000U) * 1000000U);
    until.tv_sec += (time_t)(milliseconds / 1000U) + (time_t)(nanoseconds / 1000000000U);
    until.tv_nsec = (long)(nanoseconds % 1000000000U);
    return until;
}

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

// Reports the end of the session once, after the process's exit event, with the failure of the
// running session in place of PP_OK. The caller holds the mutex.
static void report_session_ended(pp_status_t status) {
    if (s_session.ended_reported) {
        return;
    }
    if (status == PP_OK) {
        status = s_session.failure;
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

// Reports the process's exit once it ended, its output reader queued its last line and the
// runtime's launch returned, then a session end that waited for it. The caller holds the mutex.
static void report_exit_when_ready(void) {
    session_process_t *process = &s_session.process;
    if (process->handle == 0U || process->exit_posted || process->launching ||
        process->state != PP_PROCESS_EXITED || !process->output_done) {
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
        return PP_WINE_E_INVALID_ARGUMENT;
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    int32_t status = PP_WINE_OK;
    if (s_session.state != PP_SESSION_STARTING && s_session.state != PP_SESSION_RUNNING) {
        // A session that stops or ended gets no more threads; the bridge's codes have no other
        // for it.
        status = PP_WINE_E_SYSTEM;
    } else if (s_session.thread_count == PP_SESSION_THREADS) {
        status = PP_WINE_E_BUSY;
    } else {
        session_thread_t *slot = &s_session.threads[s_session.thread_count];
        slot->body = body;
        slot->argument = argument;
        slot->joined = false;
        (void)snprintf(slot->name, sizeof slot->name, "%s", name);
        s_session.threads_running++;
        // The wineserver runs on such a thread, which upstream runs on a process's main thread.
        pthread_attr_t attributes;
        int created = pthread_attr_init(&attributes);
        if (created == 0) {
            created = pthread_attr_setstacksize(&attributes, PP_SESSION_THREAD_STACK);
            if (created == 0) {
                created = pthread_create(&slot->thread, &attributes, run_session_thread, slot);
            }
            (void)pthread_attr_destroy(&attributes);
        }
        if (created != 0) {
            s_session.threads_running--;
            status = PP_WINE_E_SYSTEM;
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

// Whether a handle names the session's process. The caller holds the mutex.
static bool names_the_process(uint64_t process) {
    if (process == 0U) {
        return false;
    }
    return process == s_session.process.handle;
}

// Checks in test builds that the dispatcher still holds its six signals, and returns whether it
// does; replaced receives the first signal whose handler was replaced, or 0. Always true in the
// shipped builds.
static bool dispatcher_kept(int32_t *replaced) {
    *replaced = 0;
#ifdef PANEPORT_TEST_BUILD
    return pp_signals_check_installed(replaced) == PP_OK;
#else
    return true;
#endif
}

// Ends a process whose start failed the check after pp_process_launch had stopped waiting for
// it, so that no launch fails for it: the error names the signal, and the process ends with exit
// code 1. The runtime's terminate only posts a request to its server's thread, which then ends
// the process. Called without the mutex.
static void end_unawaited_process(uint64_t process, int32_t replaced,
                                  wine_paneport_process_terminate_t terminate) {
    (void)pp_log(PP_LOG_ERROR,
                 "The Windows process's start, which the Wine runtime reported after "
                 "pp_process_launch had stopped waiting for it, replaced the handler of %s of the "
                 "signal dispatcher, so the session fails and the process ends with exit code 1.",
                 pp_signals_name(replaced));
    if (terminate != NULL && terminate(process, 1U) != PP_WINE_OK) {
        (void)pp_log(PP_LOG_ERROR, "The Wine runtime refused to terminate the process.");
    }
}

static void bridge_process_started(uint64_t process) {
    // The process's first thread reports its start here, a guest thread that the runtime's
    // server knows. The fork's thread control interrupts such a thread through SIGUSR2 and waits
    // while the thread has it blocked (docs/design/single-process-wine.md section 7), so the
    // report blocks it while it holds the mutex, posts and asks the runtime to end the process:
    // an interruption, also the one that its own request brings, comes once it holds no lock.
    sigset_t interruption;
    sigset_t previous;
    (void)sigemptyset(&interruption);
    (void)sigaddset(&interruption, SIGUSR2);
    (void)pthread_sigmask(SIG_BLOCK, &interruption, &previous);

    // In single mode ntdll sets up Wine's signal handling on the process's first thread, so test
    // builds check the dispatcher again here (C-0005 section 4). A failure is recorded before the
    // start is reported, while the process cannot have ended, so that the session's end carries
    // it. A launch that still waits for the start fails for it and ends the process; once no
    // launch waits, the report ends the process itself.
    int32_t replaced = 0;
    const bool kept = dispatcher_kept(&replaced);
    bool end_here = false;
    (void)pthread_mutex_lock(&s_session.mutex);
    const wine_paneport_process_terminate_t terminate = s_session.terminate;
    if (names_the_process(process) && s_session.process.state == PP_PROCESS_STARTING) {
        if (!kept) {
            s_session.failure = PP_E_SYSTEM;
            s_session.process.start_check_failed = true;
            s_session.process.replaced_signal = replaced;
            if (!s_session.process.start_awaited) {
                end_here = true;
            }
        }
        s_session.process.state = PP_PROCESS_RUNNING;
        s_session.process.started = true;
        post_event(PP_EVENT_PROCESS_STARTED, process, 0);
        (void)pthread_cond_broadcast(&s_session.changed);
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (end_here) {
        end_unawaited_process(process, replaced, terminate);
    }
    (void)pthread_sigmask(SIG_SETMASK, &previous, NULL);
}

static void bridge_process_exited(uint64_t process, uint32_t exit_code) {
    (void)pthread_mutex_lock(&s_session.mutex);
    if (names_the_process(process) && s_session.process.state != PP_PROCESS_EXITED) {
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
    if ((character >= 'A' && character <= 'Z') || character == '_') {
        return true;
    }
    if (first || character < '0' || character > '9') {
        return false;
    }
    return true;
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
        bool joinable = false;
        const pthread_t thread = s_session.threads[index].thread;
        if (index < s_session.thread_count && !s_session.threads[index].joined) {
            joinable = true;
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

// Copies an entry point of the runtime into *entry, a function pointer of size bytes, and
// returns whether the runtime has it: ISO C has no cast between a data pointer and a function
// pointer.
static bool find_entry(void *library, const char *name, void *entry, size_t size) {
    void *symbol = dlsym(library, name);
    if (symbol == NULL) {
        (void)pp_error_set(PP_E_IO, "The Wine runtime has no entry point %s.", name);
        return false;
    }
    memcpy(entry, (const void *)&symbol, size);
    return true;
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
    wine_paneport_session_start_t start = NULL;
    wine_paneport_session_stop_t stop = NULL;
    wine_paneport_process_launch_t launch = NULL;
    wine_paneport_process_terminate_t terminate = NULL;
    wine_paneport_process_query_t query = NULL;
    if (!find_entry(library, WINE_PANEPORT_SESSION_START, (void *)&start, sizeof start) ||
        !find_entry(library, WINE_PANEPORT_SESSION_STOP, (void *)&stop, sizeof stop) ||
        !find_entry(library, WINE_PANEPORT_PROCESS_LAUNCH, (void *)&launch, sizeof launch) ||
        !find_entry(library, WINE_PANEPORT_PROCESS_TERMINATE, (void *)&terminate,
                    sizeof terminate) ||
        !find_entry(library, WINE_PANEPORT_PROCESS_QUERY, (void *)&query, sizeof query) ||
        start == NULL) {
        return fail_started_session(PP_E_IO);
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.library = library;
    s_session.stop = stop;
    s_session.launch = launch;
    s_session.terminate = terminate;
    s_session.query = query;
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

bool pp_session_is_active(void) {
    (void)pthread_mutex_lock(&s_session.mutex);
    const int state = s_session.state;
    // A launch still inside the runtime posts the process's end and the session's once it
    // returns, also after the session's stop.
    const bool launching = s_session.process.launching;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (launching) {
        return true;
    }
    switch (state) {
    case PP_SESSION_STARTING:
    case PP_SESSION_RUNNING:
    case PP_SESSION_STOPPING:
        return true;
    default:
        return false;
    }
}

// Ends a session that its stop ended or that ended by itself: joins the threads that the host
// created for it, waits a while for the runtime's report of the process's end, for the end of
// the process's output and for a launch still inside the runtime, stops a reader whose pipes stay
// open, and reports the ends that were still to come; a launch that is still inside the runtime
// once the wait is over reports them when it returns. output is the process's reader, which the
// caller took, or NULL. The main thread.
static void finish_session(pp_guest_output_t *output) {
    join_session_threads();

    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.state = PP_SESSION_ENDED;
    session_process_t *process = &s_session.process;
    // The runtime reports the end of the process that the stop terminated, and the reader of its
    // output ends once the runtime closed its copies of the pipes' write ends, with the process;
    // a launch still inside the runtime holds the exit event until it returns. Wait a while for
    // all three.
    const struct timespec until = deadline_after(stop_wait_milliseconds());
    while (process->handle != 0U && (process->state != PP_PROCESS_EXITED || process->launching ||
                                     (output != NULL && !process->output_done))) {
        if (pthread_cond_timedwait(&s_session.changed, &s_session.mutex, &until) == ETIMEDOUT) {
            break;
        }
    }
    bool reader_waits = false;
    if (output != NULL && !process->output_done) {
        reader_waits = true;
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (reader_waits) {
        // A copy of a write end stays open in the runtime: the reader takes what the pipes hold
        // and ends, so that the join below is bounded too.
        (void)pp_log(PP_LOG_WARNING, "The output of the Wine runtime's process did not end with "
                                     "the session, so its reader was stopped.");
        pp_guest_output_wake(output);
    }
    pp_guest_output_join(output);

    (void)pthread_mutex_lock(&s_session.mutex);
    if (process->handle != 0U && !process->exit_posted) {
        // The reader has ended; a process that the runtime never reported as ended counts as one
        // that the stop terminated.
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
    finish_session(output);
    if (state == PP_SESSION_RUNNING) {
        (void)pp_log(PP_LOG_INFO, "The Wine session stopped.");
    }
    return PP_OK;
}

void pp_session_collect(void) {
    (void)pthread_mutex_lock(&s_session.mutex);
    const bool ended = s_session.state == PP_SESSION_ENDED;
    pp_guest_output_t *output = NULL;
    if (ended) {
        output = s_session.process.output;
        s_session.process.output = NULL;
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
    // After pp_session_stop, finish_session finds nothing left to do.
    if (ended) {
        finish_session(output);
    }
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
    const struct timespec until = deadline_after(launch_wait_milliseconds());
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

// Opens the pipes of a launch that captures the program's output and starts their reader, which
// takes their read ends. On a failure it marks the process as refused and returns the error.
static pp_status_t start_capture(uint64_t handle, int output_pipe[2], int errors_pipe[2],
                                 pp_guest_output_t **output) {
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
    const pp_status_t status =
        pp_guest_output_start(output_pipe[0], errors_pipe[0], handle, output_ended, output);
    if (status != PP_OK) {
        close_if_open(output_pipe[1]);
        close_if_open(errors_pipe[1]);
        refuse_process(handle, true);
        return pp_error_set(status, "The reader of the program's output could not start.");
    }
    return PP_OK;
}

// Runs the launch hook of the test builds. Nothing in the shipped builds.
static void run_launch_hook(void) {
#ifdef PANEPORT_TEST_BUILD
    (void)pthread_mutex_lock(&s_session.mutex);
    const pp_session_hook_t hook = s_launch_hook;
    void *const hook_argument = s_launch_hook_argument;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (hook != NULL) {
        hook(hook_argument);
    }
#endif
}

// Hands the launch's reader, or none, to pp_session_stop while the session still runs, and
// returns whether it does. The stop may have begun while the pipes and the reader were set up,
// and then it took no reader: the launch ends its process itself, before the runtime hears of it,
// so that no reader outlives the stop and no program starts in a session that stopped.
static bool keep_reader(pp_guest_output_t *output) {
    (void)pthread_mutex_lock(&s_session.mutex);
    const bool running = s_session.state == PP_SESSION_RUNNING;
    if (running) {
        s_session.process.output = output;
    }
    (void)pthread_mutex_unlock(&s_session.mutex);
    return running;
}

// Fails a launch whose process's start replaced a handler of the dispatcher, which the check of
// the test builds found when the runtime reported the start and which failed the session then:
// the process ends with exit code 1, unless it had already ended with its own.
static pp_status_t fail_start_check(uint64_t handle, int32_t replaced) {
    (void)pp_log(PP_LOG_ERROR, "The Windows process failed the check of the signal dispatcher.");
    // A process that has already ended is not terminated again (pp_process_terminate).
    (void)pp_process_terminate(handle, 1U);
    return pp_error_set(PP_E_SYSTEM,
                        "The process's start replaced the handler of %s of the signal dispatcher, "
                        "so the session fails; the process ends with exit code 1, unless it had "
                        "already ended with its own.",
                        pp_signals_name(replaced));
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
    s_session.process.replaced_signal = 0;
    s_session.process.started = false;
    s_session.process.start_awaited = false;
    s_session.process.start_check_failed = false;
    s_session.process.launching = false;
    // Without a reader there is no output to wait for before the exit event.
    s_session.process.output_done = config->capture_output == 0U;
    s_session.process.exit_posted = false;
    const wine_paneport_process_launch_t launch = s_session.launch;
    (void)pthread_mutex_unlock(&s_session.mutex);
    *process = handle;

    int output_pipe[2] = {-1, -1};
    int errors_pipe[2] = {-1, -1};
    pp_guest_output_t *output = NULL;
    if (config->capture_output == 1U) {
        status = start_capture(handle, output_pipe, errors_pipe, &output);
        if (status != PP_OK) {
            return status;
        }
    }
    run_launch_hook();
    if (!keep_reader(output)) {
        close_if_open(output_pipe[1]);
        close_if_open(errors_pipe[1]);
        // The reader sees the end of both pipes at once, since nothing else holds their ends.
        pp_guest_output_join(output);
        refuse_process(handle, false);
        return pp_error_set(PP_E_NO_SESSION,
                            "The session stopped while pp_process_launch started the program.");
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
    // The exit event waits while the runtime's launch runs. A runtime that refuses the launch may
    // report an end of the process first, with an exit code of its own, as the fork's server does
    // when the process's first thread could not be created; the refused process counts as ended
    // with exit code 1 whatever it reported (paneport/session.h). From here until its wait for the
    // start ends, the launch fails for a start that fails the check.
    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.process.launching = true;
    s_session.process.start_awaited = true;
    (void)pthread_mutex_unlock(&s_session.mutex);
    const int32_t launched = launch(&request);
    // The runtime duplicated what it keeps; the reader sees the end once the process closes them.
    close_if_open(output_pipe[1]);
    close_if_open(errors_pipe[1]);
    (void)pthread_mutex_lock(&s_session.mutex);
    s_session.process.launching = false;
    if (launched != PP_WINE_OK) {
        s_session.process.start_awaited = false;
        s_session.process.state = PP_PROCESS_EXITED;
        s_session.process.exit_code = 1U;
    }
    report_exit_when_ready();
    (void)pthread_cond_broadcast(&s_session.changed);
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (launched != PP_WINE_OK) {
        // The reader, if any, sees the end of both pipes now and ends by itself.
        return pp_error_set(launched < 0 ? launched : PP_E_SYSTEM,
                            "The Wine runtime refused to launch the program (%d); its log says "
                            "why.",
                            launched);
    }

    (void)pthread_mutex_lock(&s_session.mutex);
    wait_for_start();
    // A start reported from now on ends its process itself when it fails the check.
    s_session.process.start_awaited = false;
    const uint64_t waited = launch_wait_milliseconds();
    const uint32_t state = s_session.process.state;
    const bool started = s_session.process.started;
    const uint32_t exit_code = s_session.process.exit_code;
    const bool start_check_failed = s_session.process.start_check_failed;
    const int32_t replaced = s_session.process.replaced_signal;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (state == PP_PROCESS_STARTING) {
        return pp_error_set(PP_E_SYSTEM, "The program did not finish its start within %llu ms.",
                            (unsigned long long)waited);
    }
    // A program that started may have ended already; only one that never started failed.
    if (!started) {
        return pp_error_set(PP_E_SYSTEM,
                            "The program ended before it finished its start, with exit code "
                            "0x%x.",
                            (unsigned)exit_code);
    }
    if (start_check_failed) {
        return fail_start_check(handle, replaced);
    }
    return PP_OK;
}

PP_EXPORT pp_status_t pp_process_get_state(uint64_t process, pp_process_state_t *state) {
    pp_error_clear();
    if (state == NULL || state->struct_size < 2U * sizeof(uint32_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_process_get_state needs a state with a struct_size of at least 8.");
    }
    (void)pthread_mutex_lock(&s_session.mutex);
    const bool known = names_the_process(process);
    uint32_t current = s_session.process.state;
    uint32_t exit_code = current == PP_PROCESS_EXITED ? s_session.process.exit_code : 0U;
    // Until the host recorded the process's end, the runtime may know more, such as an end whose
    // report is still on its way; once the host recorded it, the record answers.
    const wine_paneport_process_query_t query =
        current != PP_PROCESS_EXITED ? s_session.query : NULL;
    (void)pthread_mutex_unlock(&s_session.mutex);
    if (!known) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "The handle names no process of this launch.");
    }
    if (query != NULL) {
        struct wine_paneport_process_state reported = {
            .struct_size = (uint32_t)sizeof(struct wine_paneport_process_state),
        };
        // The runtime answers only for a process that its server knows.
        if (query(process, &reported) == PP_WINE_OK && reported.state >= PP_PROCESS_STARTING &&
            reported.state <= PP_PROCESS_EXITED) {
            current = reported.state;
            exit_code = current == PP_PROCESS_EXITED ? reported.exit_code : 0U;
        }
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
    const bool known = names_the_process(process);
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
