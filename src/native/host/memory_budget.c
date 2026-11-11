// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "memory_budget.h"

#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>

#include <TargetConditionals.h>

#if TARGET_OS_IOS
#include <mach/mach.h>
#include <os/proc.h>
#endif

#include "error.h"

#if TARGET_OS_IOS

static pp_status_t read_system(uint64_t *available_bytes, uint64_t *footprint_bytes) {
    task_vm_info_data_t info;
    mach_msg_type_number_t count = TASK_VM_INFO_COUNT;
    const kern_return_t result =
        task_info(mach_task_self(), TASK_VM_INFO, (task_info_t)&info, &count);
    if (result != KERN_SUCCESS || count < TASK_VM_INFO_REV1_COUNT) {
        return pp_error_set(PP_E_SYSTEM, "The system did not report the app's footprint.");
    }

    // os_proc_available_memory returns 0 for a process that is not an app; the limit is then
    // the footprint alone.
    *footprint_bytes = info.phys_footprint;
    *available_bytes = (uint64_t)os_proc_available_memory();
    return PP_OK;
}

pp_status_t pp_memory_set_stub(uint64_t available_bytes, uint64_t footprint_bytes) {
    (void)available_bytes;
    (void)footprint_bytes;
    return PP_E_UNSUPPORTED;
}

#else

// The stub's readings, which only test builds set: a process-wide singleton (C-0010
// section 3).
static _Atomic(uint64_t) s_stub_available;
static _Atomic(uint64_t) s_stub_footprint;

static pp_status_t read_system(uint64_t *available_bytes, uint64_t *footprint_bytes) {
    *available_bytes = atomic_load(&s_stub_available);
    *footprint_bytes = atomic_load(&s_stub_footprint);
    return PP_OK;
}

pp_status_t pp_memory_set_stub(uint64_t available_bytes, uint64_t footprint_bytes) {
    atomic_store(&s_stub_available, available_bytes);
    atomic_store(&s_stub_footprint, footprint_bytes);
    return PP_OK;
}

#endif

void pp_memory_budget_compute(uint64_t available_bytes, uint64_t footprint_bytes,
                              pp_memory_budget_t *budget) {
    const uint64_t limit = available_bytes > UINT64_MAX - footprint_bytes
                               ? UINT64_MAX
                               : available_bytes + footprint_bytes;
    budget->struct_size = (uint32_t)sizeof(pp_memory_budget_t);
    budget->game_mode_level_reached = limit >= PP_MEMORY_GAME_MODE_LEVEL_BYTES ? 1U : 0U;
    budget->limit_bytes = limit;
    budget->available_bytes = available_bytes;
    budget->footprint_bytes = footprint_bytes;
}

// Whether a member at offset with size lies entirely inside the caller's struct_size.
static bool fits(uint32_t struct_size, size_t offset, size_t size) {
    return offset + size <= (size_t)struct_size;
}

PP_EXPORT pp_status_t pp_memory_get_budget(pp_memory_budget_t *budget) {
    pp_error_clear();
    if (budget == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_memory_get_budget needs a struct.");
    }
    const uint32_t struct_size = budget->struct_size;
    if (struct_size < sizeof(uint32_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_memory_get_budget needs a struct_size of at least 4, not %u.",
                            (unsigned)struct_size);
    }

    uint64_t available_bytes = 0U;
    uint64_t footprint_bytes = 0U;
    const pp_status_t status = read_system(&available_bytes, &footprint_bytes);
    if (status != PP_OK) {
        return status;
    }

    pp_memory_budget_t full;
    pp_memory_budget_compute(available_bytes, footprint_bytes, &full);

    // A caller built against an older header knows fewer members; write only those.
    if (fits(struct_size, offsetof(pp_memory_budget_t, game_mode_level_reached),
             sizeof full.game_mode_level_reached)) {
        budget->game_mode_level_reached = full.game_mode_level_reached;
    }
    if (fits(struct_size, offsetof(pp_memory_budget_t, limit_bytes), sizeof full.limit_bytes)) {
        budget->limit_bytes = full.limit_bytes;
    }
    if (fits(struct_size, offsetof(pp_memory_budget_t, available_bytes),
             sizeof full.available_bytes)) {
        budget->available_bytes = full.available_bytes;
    }
    if (fits(struct_size, offsetof(pp_memory_budget_t, footprint_bytes),
             sizeof full.footprint_bytes)) {
        budget->footprint_bytes = full.footprint_bytes;
    }
    return PP_OK;
}
