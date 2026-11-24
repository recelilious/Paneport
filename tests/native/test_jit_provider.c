// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S3.1.5 and S3.1.2: the whole life cycle of the provider on the macOS backend. With the
// backend's own simulated broker the state reaches ready; with the fake platform every change
// of the state reaches the event callback on the host event thread, in order, and the pool
// timeout and the detach timeout of C-0006 section 3 drive the state to failed.

#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "jit/jit_map.h"
#include "jit/jit_provider.h"
#include "jit_test_support.h"
#include "paneport/host.h"
#include "paneport/jit.h"
#include "pp_check.h"
#include "test_support.h"

static void start_with_events(pp_jit_events_t *events) {
    memset(events, 0, sizeof *events);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_event_callback(pp_jit_record_event, events), PP_OK);
    pp_jit_set_limit_mib(8192U);
}

static void request_when_attached(uint32_t size_mib) {
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_jit_request_pool(size_mib), PP_OK);
}

static void simulated_broker_reaches_ready(void) {
    // No fake: the macOS backend of the build, as the harness and the binding tests use it.
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_DEFAULT_MIB);
    const pp_jit_status_t status = pp_jit_wait_for_state(PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_NONE);
    PP_CHECK_EQUAL(status.pool_mib, 512U);
    PP_CHECK_EQUAL(status.regions_planned, 4U);
    PP_CHECK_EQUAL(status.regions_mapped, 4U);
    PP_CHECK_EQUAL(status.debugged_since_launch, 1U);
    PP_CHECK_EQUAL(status.attached_now, 0U);
    PP_CHECK_EQUAL(status.foreign_debugger, 0U);
    PP_CHECK(status.authorization_ms >=
             status.requesting_ms + status.waiting_detach_ms + status.verifying_ms);
    PP_CHECK(pp_jit_is_ready());

    pp_jit_pool_region_t regions[PP_JIT_MAX_REGIONS];
    uint32_t count = 0U;
    pp_jit_pool_copy(regions, PP_JIT_MAX_REGIONS, &count);
    PP_CHECK_EQUAL(count, 4U);
    for (uint32_t index = 0U; index < count; index++) {
        PP_CHECK(
            !pp_jit_map_has_writable_executable(regions[index].exec_address, regions[index].size));
        PP_CHECK(
            !pp_jit_map_has_writable_executable(regions[index].write_address, regions[index].size));
    }
    // pp_shutdown delivers what is queued, so every event has arrived once it returns.
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    const pp_jit_state_t expected[] = {
        PP_JIT_STATE_DEBUGGER_ATTACHED, PP_JIT_STATE_REQUESTING, PP_JIT_STATE_WAITING_DETACH,
        PP_JIT_STATE_VERIFYING,         PP_JIT_STATE_READY,
    };
    pp_jit_check_events(&events, expected, 5U);
}

static void event_on_every_change(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_jit_events_t events;
    start_with_events(&events);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    // The broker leaves before the request, and attaches again.
    atomic_store(&fake->attached, false);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_NOT_DEBUGGED);
    atomic_store(&fake->attached, true);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_MIN_MIB), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    const pp_jit_state_t expected[] = {
        PP_JIT_STATE_DEBUGGER_ATTACHED,
        PP_JIT_STATE_NOT_DEBUGGED,
        PP_JIT_STATE_DEBUGGER_ATTACHED,
        PP_JIT_STATE_REQUESTING,
        PP_JIT_STATE_WAITING_DETACH,
        PP_JIT_STATE_VERIFYING,
        PP_JIT_STATE_READY,
    };
    pp_jit_check_events(&events, expected, 7U);
}

static void pool_timeout_fails(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->hold, true);
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_DEFAULT_MIB);
    pp_wait_for_flag(&fake->holding);

    pp_fake_advance(fake, PP_JIT_POOL_TIMEOUT_MS - 1U);
    pp_sleep_milliseconds(50);
    PP_CHECK_EQUAL(pp_jit_read_status().state, PP_JIT_STATE_REQUESTING);
    pp_fake_advance(fake, 1U);
    const pp_jit_status_t status = pp_jit_wait_for_state(PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_POOL_TIMEOUT);
    PP_CHECK_EQUAL(status.requesting_ms, PP_JIT_POOL_TIMEOUT_MS);

    // The command that came back too late is the last one: nothing goes out once failed.
    atomic_store(&fake->hold, false);
    pp_sleep_milliseconds(100);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
    PP_CHECK_EQUAL(pp_jit_read_status().regions_mapped, 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    const pp_jit_state_t expected[] = {
        PP_JIT_STATE_DEBUGGER_ATTACHED,
        PP_JIT_STATE_REQUESTING,
        PP_JIT_STATE_FAILED,
    };
    pp_jit_check_events(&events, expected, 3U);
}

static void detach_timeout_fails(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->detach_on_command, false);
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_DEFAULT_MIB);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_WAITING_DETACH);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 1U);

    pp_fake_advance(fake, PP_JIT_DETACH_TIMEOUT_MS - 1U);
    pp_sleep_milliseconds(50);
    PP_CHECK_EQUAL(pp_jit_read_status().state, PP_JIT_STATE_WAITING_DETACH);
    pp_fake_advance(fake, 1U);
    const pp_jit_status_t status = pp_jit_wait_for_state(PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_DETACH_TIMEOUT);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    const pp_jit_state_t expected[] = {
        PP_JIT_STATE_DEBUGGER_ATTACHED,
        PP_JIT_STATE_REQUESTING,
        PP_JIT_STATE_WAITING_DETACH,
        PP_JIT_STATE_FAILED,
    };
    pp_jit_check_events(&events, expected, 4U);
}

static void ready_refuses_another_request(void) {
    (void)pp_fake_jit_install();
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_MIN_MIB);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_READY);
    // The pool is fixed for the life of the process (C-0006 section 3 step 8).
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_MIN_MIB), PP_E_JIT_STATE);
    PP_CHECK(strstr(pp_last_error(), "ready") != NULL);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_E_JIT_STATE);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(0U), PP_E_JIT_STATE);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    PP_CHECK_EQUAL(pp_jit_read_status().state, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void failed_refuses_everything(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->refuse_region, 0U);
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_DEFAULT_MIB);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_FAILED);
    // Leaving failed needs a relaunch (C-0006 section 5).
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_E_JIT_STATE);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB), PP_E_JIT_STATE);
    atomic_store(&fake->attached, true);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    const pp_jit_status_t status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(status.failure, PP_JIT_FAILURE_REGION_REFUSED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    // The change to failed that the refused region makes is reported like any other.
    const pp_jit_state_t expected[] = {
        PP_JIT_STATE_DEBUGGER_ATTACHED,
        PP_JIT_STATE_REQUESTING,
        PP_JIT_STATE_FAILED,
    };
    pp_jit_check_events(&events, expected, 3U);
}

static void enabler_taken_back_is_reported(void) {
    (void)pp_fake_jit_install();
    pp_jit_events_t events;
    start_with_events(&events);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    // The app takes the enabler back: the debugger no longer counts as the broker.
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(0U), PP_OK);
    PP_CHECK_EQUAL(pp_jit_read_status().state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    const pp_jit_state_t expected[] = {
        PP_JIT_STATE_DEBUGGER_ATTACHED,
        PP_JIT_STATE_NOT_DEBUGGED,
    };
    pp_jit_check_events(&events, expected, 2U);
}

static void commands_after_shutdown(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->hold, true);
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_DEFAULT_MIB);
    pp_wait_for_flag(&fake->holding);
    // pp_shutdown returns while command 1 for the first region is still in the debugger.
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 1U);
    atomic_store(&fake->hold, false);
    pp_sleep_milliseconds(200);
    // The request thread sends nothing more and keeps no region (jit_provider.h).
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
    pp_jit_pool_region_t regions[PP_JIT_MAX_REGIONS];
    uint32_t count = PP_JIT_MAX_REGIONS;
    pp_jit_pool_copy(regions, PP_JIT_MAX_REGIONS, &count);
    PP_CHECK_EQUAL(count, 0U);
}

static _Atomic(bool) s_between_commands;
static _Atomic(bool) s_go_on;

static void stop_after_first_region(uint32_t index) {
    if (index == 0U) {
        atomic_store(&s_between_commands, true);
        while (!atomic_load(&s_go_on)) {
            pp_sleep_milliseconds(1);
        }
    }
}

static void shutdown_between_commands(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_jit_provider_on_region_kept(stop_after_first_region);
    pp_jit_events_t events;
    start_with_events(&events);
    request_when_attached(PP_JIT_POOL_DEFAULT_MIB);
    pp_wait_for_flag(&s_between_commands);
    // pp_shutdown returns after the first region was kept and before command 1 of the next.
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    atomic_store(&s_go_on, true);
    pp_sleep_milliseconds(200);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
}

static void shutdown_stops_the_supervisor(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    // No broker ever comes, so the supervisor would read the flags for ever.
    atomic_store(&fake->attach_on_enabler, false);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    pp_sleep_milliseconds(50);
    PP_CHECK(atomic_load(&fake->reads) > 2U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    const uint32_t reads = atomic_load(&fake->reads);
    pp_sleep_milliseconds(50);
    PP_CHECK_EQUAL(atomic_load(&fake->reads), reads);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"simulated_broker_reaches_ready", simulated_broker_reaches_ready},
        {"event_on_every_change", event_on_every_change},
        {"pool_timeout_fails", pool_timeout_fails},
        {"detach_timeout_fails", detach_timeout_fails},
        {"ready_refuses_another_request", ready_refuses_another_request},
        {"failed_refuses_everything", failed_refuses_everything},
        {"enabler_taken_back_is_reported", enabler_taken_back_is_reported},
        {"commands_after_shutdown", commands_after_shutdown},
        {"shutdown_between_commands", shutdown_between_commands},
        {"shutdown_stops_the_supervisor", shutdown_stops_the_supervisor},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
