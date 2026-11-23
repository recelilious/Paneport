// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "jit/jit_map.h"

#include <mach/mach.h>
#include <mach/vm_map.h>
#include <stddef.h>
#include <string.h>
#include <unistd.h>

static bool writable_executable(vm_prot_t protection) {
    if ((protection & VM_PROT_WRITE) == 0) {
        return false;
    }
    return (protection & VM_PROT_EXECUTE) != 0;
}

// Calls visit for each mapping that covers [address, address + size), in order. Returns false
// when a page of the range is not mapped, or when visit returns false.
static bool walk(uint64_t address, uint64_t size,
                 bool (*visit)(const vm_region_basic_info_data_64_t *info, void *argument),
                 void *argument) {
    if (size == 0U || address + size < address) {
        return false;
    }
    const uint64_t end = address + size;
    uint64_t cursor = address;
    while (cursor < end) {
        vm_address_t start = (vm_address_t)cursor;
        vm_size_t length = 0U;
        vm_region_basic_info_data_64_t info;
        memset(&info, 0, sizeof info);
        mach_msg_type_number_t count = VM_REGION_BASIC_INFO_COUNT_64;
        mach_port_t object = MACH_PORT_NULL;
        const kern_return_t result =
            vm_region_64(mach_task_self(), &start, &length, VM_REGION_BASIC_INFO_64,
                         (vm_region_info_t)&info, &count, &object);
        // vm_region_64 reports the mapping that holds the address, or the next one after a
        // hole; the next one means that the range is not mapped throughout.
        if (result != KERN_SUCCESS || (uint64_t)start > cursor || length == 0U) {
            return false;
        }
        if (!visit(&info, argument)) {
            return false;
        }
        cursor = (uint64_t)start + (uint64_t)length;
    }
    return true;
}

typedef struct protection_check_t {
    vm_prot_t expected;
    bool check_maximum;
} protection_check_t;

static bool has_expected(const vm_region_basic_info_data_64_t *info, void *argument) {
    const protection_check_t *check = argument;
    if (writable_executable(info->protection) || info->protection != check->expected) {
        return false;
    }
    if (!check->check_maximum) {
        return true;
    }
    return info->max_protection == check->expected;
}

bool pp_jit_map_has_protection(uint64_t address, uint64_t size, int expected, bool check_maximum) {
    protection_check_t check = {.expected = (vm_prot_t)expected, .check_maximum = check_maximum};
    return walk(address, size, has_expected, &check);
}

static bool never_writable_executable(const vm_region_basic_info_data_64_t *info, void *argument) {
    (void)argument;
    if (writable_executable(info->protection) || writable_executable(info->max_protection)) {
        return false;
    }
    return true;
}

bool pp_jit_map_has_writable_executable(uint64_t address, uint64_t size) {
    if (walk(address, size, never_writable_executable, NULL)) {
        return false;
    }
    return true;
}

pp_jit_failure_t pp_jit_map_alias(uint64_t exec_address, uint64_t size,
                                  pp_jit_map_result_t *result) {
    memset(result, 0, sizeof *result);
    const uint64_t page = pp_jit_page_size();
    if (exec_address == 0U || size == 0U || exec_address % page != 0U || size % page != 0U ||
        !pp_jit_map_has_protection(exec_address, size, PP_JIT_EXEC_PROTECTION, false)) {
        return PP_JIT_FAILURE_REGION_REFUSED;
    }

    // A second mapping of the same memory, never inherited by a child (Paneport has none).
    const mach_port_t task = mach_task_self();
    vm_address_t alias = 0U;
    vm_prot_t current = VM_PROT_NONE;
    vm_prot_t maximum = VM_PROT_NONE;
    if (vm_remap(task, &alias, (vm_size_t)size, 0U, VM_FLAGS_ANYWHERE, task,
                 (vm_address_t)exec_address, FALSE, &current, &maximum,
                 VM_INHERIT_NONE) != KERN_SUCCESS) {
        return PP_JIT_FAILURE_MAPPING;
    }
    if (vm_protect(task, alias, (vm_size_t)size, FALSE, PP_JIT_WRITE_PROTECTION) != KERN_SUCCESS) {
        pp_jit_map_release_alias((uint64_t)alias, size);
        return PP_JIT_FAILURE_MAPPING;
    }

    // Lowering the maxima makes the split permanent: vm_protect can then never make the alias
    // executable or the view writable. Raising the current protection afterwards fails with
    // KERN_PROTECTION_FAILURE, as the probe of M3.1 showed on the runner.
    const bool alias_lowered =
        vm_protect(task, alias, (vm_size_t)size, TRUE, PP_JIT_WRITE_PROTECTION) == KERN_SUCCESS;
    const bool view_lowered = vm_protect(task, (vm_address_t)exec_address, (vm_size_t)size, TRUE,
                                         PP_JIT_EXEC_PROTECTION) == KERN_SUCCESS;

    if (!pp_jit_map_has_protection((uint64_t)alias, size, PP_JIT_WRITE_PROTECTION, false) ||
        !pp_jit_map_has_protection(exec_address, size, PP_JIT_EXEC_PROTECTION, false)) {
        pp_jit_map_release_alias((uint64_t)alias, size);
        return PP_JIT_FAILURE_MAPPING;
    }
    result->write_address = (uint64_t)alias;
    result->maxima_lowered = false;
    if (alias_lowered && view_lowered) {
        result->maxima_lowered = true;
    }
    return PP_JIT_FAILURE_NONE;
}

void pp_jit_map_release_alias(uint64_t write_address, uint64_t size) {
    if (write_address != 0U && size != 0U) {
        (void)vm_deallocate(mach_task_self(), (vm_address_t)write_address, (vm_size_t)size);
    }
}

uint64_t pp_jit_page_size(void) {
    const long size = sysconf(_SC_PAGESIZE);
    return size > 0 ? (uint64_t)size : UINT64_C(16384);
}
