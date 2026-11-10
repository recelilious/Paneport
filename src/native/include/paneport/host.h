// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The life cycle and diagnostics groups of the host interface (C-0005 section 2): the
 * interface version, starting and stopping the host, its events and log lines, error
 * messages, and freeing host memory.
 *
 * Thread rules: a function marked [main] runs only on the process's main thread, and fails
 * with PP_E_WRONG_THREAD elsewhere. In test builds the thread that first calls pp_init()
 * stands in for the main thread, because test runners do not run tests on it. A function
 * marked [any] runs on any thread. Callbacks run only on the host event thread.
 */

#ifndef PANEPORT_HOST_H
#define PANEPORT_HOST_H

#include <stdint.h>

#include "paneport/common.h"

PP_EXTERN_C_BEGIN

/** The settings of pp_init(). */
typedef struct pp_init_config_t {
    /** sizeof(pp_init_config_t) as the caller knows it. Members past it count as unset. */
    uint32_t struct_size;
    /**
     * The folder for crash records, UTF-8 and NUL-terminated, such as
     * Library/Caches/Paneport/ inside the app container (C-0004 section 4). It must exist and
     * be writable. The host copies it. NULL means that the host writes no crash record.
     */
    const char *crash_record_folder;
} pp_init_config_t;

/**
 * An event that the host reports. Version 1.0 defines no kind; each later function group
 * defines its own kinds and what code, handle and address hold for them.
 */
typedef struct pp_event_t {
    /** sizeof(pp_event_t) in the host that sent it. */
    uint32_t struct_size;
    /** Which event this is. */
    uint32_t kind;
    /** A value whose meaning the kind defines, such as an exit code. */
    int64_t code;
    /** The handle that the event is about, or 0. */
    uint64_t handle;
    /** An address whose meaning the kind defines, or 0. */
    uint64_t address;
} pp_event_t;

/** How important a log line is. The values match Paneport.Core's LogLevel. */
enum {
    /** Detail that helps only while debugging. */
    PP_LOG_DEBUG = 0,
    /** A normal event. */
    PP_LOG_INFO = 1,
    /** Something unexpected that the host recovered from. */
    PP_LOG_WARNING = 2,
    /** A failure. */
    PP_LOG_ERROR = 3
};

/** A log line of the host. */
typedef struct pp_log_line_t {
    /** sizeof(pp_log_line_t) in the host that sent it. */
    uint32_t struct_size;
    /** One of the PP_LOG_ values. */
    int32_t level;
    /** The system's identifier of the thread that logged the line. */
    uint64_t thread_id;
    /**
     * The text, UTF-8 and NUL-terminated, without a line break at the end. It holds no
     * password, token or other secret (C-0010 section 2). Valid until the callback returns.
     */
    const char *text;
} pp_log_line_t;

/**
 * Receives the host's events. It runs only on the host event thread, must return quickly,
 * and must not call into the host (C-0005 section 1).
 *
 * @param event The event. Host memory, valid until the callback returns.
 * @param user_data The value given to pp_set_event_callback().
 */
typedef void (*pp_event_callback_t)(const pp_event_t *event, void *user_data);

/**
 * Receives the host's log lines. It runs only on the host event thread, must return
 * quickly, and must not call into the host (C-0005 section 1).
 *
 * @param line The line. Host memory, valid until the callback returns.
 * @param user_data The value given to pp_set_log_callback().
 */
typedef void (*pp_log_callback_t)(const pp_log_line_t *line, void *user_data);

/**
 * Returns the version of the host interface that this host implements.
 *
 * Thread rule: [any]. Memory: none.
 *
 * @return The major version in the high 16 bits and the minor version in the low 16 bits.
 */
PP_EXPORT uint32_t pp_abi_version(void);

/**
 * Describes the calling thread's last call into the host that failed.
 *
 * Thread rule: [any]. It does not change the message itself. Memory: the host owns the
 * text, which stays valid until the calling thread's next call into the host.
 *
 * @return The message, UTF-8 and NUL-terminated, or an empty string when the thread's last
 *         call succeeded. Never NULL.
 */
PP_EXPORT const char *pp_last_error(void);

/**
 * Frees memory that the host returned to the caller.
 *
 * Thread rule: [any]. Memory: takes ownership of the block, which the caller must not use
 * again. NULL is ignored.
 *
 * @param memory A block that a host function returned, or NULL.
 */
PP_EXPORT void pp_free(void *memory);

/**
 * Starts the host: copies the configuration, prepares the crash record folder, and starts
 * the host event thread. It runs once per process; a second call fails, also after
 * pp_shutdown().
 *
 * Thread rule: [main]. In test builds the first caller becomes the main thread.
 * Memory: the host copies what it keeps; the caller keeps ownership of config.
 *
 * @param config The settings. Must not be NULL, and struct_size must cover at least its own
 *               member.
 * @return PP_OK; PP_E_ALREADY_INITIALIZED; PP_E_INVALID_ARGUMENT for a missing config, a
 *         path that is too long or not a folder; PP_E_IO for a folder that cannot be written;
 *         PP_E_SYSTEM or PP_E_OUT_OF_MEMORY when the event thread cannot start.
 */
PP_EXPORT pp_status_t pp_init(const pp_init_config_t *config);

/**
 * Stops the host. It delivers what the event thread has already queued, stops the thread
 * and clears both callbacks. Once it returns, no callback runs again.
 *
 * Thread rule: [main]. Memory: none.
 *
 * @return PP_OK; PP_E_NOT_INITIALIZED when the host is not running; PP_E_WRONG_THREAD.
 */
PP_EXPORT pp_status_t pp_shutdown(void);

/**
 * Sets the callback that receives the host's events, or removes it. Events that arrive
 * while no callback is set are discarded. Once the call returns, the previous callback is
 * not running and is never called again, so its user_data may be released.
 *
 * Thread rule: [main]. Memory: the caller keeps ownership of user_data.
 *
 * @param callback The callback, or NULL to remove it.
 * @param user_data A value passed to every call of the callback.
 * @return PP_OK; PP_E_NOT_INITIALIZED; PP_E_WRONG_THREAD.
 */
PP_EXPORT pp_status_t pp_set_event_callback(pp_event_callback_t callback, void *user_data);

/**
 * Sets the callback that receives the host's log lines, or removes it. Lines logged while
 * no callback is set wait in a small buffer and arrive once one is set. Once the call
 * returns, the previous callback is not running and is never called again.
 *
 * Thread rule: [main]. Memory: the caller keeps ownership of user_data.
 *
 * @param callback The callback, or NULL to remove it.
 * @param user_data A value passed to every call of the callback.
 * @return PP_OK; PP_E_NOT_INITIALIZED; PP_E_WRONG_THREAD.
 */
PP_EXPORT pp_status_t pp_set_log_callback(pp_log_callback_t callback, void *user_data);

PP_EXTERN_C_END

#endif /* PANEPORT_HOST_H */
