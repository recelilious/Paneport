// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The bridge between the native host and the Wine fork (C-0005 section 3): the table of C
 * function pointers, pp_wine_bridge_t, that the host passes the fork when a session starts.
 * Through it the fork logs its diagnostics, has the host create the wineserver's thread,
 * marks its threads and registers its guest address reservations and its guest fault handler
 * with the signal dispatcher (C-0005 section 4), and reports its process events. Later stages
 * append the members they need, such as the JIT code pool (M4.4) and the display (M5.1).
 *
 * This header includes no other Paneport header, so that the Wine fork's build can add
 * src/native/include to its include path and use it alone; the header is never copied into
 * the fork. The fork's own entry points, which the host calls, are declared in the fork's
 * header include/wine/paneport.h, under the fork's license (C-0005 section 3).
 *
 * Thread rules: every member runs on any thread, but never in a signal handler, except where a
 * member says otherwise. A guest fault handler runs only in a signal handler.
 */

#ifndef PP_WINE_BRIDGE_H
#define PP_WINE_BRIDGE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/**
 * The version of the bridge's members. It grows by one whenever a stage appends members. The
 * fork refuses a table whose version is older than the one it was built with.
 */
#define PP_WINE_BRIDGE_VERSION 1

/**
 * The results of the bridge's members and of the fork's entry points: 0, or a negative status.
 * Each has the value of the host interface's pp_status_t code of the same name, such as
 * PP_E_SYSTEM, which this header cannot include.
 */
enum {
    /** The call succeeded. */
    PP_WINE_OK = 0,
    /** An argument is missing or out of range. */
    PP_WINE_E_INVALID_ARGUMENT = -1,
    /** Memory or a table ran out. */
    PP_WINE_E_OUT_OF_MEMORY = -5,
    /** A file or folder could not be read or written. */
    PP_WINE_E_IO = -6,
    /** The call is not available in this build. */
    PP_WINE_E_UNSUPPORTED = -7,
    /** The resource is in use. */
    PP_WINE_E_BUSY = -8,
    /** The system, or the runtime, refused. */
    PP_WINE_E_SYSTEM = -9,
};

/**
 * The marks of a thread whose faults take the guest path of the signal dispatcher (C-0005
 * section 4). Every thread that the Wine fork creates carries the guest mark while it runs
 * Windows code or the fork's Unix side for it.
 */
enum {
    /** No mark: the thread's faults go to the .NET runtime unless their address is a guest one. */
    PP_THREAD_MARK_NONE = 0,
    /** A thread of a pseudo-process. */
    PP_THREAD_MARK_GUEST = 1,
    /** A thread of a translator or a graphics layer that runs on behalf of the guest. */
    PP_THREAD_MARK_TRANSLATOR = 2,
};

/** The routes on which a fault reaches the guest fault handler, or the JIT probe handler. */
enum {
    /** The fault went to the .NET runtime's handler; a guest handler never sees it. */
    PP_FAULT_ROUTE_RUNTIME = 0,
    /** The faulting thread carries a guest or translator mark. */
    PP_FAULT_ROUTE_THREAD_MARK = 1,
    /** The fault address or the program counter lies in the JIT code pool. */
    PP_FAULT_ROUTE_JIT_POOL = 2,
    /** The fault address lies in a registered guest reservation. */
    PP_FAULT_ROUTE_RESERVATION = 3,
    /** The fault lies in the JIT code pool while the JIT state is verifying (C-0006 section 3). */
    PP_FAULT_ROUTE_JIT_PROBE = 4,
};

/** What a guest fault handler returns. */
enum {
    /** The handler did not take the fault and left the context unchanged; the app ends. */
    PP_GUEST_FAULT_DECLINED = 0,
    /** The thread goes on from the context as the handler left it. */
    PP_GUEST_FAULT_HANDLED = 1,
};

/**
 * A fault on its way to a guest fault handler. It lives on the stack of the signal handler and
 * is valid only until the handler returns.
 */
typedef struct pp_guest_fault_t {
    /** sizeof(pp_guest_fault_t) in the host that built it. */
    uint32_t struct_size;
    /** One of the PP_FAULT_ROUTE_ values. */
    uint32_t route;
    /** The faulting thread's PP_THREAD_MARK_ value. */
    uint32_t thread_mark;
    /** The signal number: SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP or SIGABRT. */
    int32_t signal;
    /** The signal's si_code. */
    int32_t code;
    /** The signal's si_addr. */
    uint64_t fault_address;
    /** The program counter of the context. */
    uint64_t program_counter;
    /** The kernel's siginfo_t. */
    void *signal_info;
    /** The kernel's ucontext_t, which the handler may change. */
    void *context;
} pp_guest_fault_t;

/**
 * Handles a fault of the guest path in the faulting thread's signal context. It may call only
 * async-signal-safe functions, must return, and never calls a member of the bridge. It returns
 * PP_GUEST_FAULT_HANDLED when the thread may go on from the context as the handler left it, or
 * PP_GUEST_FAULT_DECLINED, with the context unchanged, which ends the app with a crash record.
 * A fault inside the handler on a marked thread ends the app with a record of its own.
 *
 * @param fault The fault. Valid until the handler returns.
 * @param user_data The value given to set_guest_fault_handler.
 * @return PP_GUEST_FAULT_HANDLED or PP_GUEST_FAULT_DECLINED.
 */
typedef uint32_t (*pp_guest_fault_handler_t)(pp_guest_fault_t *fault, void *user_data);

/** How important a line of the runtime's diagnostics is. The values are those of PP_LOG_. */
enum {
    /** Detail that helps only while debugging. */
    PP_WINE_LOG_DEBUG = 0,
    /** A normal event. */
    PP_WINE_LOG_INFO = 1,
    /** Something unexpected that the runtime recovered from. */
    PP_WINE_LOG_WARNING = 2,
    /** A failure. */
    PP_WINE_LOG_ERROR = 3,
};

/**
 * The body of a thread that the host creates for the fork.
 *
 * @param argument The value given to start_thread.
 */
typedef void (*pp_wine_thread_body_t)(void *argument);

/**
 * The table that the host passes the Wine fork when a session starts. The host owns it, and it
 * stays valid until the process ends. A member that returns int32_t returns PP_WINE_OK or a
 * negative PP_WINE_E_ status.
 */
typedef struct pp_wine_bridge_t {
    /** sizeof(pp_wine_bridge_t) in the host that built it. Members past it are missing. */
    uint32_t struct_size;
    /** PP_WINE_BRIDGE_VERSION as the host knows it. */
    uint32_t version;

    /**
     * Hands one line of the runtime's diagnostics to the host's log pipeline, which delivers it
     * to the log callback marked as coming from the runtime (PP_LOG_SOURCE_RUNTIME). A line that
     * is too long is cut at a character boundary.
     *
     * Memory: the host copies the text before the call returns.
     *
     * @param level One of the PP_WINE_LOG_ values.
     * @param text The line, UTF-8 and NUL-terminated, without a line break at its end.
     */
    void (*log)(int32_t level, const char *text);

    /**
     * Creates a thread that the host owns and joins when the session ends, with a stack of
     * 8 MiB, as a main thread has, and runs body on it. The fork runs the wineserver's event
     * loop on such a thread (C-0004 section 1).
     *
     * Memory: the host copies the name. The fork keeps ownership of argument.
     *
     * @param name The thread's name for debuggers and crash reports, ASCII, at most 63 bytes.
     * @param body The thread's body. The thread ends when it returns.
     * @param argument The value passed to body.
     * @return PP_WINE_OK; PP_WINE_E_INVALID_ARGUMENT for a NULL name or body; PP_WINE_E_BUSY when
     * the session already has as many such threads as the host keeps (four); PP_WINE_E_SYSTEM.
     */
    int32_t (*start_thread)(const char *name, pp_wine_thread_body_t body, void *argument);

    /**
     * Marks the calling thread as a guest or translator thread for the signal dispatcher, or
     * changes its mark, until unmark_thread or until the thread ends. The thread gets an
     * alternate signal stack unless it has one.
     *
     * @param mark PP_THREAD_MARK_GUEST or PP_THREAD_MARK_TRANSLATOR.
     * @return PP_WINE_OK; PP_WINE_E_INVALID_ARGUMENT for another mark; PP_WINE_E_OUT_OF_MEMORY when
     * the table of marked threads is full; PP_WINE_E_SYSTEM.
     */
    int32_t (*mark_thread)(uint32_t mark);

    /**
     * Takes the calling thread's mark away, if it has one.
     *
     * @return PP_WINE_OK.
     */
    int32_t (*unmark_thread)(void);

    /**
     * Registers a guest address reservation, from start for size bytes: a fault inside it takes
     * the guest path on any thread.
     *
     * @param start The first address.
     * @param size The size in bytes.
     * @param handle Receives the reservation's handle.
     * @return PP_WINE_OK; PP_WINE_E_INVALID_ARGUMENT for a NULL handle, a start or size of 0, or a
     * range past the end of the address space; PP_WINE_E_OUT_OF_MEMORY when the table is full.
     */
    int32_t (*add_reservation)(uint64_t start, uint64_t size, uint64_t *handle);

    /**
     * Removes a guest address reservation.
     *
     * @param handle A handle that add_reservation returned.
     * @return PP_WINE_OK; PP_WINE_E_INVALID_ARGUMENT for a handle that names no reservation.
     */
    int32_t (*remove_reservation)(uint64_t handle);

    /**
     * Sets the guest fault handler, or removes it with NULL. When it returns, no fault is still
     * in the handler that it replaced.
     *
     * @param handler The handler, or NULL.
     * @param user_data A value passed to every call of the handler.
     * @return PP_WINE_OK.
     */
    int32_t (*set_guest_fault_handler)(pp_guest_fault_handler_t handler, void *user_data);

    /**
     * Reports that a Windows process of the session finished its start and is about to run the
     * program, on the process's first thread. The host reports a process-started event and lets
     * pp_process_launch return.
     *
     * @param process The host's handle of the process, as the launch gave it.
     */
    void (*process_started)(uint64_t process);

    /**
     * Reports that a Windows process of the session ended, or failed to start, with its exit
     * code. Its threads have ended or are ending, and none runs Windows code again. The host
     * reports a process-exited event.
     *
     * @param process The host's handle of the process, as the launch gave it.
     * @param exit_code The process's exit code, as GetExitCodeProcess reports it.
     */
    void (*process_exited)(uint64_t process, uint32_t exit_code);
} pp_wine_bridge_t;

#ifdef __cplusplus
}
#endif

#endif /* PP_WINE_BRIDGE_H */
