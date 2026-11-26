// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The session and process groups of the host interface (C-0005 section 2): starting and
 * stopping the launch's one Wine session, and launching, following and terminating the Windows
 * program that it runs. Version 1.4 of the interface added them.
 *
 * A launch of the app runs at most one session, and a session that ended cannot start again:
 * switching games needs a relaunch (C-0004 section 1). In this version a session runs one
 * Windows process, the first that pp_process_launch starts; when that process ends, the session
 * ends too. A later stage lets a session run more than one.
 *
 * Events: PP_EVENT_PROCESS_STARTED and PP_EVENT_PROCESS_EXITED name the process in the
 * event's handle, and PP_EVENT_PROCESS_EXITED carries its exit code in code;
 * PP_EVENT_SESSION_ENDED comes once, after the session's last thread ended, with 0 in code, or
 * the negative status that ended it. Events arrive only on the host event thread.
 *
 * Output: a process launched with capture_output set writes its standard output and standard
 * error into pipes that the host reads. Each line reaches the log callback as a pp_log_line_t
 * of the source PP_LOG_SOURCE_GUEST_OUTPUT or PP_LOG_SOURCE_GUEST_ERRORS, with the process's
 * handle, at the level PP_LOG_INFO, without its line break; a line longer than a log line holds
 * arrives as several. The runtime's own diagnostics arrive with the source
 * PP_LOG_SOURCE_RUNTIME.
 */

#ifndef PP_SESSION_H
#define PP_SESSION_H

#include <stdint.h>

#include "paneport/common.h"

PP_EXTERN_C_BEGIN

/** The status codes of the session and process groups. */
enum {
    /** pp_session_start needs the JIT state ready (C-0006 sections 3 and 5). */
    PP_E_JIT_NOT_READY = -11,
    /** A session has already run in this process; another needs a relaunch (C-0004 section 1). */
    PP_E_SESSION_EXISTS = -12,
    /** The call needs a running session. */
    PP_E_NO_SESSION = -13,
};

/** The architecture of the Windows programs that a session runs. */
enum {
    /** 64-bit x86 programs, translated by the FEX fork's ARM64EC module. */
    PP_ARCHITECTURE_X86_64 = 1,
    /** 32-bit x86 programs. Refused with PP_E_UNSUPPORTED until 32-bit guests are built. */
    PP_ARCHITECTURE_I686 = 2,
};

/** The kinds of event that the session and process groups report, in pp_event_t's kind. */
enum {
    /** The session ended: code is 0, or the negative status that ended it. */
    PP_EVENT_SESSION_ENDED = 2,
    /** A process finished its start and runs the program: handle names it. */
    PP_EVENT_PROCESS_STARTED = 3,
    /** A process ended, or failed to start: handle names it, code is its exit code. */
    PP_EVENT_PROCESS_EXITED = 4,
};

/** The states of a process, in pp_process_state_t. */
enum {
    /** pp_process_launch is starting it; the program has not run yet. */
    PP_PROCESS_STARTING = 1,
    /** The program runs. */
    PP_PROCESS_RUNNING = 2,
    /** The process ended, and exit_code holds its exit code. */
    PP_PROCESS_EXITED = 3,
};

/** The settings of pp_session_start. */
typedef struct pp_session_config_t {
    /** sizeof(pp_session_config_t) as the caller knows it. Members past it count as unset. */
    uint32_t struct_size;
    /** One of the PP_ARCHITECTURE_ values. */
    uint32_t architecture;
    /**
     * The absolute path of the Wine prefix's folder, UTF-8 and NUL-terminated: an existing
     * folder made from the template prefix (plan section 7.4), such as one in
     * Library/Application Support/Paneport/prefixes/. The host copies it.
     */
    const char *prefix_folder;
    /**
     * The absolute path of the folder that holds the Wine fork's runtime as its build installs
     * it, with lib/wine/aarch64-unix/ntdll.so and the folders of the Windows modules beside it.
     * The host copies it. The app's bundle layout of the runtime comes with the iOS build.
     */
    const char *runtime_folder;
    /**
     * The session's environment variables, as a block of "NAME=value" strings, each
     * NUL-terminated, ending with an empty string, UTF-8; or NULL for none. A name holds
     * capital letters, digits and underscores, and does not start with a digit. The host copies
     * the block. At most 64 KiB.
     */
    const char *environment;
} pp_session_config_t;

/** The settings of pp_process_launch. */
typedef struct pp_process_config_t {
    /** sizeof(pp_process_config_t) as the caller knows it. Members past it count as unset. */
    uint32_t struct_size;
    /**
     * 1 to capture the program's standard output and standard error and deliver them, line by
     * line, to the log callback; 0 to discard both.
     */
    uint8_t capture_output;
    /**
     * The absolute path of the host folder that holds the program, UTF-8 and NUL-terminated,
     * which the session maps to a drive letter of its own for this process; or NULL when
     * program is a Windows path on a drive of the prefix. The host copies it.
     */
    const char *program_folder;
    /**
     * The program, UTF-8 and NUL-terminated: a path relative to program_folder, with forward or
     * backward slashes, such as "console.exe"; or, when program_folder is NULL, an absolute
     * Windows path, such as "C:\\tools\\console.exe". The host copies it.
     */
    const char *program;
    /**
     * The program's arguments, as a block of strings, each NUL-terminated, ending with an empty
     * string, UTF-8; or NULL for none. The host copies the block. At most 32 KiB.
     */
    const char *arguments;
} pp_process_config_t;

/** The state of a process, which pp_process_get_state reports. */
typedef struct pp_process_state_t {
    /** sizeof(pp_process_state_t) as the caller knows it. Set by the caller. */
    uint32_t struct_size;
    /** One of the PP_PROCESS_ values. */
    uint32_t state;
    /** The exit code once state is PP_PROCESS_EXITED, else 0. */
    uint32_t exit_code;
} pp_process_state_t;

/**
 * Starts the launch's Wine session: loads the Wine fork's runtime from the runtime folder,
 * passes it the Wine bridge (C-0005 section 3) and lets it start its wineserver on a thread of
 * the host. Test builds then check that the signal dispatcher still holds its six signals and
 * fail the session otherwise (C-0005 section 4).
 *
 * Thread rule: [main]. Memory: the host copies what it keeps; the caller keeps ownership of
 * config.
 *
 * A session counts as having run once the checks of the arguments passed and the host begins to
 * load the runtime: a start that fails from there on, also one whose runtime cannot be loaded,
 * ends the session, with the session-ended event carrying the start's status, and another start
 * needs a relaunch.
 *
 * @param config The settings. Must not be NULL, and struct_size must cover every member that
 *               this version declares.
 * @return PP_OK; PP_E_NOT_INITIALIZED; PP_E_WRONG_THREAD; PP_E_JIT_NOT_READY unless the JIT
 *         state is ready; PP_E_SESSION_EXISTS when a session has already run in this process,
 *         also one whose start failed; PP_E_INVALID_ARGUMENT for a missing or malformed member,
 *         a path that is relative, too long or not a folder, a runtime folder without the
 *         runtime's library, or an environment block that is malformed or too long;
 *         PP_E_UNSUPPORTED for an architecture that this build does not run; PP_E_IO when the
 *         runtime cannot be loaded or lacks one of its entry points; when the runtime refuses to
 *         start the session, its own negative status, a PP_WINE_E_ code of
 *         paneport/wine_bridge.h, whose values are those of the host, such as PP_E_IO for a
 *         prefix that it cannot prepare or PP_E_OUT_OF_MEMORY, and PP_E_SYSTEM for any other
 *         answer; in test builds PP_E_SYSTEM when a signal handler of the dispatcher was
 *         replaced.
 */
PP_EXPORT pp_status_t pp_session_start(const pp_session_config_t *config);

/**
 * Stops the session: terminates its processes, stops its wineserver, and waits for the threads
 * that the host created for it to end. It waits up to 30 s for the runtime's report of the
 * process's end and for the end of the process's output, and then counts a process whose end was
 * not reported as ended with exit code 1. The session-ended event follows unless the session had
 * already ended. Once it returns, no thread of the session runs Windows code again.
 *
 * pp_shutdown refuses with PP_E_BUSY while a session is starting, running or stopping, so the
 * caller stops the session first; after a session that ended by itself, this call still joins
 * the threads that the host created for it.
 *
 * Thread rule: [main]. Memory: none.
 *
 * @return PP_OK, also when the session had already ended by itself; PP_E_NOT_INITIALIZED;
 *         PP_E_WRONG_THREAD; PP_E_NO_SESSION when no session has started.
 */
PP_EXPORT pp_status_t pp_session_stop(void);

/**
 * Launches a Windows program as the session's process, and returns once it finished its start,
 * or failed to start. The process-started event follows when it started, and the process-exited
 * event when it ends. The process's start sets up Wine's signal handling, so test builds check
 * again, when the runtime reports the start and before the process-started event, that the
 * signal dispatcher holds its six signals (C-0005 section 4). A replaced handler fails the
 * session, whose session-ended event then carries PP_E_SYSTEM, and the launch, with PP_E_SYSTEM.
 * The launch terminates the process with exit code 1 if it still runs; a process that has already
 * ended keeps its own exit code.
 *
 * Thread rule: [any]. Memory: the host copies what it keeps; the caller keeps ownership of
 * config.
 *
 * @param config The settings. Must not be NULL, and struct_size must cover every member that
 *               this version declares.
 * @param process Receives the process's handle, which is never 0. Must not be NULL.
 * @return PP_OK; PP_E_NO_SESSION unless a session runs, also when the session's stop began during
 *         the launch, before the runtime heard of the process, which then counts as ended with
 *         exit code 1, with *process set; PP_E_UNSUPPORTED when the session already runs its
 *         process, since this version runs one per session; PP_E_INVALID_ARGUMENT for a missing
 *         or malformed member, a folder that is relative, too long or not a folder, or a block
 *         that is malformed or too long. With *process set: PP_E_SYSTEM or PP_E_OUT_OF_MEMORY
 *         when the host cannot set up the capture of the output, and, when the runtime refuses
 *         the launch, its own negative status, a PP_WINE_E_ code of paneport/wine_bridge.h such
 *         as PP_E_OUT_OF_MEMORY or PP_E_IO, or PP_E_SYSTEM for any other answer; the process
 *         then counts as ended with exit code 1. PP_E_SYSTEM when the program ended before it
 *         finished its start, so that pp_process_get_state reports its exit code. PP_E_SYSTEM
 *         when the program did not finish its start within 120 s: the process is then still
 *         PP_PROCESS_STARTING, without an exit code, and its process-started or process-exited
 *         event follows once the runtime reports it. In test builds PP_E_SYSTEM when its start
 *         replaced a handler of the signal dispatcher.
 */
PP_EXPORT pp_status_t pp_process_launch(const pp_process_config_t *config, uint64_t *process);

/**
 * Reads the state of a process of the session, and its exit code once it ended. It also works
 * after the session ended.
 *
 * The host keeps its own record of the process from the runtime's reports of its start and its
 * end, which the process-started and process-exited events follow. Until that record says that
 * the process ended, the host asks the runtime's query of the process (C-0005 section 3), and
 * reports the runtime's answer when the runtime knows the process: the runtime can know of an end
 * whose report, and so whose event, is still on its way. Once the record says that the process
 * ended, the record answers.
 *
 * Thread rule: [any]. Memory: the caller owns state.
 *
 * @param process A handle that pp_process_launch returned.
 * @param state The caller's struct, with struct_size set to at least 8. The host writes only
 *              the members that lie entirely inside struct_size.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for a NULL state, a struct_size below 8, or a handle
 *         that names no process of this launch.
 */
PP_EXPORT pp_status_t pp_process_get_state(uint64_t process, pp_process_state_t *state);

/**
 * Terminates a process of the session, as TerminateProcess does: its threads end without
 * running more of the program, and the process-exited event reports exit_code. It returns once
 * the termination is under way; the event says when it is done.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @param process A handle that pp_process_launch returned.
 * @param exit_code The exit code that the process gets.
 * @return PP_OK, also when the process has already ended; PP_E_INVALID_ARGUMENT for a handle
 *         that names no process of this launch; PP_E_SYSTEM when the runtime refuses.
 */
PP_EXPORT pp_status_t pp_process_terminate(uint64_t process, uint32_t exit_code);

PP_EXTERN_C_END

#endif /* PP_SESSION_H */
