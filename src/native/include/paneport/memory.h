// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

/**
 * @file
 * The memory group of the host interface (C-0005 section 2).
 */

#ifndef PANEPORT_MEMORY_H
#define PANEPORT_MEMORY_H

#include <stdint.h>

#include "paneport/common.h"

PP_EXTERN_C_BEGIN

/**
 * The memory figures of the app, in bytes. The memory limit is the host's estimate:
 * the available memory plus the current footprint. Wherever a contract says "memory
 * limit", it means this value (C-0005 section 2).
 */
typedef struct pp_memory_budget_t {
    /** sizeof(pp_memory_budget_t) as the caller knows it. Set by the caller. */
    uint32_t struct_size;
    /** 1 when the limit has reached the Game Mode level of about 8,192 MB, else 0. */
    uint8_t game_mode_level_reached;
    /** The memory limit: available_bytes plus footprint_bytes. */
    uint64_t limit_bytes;
    /** The memory still available to the app. */
    uint64_t available_bytes;
    /** The app's current memory footprint. */
    uint64_t footprint_bytes;
} pp_memory_budget_t;

/**
 * Reads the memory budget now. On iOS the available memory comes from
 * os_proc_available_memory() and the footprint from phys_footprint of TASK_VM_INFO; no
 * private call is used. Each call reads both again.
 *
 * Thread rule: [any]. It does not need pp_init(). Memory: the caller owns budget.
 *
 * @param budget The caller's struct, with struct_size set. The host writes only the members
 *               that lie entirely inside struct_size, and leaves the rest untouched.
 * @return PP_OK; PP_E_INVALID_ARGUMENT for NULL or a struct_size below 4; PP_E_SYSTEM when
 *         the system does not report the footprint.
 */
PP_EXPORT pp_status_t pp_memory_get_budget(pp_memory_budget_t *budget);

PP_EXTERN_C_END

#endif /* PANEPORT_MEMORY_H */
