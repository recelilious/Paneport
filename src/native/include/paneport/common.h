// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The conventions that every header of the host interface shares (C-0005 section 1): the
 * interface version, the status codes, and the export and linkage macros.
 */

#ifndef PANEPORT_COMMON_H
#define PANEPORT_COMMON_H

#include <stdint.h>

/**
 * The major version of the host interface. It changes when anything is changed or removed,
 * which needs a change to C-0005. The .NET side refuses to start when it differs.
 */
#define PP_ABI_VERSION_MAJOR 1

/**
 * The minor version of the host interface. It changes when a function is added or a member
 * is appended to a struct.
 */
#define PP_ABI_VERSION_MINOR 0

/** Marks a function that the host exports. Every other symbol of the host is hidden. */
#if defined(__GNUC__) || defined(__clang__)
#define PP_EXPORT __attribute__((visibility("default")))
#else
#define PP_EXPORT
#endif

/** Opens a block of declarations with C linkage, for C++ and Objective-C++ callers. */
#ifdef __cplusplus
#define PP_EXTERN_C_BEGIN extern "C" {
#define PP_EXTERN_C_END }
#else
#define PP_EXTERN_C_BEGIN
#define PP_EXTERN_C_END
#endif

PP_EXTERN_C_BEGIN

/**
 * The result of a call into the host. PP_OK is 0 and every error is negative. After an
 * error, pp_last_error() describes it on the calling thread.
 */
typedef int32_t pp_status_t;

/** The status codes of version 1.0. Later function groups add their own codes. */
enum {
    /** The call succeeded. */
    PP_OK = 0,
    /** An argument is missing, too small or out of range. */
    PP_E_INVALID_ARGUMENT = -1,
    /** A [main] function was called on another thread. */
    PP_E_WRONG_THREAD = -2,
    /** The function needs pp_init() first, or comes after pp_shutdown(). */
    PP_E_NOT_INITIALIZED = -3,
    /** pp_init() runs once per process, and it already ran. */
    PP_E_ALREADY_INITIALIZED = -4,
    /** The host could not allocate the memory the call needs. */
    PP_E_OUT_OF_MEMORY = -5,
    /** A file or folder could not be read or written. */
    PP_E_IO = -6,
    /** The call is not available in this build or on this platform. */
    PP_E_UNSUPPORTED = -7,
    /** The resource is in use by another call, which was not waited for. */
    PP_E_BUSY = -8,
    /** The system refused something the host needs, such as a thread. */
    PP_E_SYSTEM = -9,
};

PP_EXTERN_C_END

#endif /* PANEPORT_COMMON_H */
