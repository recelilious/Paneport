// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The JIT group of the host interface (C-0005 section 2), which version 1.3 added: the state of
 * the JIT request protocol of C-0006, the request of the code pool, and the two notices with
 * which the app tells the host that it opens the JIT enabler and that it came back to the
 * foreground. docs/design/jit.md describes the provider behind them.
 *
 * Thread rules as in paneport/host.h: a function marked [main] runs only on the main thread and
 * fails with PP_E_WRONG_THREAD elsewhere; one marked [any] runs on any thread. Every function
 * needs a running host (pp_init()). The host reports each change of the state through the
 * event callback, on the host event thread, with the kind PP_EVENT_JIT_STATE_CHANGED.
 */

#ifndef PP_JIT_H
#define PP_JIT_H

#include <stdint.h>

#include "paneport/common.h"

PP_EXTERN_C_BEGIN

/** The status code of the JIT group. */
enum {
    /**
     * The JIT state does not allow the call: for example, a pool request before the broker is
     * attached or after a pool was requested. The message names the state.
     */
    PP_E_JIT_STATE = -10,
};

/** A state of the JIT request protocol (C-0006 section 5): one of the PP_JIT_STATE_ values. */
typedef int32_t pp_jit_state_t;

/** The states of C-0006 section 5, in the order in which a request passes them. */
enum {
    /** No debugger is attached, or only a foreign one is. */
    PP_JIT_STATE_NOT_DEBUGGED = 0,
    /** The broker that this launch asked for is attached; no pool has been requested yet. */
    PP_JIT_STATE_DEBUGGER_ATTACHED = 1,
    /** The pool is being requested. */
    PP_JIT_STATE_REQUESTING = 2,
    /** The pool is mapped, and the debugger is detaching. */
    PP_JIT_STATE_WAITING_DETACH = 3,
    /** The probe runs, with no debugger attached. */
    PP_JIT_STATE_VERIFYING = 4,
    /** The pool is authorized and verified. */
    PP_JIT_STATE_READY = 5,
    /** A request, the detach, the probe or a timeout failed. Only a relaunch leaves it. */
    PP_JIT_STATE_FAILED = 6,
};

/** Why the state became PP_JIT_STATE_FAILED: one of the PP_JIT_FAILURE_ values. */
typedef int32_t pp_jit_failure_t;

/** The reasons of PP_JIT_STATE_FAILED. */
enum {
    /** The state is not PP_JIT_STATE_FAILED. */
    PP_JIT_FAILURE_NONE = 0,
    /** One eighth of the memory limit is less than PP_JIT_POOL_MIN_MIB (C-0006 section 4). */
    PP_JIT_FAILURE_MEMORY_LIMIT = 1,
    /** The pool was not complete PP_JIT_POOL_TIMEOUT_MS after the state became requesting. */
    PP_JIT_FAILURE_POOL_TIMEOUT = 2,
    /** The debugger had not detached PP_JIT_DETACH_TIMEOUT_MS after the host asked it to. */
    PP_JIT_FAILURE_DETACH_TIMEOUT = 3,
    /** The debugger returned no region, or a region that is not executable and read-only. */
    PP_JIT_FAILURE_REGION_REFUSED = 4,
    /** The host could not make a region's writable alias with the protections it needs. */
    PP_JIT_FAILURE_MAPPING = 5,
    /** The debugger detached before the pool was complete. */
    PP_JIT_FAILURE_DEBUGGER_LOST = 6,
    /** A probed page did not run its test routine (C-0006 section 3 step 6). */
    PP_JIT_FAILURE_VERIFICATION = 7,
    /** The system refused something the host needs, such as a thread or the debugger flags. */
    PP_JIT_FAILURE_SYSTEM = 8,
};

/** The pool sizes of C-0006 section 4, in MiB. */
#define PP_JIT_POOL_DEFAULT_MIB 512U
#define PP_JIT_POOL_MIN_MIB 256U
#define PP_JIT_POOL_MAX_MIB 896U

/** The timeouts of C-0006 section 3, in milliseconds. */
#define PP_JIT_POOL_TIMEOUT_MS 60000U
#define PP_JIT_DETACH_TIMEOUT_MS 30000U

/** The event kinds of the JIT group. */
enum {
    /**
     * The JIT state changed. The event's code holds the new pp_jit_state_t; its handle and
     * address are 0. pp_jit_get_state() tells the rest, such as a failure's reason.
     */
    PP_EVENT_JIT_STATE_CHANGED = 1,
};

/**
 * The JIT state and what the host knows about the request. Durations are in milliseconds of
 * the host's monotonic clock.
 */
typedef struct pp_jit_status_t {
    /**
     * sizeof(pp_jit_status_t) as the caller knows it, at least 4. Set by the caller: the host
     * writes only the members that lie entirely inside it, and leaves it as it is.
     */
    uint32_t struct_size;
    /** The state, a PP_JIT_STATE_ value. */
    pp_jit_state_t state;
    /** Why the state is PP_JIT_STATE_FAILED, or PP_JIT_FAILURE_NONE. */
    pp_jit_failure_t failure;
    /** 1 when the last read found the code-signing flag CS_DEBUGGED set, else 0. */
    uint8_t debugged_since_launch;
    /** 1 when the last read found the process's traced flag P_TRACED set, else 0. */
    uint8_t attached_now;
    /** 1 when the app said that this launch opened the JIT enabler, else 0. */
    uint8_t enabler_opened;
    /** 1 when a debugger is attached that the host does not take for the broker, else 0. */
    uint8_t foreign_debugger;
    /** The size that pp_jit_request_pool() asked for, in MiB, or 0 before it. */
    uint32_t requested_mib;
    /** The pool's size after the limits of C-0006 section 4, in MiB, or 0. */
    uint32_t pool_mib;
    /** How many regions the pool takes. */
    uint32_t regions_planned;
    /** How many regions are prepared and mapped so far. */
    uint32_t regions_mapped;
    /** How long the current state has lasted so far. */
    uint64_t in_state_ms;
    /** How long the state was debugger_attached, once it left it, in total. */
    uint64_t debugger_attached_ms;
    /** How long the state was requesting, once it left it. */
    uint64_t requesting_ms;
    /** How long the state was waiting_detach, once it left it. */
    uint64_t waiting_detach_ms;
    /** How long the state was verifying, once it left it. */
    uint64_t verifying_ms;
    /** The time from the request until the state became ready, or 0 before it. */
    uint64_t authorization_ms;
} pp_jit_status_t;

/**
 * Reads the JIT state.
 *
 * Thread rule: [any]. Memory: the caller owns status.
 *
 * @param status The caller's struct, with struct_size set. The host writes only the members
 *               that lie entirely inside struct_size.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL or a struct_size below 4; PP_E_NOT_INITIALIZED.
 */
PP_EXPORT pp_status_t pp_jit_get_state(pp_jit_status_t *status);

/**
 * Tells the host that this launch opens the JIT enabler, stikdebug://enable-jit (C-0006
 * section 3 step 2), or that iOS refused to open it. The app calls it with 1 right before it
 * asks iOS to open the URL, and with 0 when the open fails. The host takes an attached debugger
 * for the broker only when this launch opened the URL and the debugger attached afterwards;
 * any other debugger is foreign, and the host never sends it a JIT command. After the call
 * with 1, the host watches for the broker on a thread of its own.
 *
 * Thread rule: [main]. Memory: none.
 *
 * @param opened 1 before opening the URL, 0 when opening it failed.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for another value; PP_E_JIT_STATE for 1 in any state but
 *         not_debugged, and for 0 in any state but not_debugged and debugger_attached;
 *         PP_E_NOT_INITIALIZED; PP_E_WRONG_THREAD.
 */
PP_EXPORT pp_status_t pp_jit_set_enabler_opened(uint8_t opened);

/**
 * Tells the host that the app came back to the foreground. The host reads the debugger flags
 * again at once (C-0006 section 3 step 1), and the state follows them. In the ready state the
 * reading is only recorded, because a pool once authorized stays authorized.
 *
 * Thread rule: [main]. Memory: none.
 *
 * @return PP_OK; PP_E_NOT_INITIALIZED; PP_E_WRONG_THREAD.
 */
PP_EXPORT pp_status_t pp_jit_note_foreground(void);

/**
 * Starts the request of the code pool (C-0006 section 3 step 3). The host limits the size to
 * PP_JIT_POOL_MIN_MIB to PP_JIT_POOL_MAX_MIB and to one eighth of the memory limit that
 * pp_memory_get_budget() reads now (C-0006 section 4); when that eighth is below the minimum,
 * the state becomes failed. The app passes the title's jitPoolMiB when its compatibility entry
 * has one (C-0008 section 2), and PP_JIT_POOL_DEFAULT_MIB otherwise. The call returns at once;
 * the state reports the request's progress and its end, ready or failed.
 *
 * Thread rule: [main]. Memory: none.
 *
 * @param size_mib The size the app chose, in MiB; above 0.
 * @return PP_OK when the request started, or failed for the memory limit; PP_E_INVALID_ARGUMENT
 *         for 0; PP_E_JIT_STATE in any state but debugger_attached; PP_E_NOT_INITIALIZED;
 *         PP_E_WRONG_THREAD.
 */
PP_EXPORT pp_status_t pp_jit_request_pool(uint32_t size_mib);

PP_EXTERN_C_END

#endif /* PP_JIT_H */
