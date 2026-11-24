// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S3.1.4: the verification probe, through the signal dispatcher's JIT probe route. It runs
// the first page, the last page and one page in every 16 MiB of each region; it passes on a
// healthy pool, and reports a failure when a fault is planted on any of those pages, while a
// fault on a page that it does not run goes unseen. Through the provider, a planted fault
// makes the state failed.

#include <mach/mach.h>
#include <mach/vm_map.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "jit/jit_map.h"
#include "jit/jit_platform.h"
#include "jit/jit_probe.h"
#include "jit/jit_provider.h"
#include "jit_test_support.h"
#include "paneport/jit.h"
#include "pp_check.h"
#include "signals/signals.h"
#include "test_support.h"

#define MIB (UINT64_C(1024) * 1024U)

// Two regions as the provider maps them: 128 MiB and the 44 MiB rest of a pool of 300 MiB.
typedef struct probe_pool_t {
    pp_jit_pool_region_t regions[2];
} probe_pool_t;

static probe_pool_t make_pool(void) {
    probe_pool_t pool;
    memset(&pool, 0, sizeof pool);
    const uint64_t sizes[2] = {128U * MIB, 44U * MIB};
    for (uint32_t index = 0U; index < 2U; index++) {
        pp_jit_pool_region_t *region = &pool.regions[index];
        region->size = sizes[index];
        region->exec_address = pp_jit_macos_prepare_region(region->size);
        PP_CHECK(region->exec_address != 0U);
        pp_jit_map_result_t mapped;
        PP_CHECK_EQUAL(pp_jit_map_alias(region->exec_address, region->size, &mapped),
                       PP_JIT_FAILURE_NONE);
        region->write_address = mapped.write_address;
        PP_CHECK_EQUAL(
            pp_signals_add_pool_range(region->exec_address, region->size, &region->exec_range),
            PP_OK);
        PP_CHECK_EQUAL(
            pp_signals_add_pool_range(region->write_address, region->size, &region->write_range),
            PP_OK);
    }
    return pool;
}

static uint64_t probe_handled(void) {
    pp_signal_counters_t counters;
    memset(&counters, 0, sizeof counters);
    counters.struct_size = (uint32_t)sizeof counters;
    pp_signals_read_counters(&counters);
    return counters.probe_handled;
}

static void poison(uint64_t address, vm_prot_t protection) {
    PP_CHECK_EQUAL(vm_protect(mach_task_self(), (vm_address_t)address,
                              (vm_size_t)pp_jit_page_size(), FALSE, protection),
                   KERN_SUCCESS);
}

static void probed_offsets(void) {
    const uint64_t page = pp_jit_page_size();
    uint64_t offsets[PP_JIT_PROBE_MAX_PAGES];
    PP_CHECK_EQUAL(pp_jit_probe_offsets(128U * MIB, page, offsets, PP_JIT_PROBE_MAX_PAGES), 9U);
    for (uint32_t index = 0U; index < 8U; index++) {
        PP_CHECK_EQUAL(offsets[index], (uint64_t)index * 16U * MIB);
    }
    PP_CHECK_EQUAL(offsets[8], (128U * MIB) - page);
    PP_CHECK_EQUAL(pp_jit_probe_offsets(44U * MIB, page, offsets, PP_JIT_PROBE_MAX_PAGES), 4U);
    PP_CHECK_EQUAL(offsets[2], 32U * MIB);
    PP_CHECK_EQUAL(offsets[3], (44U * MIB) - page);
    PP_CHECK_EQUAL(pp_jit_probe_offsets(page, page, offsets, PP_JIT_PROBE_MAX_PAGES), 1U);
    PP_CHECK_EQUAL(offsets[0], 0U);
    PP_CHECK_EQUAL(pp_jit_probe_offsets(page / 2U, page, offsets, PP_JIT_PROBE_MAX_PAGES), 0U);
    PP_CHECK_EQUAL(pp_jit_probe_offsets(128U * MIB, page, offsets, 3U), 3U);
    PP_CHECK_EQUAL(PP_JIT_PROBE_INTERVAL, 16U * MIB);
    PP_CHECK_EQUAL(pp_jit_page_size(), 16384U);
}

static void healthy_pool_passes(void) {
    pp_start_host(NULL);
    const probe_pool_t pool = make_pool();
    const uint64_t handled = probe_handled();
    pp_jit_probe_result_t result;
    pp_jit_probe_run(pool.regions, 2U, &result);
    PP_CHECK(result.passed);
    PP_CHECK_EQUAL(result.pages, 9U + 4U);
    PP_CHECK_EQUAL(result.faults, 0U);
    PP_CHECK_EQUAL(probe_handled(), handled);
    PP_CHECK_EQUAL(atomic_load(&pp_jit_probe_window.phase), PP_JIT_PROBE_IDLE);
    // The probe leaves nothing behind: the probed pages hold zero words again.
    PP_CHECK_EQUAL(pp_jit_test_load_byte(pool.regions[0].exec_address), 0U);
    PP_CHECK_EQUAL(pp_jit_test_load_byte(pool.regions[1].exec_address + (16U * MIB)), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// Plants a fault on the view's page at offset of region, and checks that the probe names it.
static void check_planted_fault(uint32_t region, uint64_t offset) {
    pp_start_host(NULL);
    const probe_pool_t pool = make_pool();
    poison(pool.regions[region].exec_address + offset, VM_PROT_NONE);
    const uint64_t handled = probe_handled();
    pp_jit_probe_result_t result;
    pp_jit_probe_run(pool.regions, 2U, &result);
    PP_CHECK(!result.passed);
    PP_CHECK_EQUAL(result.region, region);
    PP_CHECK_EQUAL(result.offset, offset);
    PP_CHECK_EQUAL(result.returned, PP_JIT_PROBE_FAULTED);
    // Darwin reports a page that may not run as SIGBUS (the probe of M3.1 on the runner); the
    // cache operation on it, if that faults first, may report SIGSEGV.
    PP_CHECK(result.signal == SIGBUS || result.signal == SIGSEGV);
    PP_CHECK(result.faults >= 1U);
    PP_CHECK(probe_handled() >= handled + 1U);
    PP_CHECK_EQUAL(atomic_load(&pp_jit_probe_window.phase), PP_JIT_PROBE_IDLE);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void fault_on_first_page(void) { check_planted_fault(1U, 0U); }

static void fault_on_last_page(void) { check_planted_fault(0U, (128U * MIB) - pp_jit_page_size()); }

static void fault_on_interval_page(void) { check_planted_fault(0U, 48U * MIB); }

static void fault_off_the_probed_pages(void) {
    pp_start_host(NULL);
    const probe_pool_t pool = make_pool();
    poison(pool.regions[0].exec_address + (16U * MIB) + pp_jit_page_size(), VM_PROT_NONE);
    pp_jit_probe_result_t result;
    pp_jit_probe_run(pool.regions, 2U, &result);
    PP_CHECK(result.passed);
    PP_CHECK_EQUAL(result.faults, 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void routine_of_another_page_fails(void) {
    // The view runs what the test left on it: on the first page the first page's routine, and
    // on the next probed page the first page's routine again, as a view that shows another
    // page's code would. The probe's own routines go to the other region's alias, which nothing
    // runs. Nothing faults, so only the value of its own of each page can fail the second page.
    pp_start_host(NULL);
    const probe_pool_t pool = make_pool();
    const pp_jit_pool_region_t *view = &pool.regions[0];
    pp_jit_test_plant_routine(view->write_address, view->exec_address, PP_JIT_PROBE_FIRST_VALUE);
    pp_jit_test_plant_routine(view->write_address + (16U * MIB), view->exec_address + (16U * MIB),
                              PP_JIT_PROBE_FIRST_VALUE);
    pp_jit_pool_region_t crossed = pool.regions[1];
    crossed.exec_address = view->exec_address;
    pp_jit_probe_result_t result;
    pp_jit_probe_run(&crossed, 1U, &result);
    PP_CHECK(!result.passed);
    PP_CHECK_EQUAL(result.pages, 2U);
    PP_CHECK_EQUAL(result.region, 0U);
    PP_CHECK_EQUAL(result.offset, 16U * MIB);
    PP_CHECK_EQUAL(result.returned, PP_JIT_PROBE_FIRST_VALUE);
    PP_CHECK_EQUAL(result.signal, 0);
    PP_CHECK_EQUAL(result.faults, 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// Calls the probe handler for one fault in phase, at address and with the program counter pc.
static uint32_t handle_fault(uint32_t phase, uint64_t address, uint64_t pc) {
    atomic_store(&pp_jit_probe_window.phase, phase);
    pp_guest_fault_t fault;
    memset(&fault, 0, sizeof fault);
    fault.struct_size = (uint32_t)sizeof fault;
    fault.route = PP_FAULT_ROUTE_JIT_PROBE;
    fault.signal = SIGBUS;
    fault.fault_address = address;
    fault.program_counter = pc;
    // No context: the handler's recovery changes nothing, and its decision is all that counts.
    fault.context = NULL;
    const uint32_t decision = pp_jit_probe_handle_fault(&fault, NULL);
    atomic_store(&pp_jit_probe_window.phase, (uint32_t)PP_JIT_PROBE_IDLE);
    return decision;
}

static void handler_takes_only_its_window(void) {
    // The handler on its own: it takes a fault only where the current phase of the probe can
    // make one, and declines every other, which leaves its count alone.
    pp_jit_probe_window_t *window = &pp_jit_probe_window;
    const uint64_t page = pp_jit_page_size();
    const uint64_t write = UINT64_C(0x300000000);
    const uint64_t exec = UINT64_C(0x400000000);
    const uint64_t elsewhere = UINT64_C(0x500000000);
    atomic_store(&window->page_size, page);
    atomic_store(&window->write_start, write);
    atomic_store(&window->exec_start, exec);
    atomic_store(&window->faults, 0U);
    atomic_store(&window->first_signal, 0);
    const uint32_t handled = PP_GUEST_FAULT_HANDLED;
    const uint32_t declined = PP_GUEST_FAULT_DECLINED;

    // Idle: none, even on the page's own addresses.
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_IDLE, write, elsewhere), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_IDLE, exec, exec), declined);
    // Storing: only the routine's 8 bytes on the alias.
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_STORE, write, elsewhere), handled);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_STORE, write + 4U, elsewhere), handled);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_STORE, write + 8U, elsewhere), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_STORE, write - 1U, elsewhere), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_STORE, exec, elsewhere), declined);
    // Flushing: the page on either view.
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_FLUSH, write + page - 1U, elsewhere), handled);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_FLUSH, exec + 64U, elsewhere), handled);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_FLUSH, write + page, elsewhere), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_FLUSH, exec + page, elsewhere), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_FLUSH, elsewhere, elsewhere), declined);
    // Running: only a program counter on the page of the view, whatever the address.
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_RUN, elsewhere, exec), handled);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_RUN, exec, exec + page - 4U), handled);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_RUN, exec, exec + page), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_RUN, exec, exec - 4U), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_RUN, exec, write), declined);
    // A phase that does not exist takes nothing.
    PP_CHECK_EQUAL(handle_fault(7U, write, exec), declined);
    PP_CHECK_EQUAL(atomic_load(&window->faults), 6U);
    PP_CHECK_EQUAL(atomic_load(&window->first_signal), SIGBUS);

    // Without a page in the window, nothing is inside it.
    atomic_store(&window->write_start, 0U);
    atomic_store(&window->exec_start, 0U);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_STORE, 4U, elsewhere), declined);
    PP_CHECK_EQUAL(handle_fault(PP_JIT_PROBE_RUN, 4U, 4U), declined);
    PP_CHECK_EQUAL(atomic_load(&window->faults), 6U);
}

static void fault_on_the_alias(void) {
    // A write through the alias that faults fails the probe too.
    pp_start_host(NULL);
    const probe_pool_t pool = make_pool();
    poison(pool.regions[1].write_address + (32U * MIB), VM_PROT_READ);
    pp_jit_probe_result_t result;
    pp_jit_probe_run(pool.regions, 2U, &result);
    PP_CHECK(!result.passed);
    PP_CHECK_EQUAL(result.region, 1U);
    PP_CHECK_EQUAL(result.offset, 32U * MIB);
    PP_CHECK_EQUAL(result.returned, PP_JIT_PROBE_FAULTED);
    PP_CHECK(result.signal == SIGBUS || result.signal == SIGSEGV);
    PP_CHECK(result.faults >= 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void provider_fails_on_a_planted_fault(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    // The broker stays after command 0 until the test lets it go, so the fault is planted
    // after the mapping and before the probe.
    atomic_store(&fake->detach_on_command, false);
    pp_start_host(NULL);
    pp_jit_set_limit_mib(8192U);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_WAITING_DETACH);

    pp_jit_pool_region_t regions[PP_JIT_MAX_REGIONS];
    uint32_t count = 0U;
    pp_jit_pool_copy(regions, PP_JIT_MAX_REGIONS, &count);
    PP_CHECK_EQUAL(count, 4U);
    poison(regions[2].exec_address + (112U * MIB), VM_PROT_NONE);
    const uint64_t handled = probe_handled();
    atomic_store(&fake->attached, false);

    const pp_jit_status_t status = pp_jit_wait_for_state(PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_VERIFICATION);
    PP_CHECK(probe_handled() >= handled + 1U);
    PP_CHECK(!pp_jit_is_ready());
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"probed_offsets", probed_offsets},
        {"healthy_pool_passes", healthy_pool_passes},
        {"fault_on_first_page", fault_on_first_page},
        {"fault_on_last_page", fault_on_last_page},
        {"fault_on_interval_page", fault_on_interval_page},
        {"fault_off_the_probed_pages", fault_off_the_probed_pages},
        {"routine_of_another_page_fails", routine_of_another_page_fails},
        {"handler_takes_only_its_window", handler_takes_only_its_window},
        {"fault_on_the_alias", fault_on_the_alias},
        {"provider_fails_on_a_planted_fault", provider_fails_on_a_planted_fault},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
