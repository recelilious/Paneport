// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S3.1.3: region request and mapping, on the regions of the macOS backend. Alias coherence: a
// byte written through the alias reads back through the executable view, and code written
// through it runs from the view. No mapping is ever writable and executable at once, and
// neither view can be made so later. The check finds memory that may become so (planted:
// writable memory, whose maximum protection allows both). Command 1 goes out once per region,
// with its size, and command 0 once.

#include <mach/mach.h>
#include <mach/vm_map.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <sys/mman.h>

#include "jit/jit_map.h"
#include "jit/jit_platform.h"
#include "jit/jit_probe.h"
#include "jit/jit_provider.h"
#include "jit_test_support.h"
#include "paneport/jit.h"
#include "pp_check.h"
#include "test_support.h"

#define MIB (UINT64_C(1024) * 1024U)

static uint64_t mapped_region(uint64_t size, pp_jit_map_result_t *mapped) {
    const uint64_t exec_address = pp_jit_macos_prepare_region(size);
    PP_CHECK(exec_address != 0U);
    PP_CHECK_EQUAL(pp_jit_map_alias(exec_address, size, mapped), PP_JIT_FAILURE_NONE);
    PP_CHECK(mapped->write_address != 0U);
    PP_CHECK(mapped->write_address != exec_address);
    return exec_address;
}

static void alias_is_coherent(void) {
    pp_jit_map_result_t mapped;
    const uint64_t size = 16U * MIB;
    const uint64_t exec_address = mapped_region(size, &mapped);
    const uint64_t page = pp_jit_page_size();
    const uint64_t offsets[] = {0U, 100U, size / 2U, size - 1U};
    for (size_t index = 0U; index < sizeof offsets / sizeof offsets[0]; index++) {
        const uint8_t value = (uint8_t)(0x5AU + index);
        pp_jit_test_store_byte(mapped.write_address + offsets[index], value);
        PP_CHECK_EQUAL(pp_jit_test_load_byte(exec_address + offsets[index]), value);
        PP_CHECK_EQUAL(pp_jit_test_load_byte(mapped.write_address + offsets[index]), value);
    }
    // Code written through the alias runs from the view, on pages far apart.
    PP_CHECK_EQUAL(pp_jit_probe_page(mapped.write_address + page, exec_address + page, 0x1234U),
                   0x1234U);
    PP_CHECK_EQUAL(
        pp_jit_probe_page(mapped.write_address + size - page, exec_address + size - page, 0x4321U),
        0x4321U);
    pp_jit_map_release_alias(mapped.write_address, size);
    pp_jit_macos_release_region(exec_address, size);
}

static void no_view_is_writable_and_executable(void) {
    pp_jit_map_result_t mapped;
    const uint64_t size = 32U * MIB;
    const uint64_t exec_address = mapped_region(size, &mapped);
    PP_CHECK(mapped.maxima_lowered);
    PP_CHECK(pp_jit_map_has_protection(exec_address, size, PP_JIT_EXEC_PROTECTION, true));
    PP_CHECK(pp_jit_map_has_protection(mapped.write_address, size, PP_JIT_WRITE_PROTECTION, true));
    PP_CHECK(!pp_jit_map_has_writable_executable(exec_address, size));
    PP_CHECK(!pp_jit_map_has_writable_executable(mapped.write_address, size));

    // The lowered maxima keep it so: neither view gains the other's permission.
    const mach_port_t task = mach_task_self();
    const uint64_t page = pp_jit_page_size();
    PP_CHECK_EQUAL(vm_protect(task, (vm_address_t)exec_address, (vm_size_t)page, FALSE,
                              VM_PROT_READ | VM_PROT_WRITE | VM_PROT_EXECUTE),
                   KERN_PROTECTION_FAILURE);
    PP_CHECK_EQUAL(vm_protect(task, (vm_address_t)exec_address, (vm_size_t)page, FALSE,
                              VM_PROT_READ | VM_PROT_WRITE),
                   KERN_PROTECTION_FAILURE);
    PP_CHECK_EQUAL(vm_protect(task, (vm_address_t)mapped.write_address, (vm_size_t)page, FALSE,
                              VM_PROT_READ | VM_PROT_EXECUTE),
                   KERN_PROTECTION_FAILURE);
    PP_CHECK(pp_jit_map_has_protection(exec_address, size, PP_JIT_EXEC_PROTECTION, true));
    PP_CHECK(pp_jit_map_has_protection(mapped.write_address, size, PP_JIT_WRITE_PROTECTION, true));
    pp_jit_map_release_alias(mapped.write_address, size);
    pp_jit_macos_release_region(exec_address, size);
}

static void writable_executable_is_found(void) {
    // Planted: writable memory whose maximum protection, like that of any anonymous memory,
    // still allows writing and executing at once, which lowering the maxima takes away from the
    // pool. It is not a region either, because it is not executable.
    const size_t size = (size_t)(16U * MIB);
    pp_jit_map_result_t mapped;
    void *data = mmap(NULL, size, PROT_READ | PROT_WRITE, MAP_PRIVATE | MAP_ANON, -1, 0);
    PP_CHECK(data != MAP_FAILED);
    const uint64_t data_address = (uint64_t)(uintptr_t)data;
    PP_CHECK(pp_jit_map_has_protection(data_address, size, PP_JIT_WRITE_PROTECTION, false));
    PP_CHECK(!pp_jit_map_has_protection(data_address, size, PP_JIT_WRITE_PROTECTION, true));
    PP_CHECK(pp_jit_map_has_writable_executable(data_address, size));
    PP_CHECK_EQUAL(pp_jit_map_alias(data_address, size, &mapped), PP_JIT_FAILURE_REGION_REFUSED);
    PP_CHECK_EQUAL(munmap(data, size), 0);

    // Nor is a range that is not page-aligned.
    const uint64_t page = pp_jit_page_size();
    const uint64_t exec_address = pp_jit_macos_prepare_region(size);
    PP_CHECK(exec_address != 0U);
    PP_CHECK_EQUAL(pp_jit_map_alias(exec_address + 64U, size - page, &mapped),
                   PP_JIT_FAILURE_REGION_REFUSED);
    PP_CHECK_EQUAL(pp_jit_map_alias(exec_address, size - 64U, &mapped),
                   PP_JIT_FAILURE_REGION_REFUSED);
    PP_CHECK_EQUAL(pp_jit_map_alias(0U, size, &mapped), PP_JIT_FAILURE_REGION_REFUSED);
    // The region itself still is one.
    PP_CHECK_EQUAL(pp_jit_map_alias(exec_address, size, &mapped), PP_JIT_FAILURE_NONE);
    pp_jit_map_release_alias(mapped.write_address, size);
    pp_jit_macos_release_region(exec_address, size);

    // Nor is a range that is not mapped throughout. The maxima of its three thirds are lowered
    // to readable and executable, so that nothing but the hole in the middle can make the whole
    // range fail.
    const uint64_t third = (uint64_t)size;
    const uint64_t base = pp_jit_macos_prepare_region(3U * third);
    PP_CHECK(base != 0U);
    PP_CHECK_EQUAL(vm_protect(mach_task_self(), (vm_address_t)base, (vm_size_t)(3U * third), TRUE,
                              PP_JIT_EXEC_PROTECTION),
                   KERN_SUCCESS);
    PP_CHECK(!pp_jit_map_has_writable_executable(base, 3U * third));
    pp_jit_macos_release_region(base + third, third);
    PP_CHECK(!pp_jit_map_has_writable_executable(base, third));
    PP_CHECK(!pp_jit_map_has_writable_executable(base + (2U * third), third));
    PP_CHECK(pp_jit_map_has_writable_executable(base, 3U * third));
    PP_CHECK(!pp_jit_map_has_protection(base, 3U * third, PP_JIT_EXEC_PROTECTION, false));
    PP_CHECK_EQUAL(pp_jit_map_alias(base, 3U * third, &mapped), PP_JIT_FAILURE_REGION_REFUSED);
    pp_jit_macos_release_region(base, third);
    pp_jit_macos_release_region(base + (2U * third), third);
}

// Runs a request to the end with the fake, and returns the status.
static pp_jit_status_t request(uint32_t size_mib, uint64_t limit_mib, pp_jit_state_t end) {
    pp_jit_set_limit_mib(limit_mib);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_jit_request_pool(size_mib), PP_OK);
    return pp_jit_wait_for_state(end);
}

static void check_registry(const uint64_t *sizes, uint32_t count) {
    pp_jit_pool_region_t regions[PP_JIT_MAX_REGIONS];
    uint32_t copied = 0U;
    pp_jit_pool_copy(regions, PP_JIT_MAX_REGIONS, &copied);
    PP_CHECK_EQUAL(copied, count);
    for (uint32_t index = 0U; index < count; index++) {
        const pp_jit_pool_region_t *region = &regions[index];
        PP_CHECK_EQUAL(region->size, sizes[index]);
        PP_CHECK(region->exec_range != 0U);
        PP_CHECK(region->write_range != 0U);
        PP_CHECK(pp_jit_map_has_protection(region->exec_address, region->size,
                                           PP_JIT_EXEC_PROTECTION, true));
        PP_CHECK(pp_jit_map_has_protection(region->write_address, region->size,
                                           PP_JIT_WRITE_PROTECTION, true));
    }
}

static void commands_per_region(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    const pp_jit_status_t status = request(PP_JIT_POOL_DEFAULT_MIB, 8192U, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(status.pool_mib, 512U);
    PP_CHECK_EQUAL(status.regions_planned, 4U);
    PP_CHECK_EQUAL(status.regions_mapped, 4U);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 4U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->commands_without_debugger), 0U);
    const uint64_t sizes[] = {128U * MIB, 128U * MIB, 128U * MIB, 128U * MIB};
    for (uint32_t index = 0U; index < 4U; index++) {
        PP_CHECK_EQUAL(atomic_load(&fake->lengths[index]), sizes[index]);
    }
    check_registry(sizes, 4U);
    PP_CHECK(pp_jit_is_ready());
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void pool_of_a_title(void) {
    // A title's jitPoolMiB of 300 is kept, and its last region takes the rest.
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    const pp_jit_status_t status = request(300U, 8192U, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(status.requested_mib, 300U);
    PP_CHECK_EQUAL(status.pool_mib, 300U);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 3U);
    const uint64_t sizes[] = {128U * MIB, 128U * MIB, 44U * MIB};
    for (uint32_t index = 0U; index < 3U; index++) {
        PP_CHECK_EQUAL(atomic_load(&fake->lengths[index]), sizes[index]);
    }
    check_registry(sizes, 3U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void pool_limited_to_an_eighth(void) {
    // One eighth of a memory limit of 3 GiB is 384 MiB.
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    const pp_jit_status_t status = request(PP_JIT_POOL_MAX_MIB, 3072U, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(status.requested_mib, PP_JIT_POOL_MAX_MIB);
    PP_CHECK_EQUAL(status.pool_mib, 384U);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 3U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void below_the_minimum_fails(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    const pp_jit_status_t status = request(PP_JIT_POOL_DEFAULT_MIB, 2047U, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_MEMORY_LIMIT);
    PP_CHECK_EQUAL(status.pool_mib, 0U);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 0U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void region_refused_fails(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->refuse_region, 1U);
    pp_start_host(NULL);
    const pp_jit_status_t status = request(PP_JIT_POOL_DEFAULT_MIB, 8192U, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_REGION_REFUSED);
    PP_CHECK_EQUAL(status.regions_mapped, 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 2U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void debugger_lost_fails(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->lose_after_region, 0U);
    pp_start_host(NULL);
    const pp_jit_status_t status = request(PP_JIT_POOL_DEFAULT_MIB, 8192U, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_DEBUGGER_LOST);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
    // Nothing went out once the debugger had gone.
    PP_CHECK_EQUAL(atomic_load(&fake->commands_without_debugger), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"alias_is_coherent", alias_is_coherent},
        {"no_view_is_writable_and_executable", no_view_is_writable_and_executable},
        {"writable_executable_is_found", writable_executable_is_found},
        {"commands_per_region", commands_per_region},
        {"pool_of_a_title", pool_of_a_title},
        {"pool_limited_to_an_eighth", pool_limited_to_an_eighth},
        {"below_the_minimum_fails", below_the_minimum_fails},
        {"region_refused_fails", region_refused_fails},
        {"debugger_lost_fails", debugger_lost_fails},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
