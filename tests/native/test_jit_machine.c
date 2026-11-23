// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S3.1.2: the state machine of the JIT request protocol on its own, with a clock of the test:
// every transition of C-0006 section 5 in order, both timeouts at their limits, the terminal
// failed state, and the pool sizes of C-0006 section 4.

#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "jit/jit_machine.h"
#include "pp_check.h"

#define MIB (UINT64_C(1024) * 1024U)
#define GIB (UINT64_C(1024) * MIB)

static const pp_jit_flags_t k_clear = {.debugged_since_launch = false, .attached_now = false};
static const pp_jit_flags_t k_broker = {.debugged_since_launch = true, .attached_now = true};
static const pp_jit_flags_t k_left = {.debugged_since_launch = true, .attached_now = false};

// A machine in debugger_attached at 2,000 ms, after the enabler opened at 1,000 ms.
static pp_jit_machine_t attached_machine(void) {
    pp_jit_machine_t machine;
    pp_jit_machine_init(&machine, 0U);
    PP_CHECK(pp_jit_machine_set_enabler(&machine, true, k_clear, 1000U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    pp_jit_machine_apply_flags(&machine, k_broker, 2000U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_DEBUGGER_ATTACHED);
    return machine;
}

// A machine in requesting at 3,000 ms, for a pool of 512 MiB in four regions.
static pp_jit_machine_t requesting_machine(void) {
    pp_jit_machine_t machine = attached_machine();
    PP_CHECK(pp_jit_machine_request(&machine, PP_JIT_POOL_DEFAULT_MIB, 8U * GIB, 3000U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);
    PP_CHECK_EQUAL(machine.regions_planned, 4U);
    return machine;
}

static void complete_pool(pp_jit_machine_t *machine, uint64_t now_ms) {
    for (uint32_t index = 0U; index < machine->regions_planned; index++) {
        pp_jit_machine_region_mapped(machine, now_ms);
    }
    PP_CHECK_EQUAL(machine->regions_mapped, machine->regions_planned);
}

static void transitions_in_order(void) {
    pp_jit_machine_t machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 5000U);
    PP_CHECK(machine.detach_sent);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);
    pp_jit_machine_detach_returned(&machine, 5500U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_WAITING_DETACH);
    // The broker is leaving, as command 0 asked, so the reading that is still attached changes
    // nothing, and the clear one starts the probe.
    pp_jit_machine_apply_flags(&machine, k_broker, 5600U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_WAITING_DETACH);
    pp_jit_machine_apply_flags(&machine, k_left, 6000U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_VERIFYING);
    pp_jit_machine_verified(&machine, true, 6500U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_NONE);

    pp_jit_status_t status;
    pp_jit_machine_status(&machine, 7000U, &status);
    PP_CHECK_EQUAL(status.struct_size, sizeof(pp_jit_status_t));
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(status.pool_mib, 512U);
    PP_CHECK_EQUAL(status.requested_mib, 512U);
    PP_CHECK_EQUAL(status.regions_planned, 4U);
    PP_CHECK_EQUAL(status.regions_mapped, 4U);
    PP_CHECK_EQUAL(status.debugger_attached_ms, 1000U);
    PP_CHECK_EQUAL(status.requesting_ms, 2500U);
    PP_CHECK_EQUAL(status.waiting_detach_ms, 500U);
    PP_CHECK_EQUAL(status.verifying_ms, 500U);
    PP_CHECK_EQUAL(status.authorization_ms, 3500U);
    PP_CHECK_EQUAL(status.in_state_ms, 500U);
    PP_CHECK_EQUAL(status.debugged_since_launch, 1U);
    PP_CHECK_EQUAL(status.attached_now, 0U);
    PP_CHECK_EQUAL(status.enabler_opened, 1U);
    PP_CHECK_EQUAL(status.foreign_debugger, 0U);
}

static void broker_left_before_request(void) {
    pp_jit_machine_t machine = attached_machine();
    pp_jit_machine_apply_flags(&machine, k_left, 2500U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    // A second attach of the broker after the first still counts as attaching afterwards.
    pp_jit_machine_apply_flags(&machine, k_broker, 3000U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_DEBUGGER_ATTACHED);
    pp_jit_status_t status;
    pp_jit_machine_status(&machine, 3000U, &status);
    PP_CHECK_EQUAL(status.debugger_attached_ms, 500U);
}

static void foreign_debuggers(void) {
    // Attached before the app opened the enabler: foreign, until it detaches.
    pp_jit_machine_t machine;
    pp_jit_machine_init(&machine, 0U);
    pp_jit_machine_apply_flags(&machine, k_broker, 100U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK(pp_jit_machine_foreign(&machine));
    PP_CHECK(pp_jit_machine_set_enabler(&machine, true, k_broker, 200U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK(pp_jit_machine_foreign(&machine));
    pp_jit_machine_apply_flags(&machine, k_left, 300U);
    PP_CHECK(!pp_jit_machine_foreign(&machine));
    pp_jit_machine_apply_flags(&machine, k_broker, 400U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_DEBUGGER_ATTACHED);

    // Attached without the enabler: foreign.
    pp_jit_machine_init(&machine, 0U);
    pp_jit_machine_apply_flags(&machine, k_clear, 100U);
    pp_jit_machine_apply_flags(&machine, k_broker, 200U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK(pp_jit_machine_foreign(&machine));

    // The enabler did not open, and a debugger attached afterwards: foreign.
    pp_jit_machine_init(&machine, 0U);
    PP_CHECK(pp_jit_machine_set_enabler(&machine, true, k_clear, 100U));
    PP_CHECK(pp_jit_machine_set_enabler(&machine, false, k_clear, 200U));
    pp_jit_machine_apply_flags(&machine, k_broker, 300U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK(pp_jit_machine_foreign(&machine));

    // Taking the enabler back in debugger_attached: the attached debugger is not the broker.
    machine = attached_machine();
    PP_CHECK(pp_jit_machine_set_enabler(&machine, false, k_broker, 2100U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK(pp_jit_machine_foreign(&machine));

    // Attached, but CS_DEBUGGED not set yet: neither foreign nor the broker, until it is.
    pp_jit_machine_init(&machine, 0U);
    PP_CHECK(pp_jit_machine_set_enabler(&machine, true, k_clear, 100U));
    const pp_jit_flags_t traced_only = {.debugged_since_launch = false, .attached_now = true};
    pp_jit_machine_apply_flags(&machine, traced_only, 200U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK(!pp_jit_machine_foreign(&machine));
    pp_jit_machine_apply_flags(&machine, k_broker, 300U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_DEBUGGER_ATTACHED);

    // After the detach, any debugger is another one.
    machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 4000U);
    pp_jit_machine_detach_returned(&machine, 4100U);
    pp_jit_machine_apply_flags(&machine, k_left, 4200U);
    pp_jit_machine_verified(&machine, true, 4300U);
    pp_jit_machine_apply_flags(&machine, k_broker, 4400U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_READY);
    PP_CHECK(pp_jit_machine_foreign(&machine));
}

static void pool_timeout(void) {
    pp_jit_machine_t machine = requesting_machine();
    pp_jit_machine_region_mapped(&machine, 10000U);
    pp_jit_machine_tick(&machine, 3000U + PP_JIT_POOL_TIMEOUT_MS - 1U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);
    pp_jit_machine_tick(&machine, 3000U + PP_JIT_POOL_TIMEOUT_MS);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_POOL_TIMEOUT);

    // A complete pool is not timed out by the pool timeout.
    machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_tick(&machine, 3000U + (10U * PP_JIT_POOL_TIMEOUT_MS));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);
}

static void detach_timeout(void) {
    // Command 0 never returns.
    pp_jit_machine_t machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 5000U);
    pp_jit_machine_tick(&machine, 5000U + PP_JIT_DETACH_TIMEOUT_MS - 1U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);
    pp_jit_machine_tick(&machine, 5000U + PP_JIT_DETACH_TIMEOUT_MS);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_DETACH_TIMEOUT);

    // Command 0 returns, but the debugger stays.
    machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 5000U);
    pp_jit_machine_detach_returned(&machine, 6000U);
    pp_jit_machine_apply_flags(&machine, k_broker, 20000U);
    pp_jit_machine_tick(&machine, 5000U + PP_JIT_DETACH_TIMEOUT_MS - 1U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_WAITING_DETACH);
    pp_jit_machine_tick(&machine, 5000U + PP_JIT_DETACH_TIMEOUT_MS);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_DETACH_TIMEOUT);
}

static void debugger_lost(void) {
    pp_jit_machine_t machine = requesting_machine();
    pp_jit_machine_region_mapped(&machine, 3500U);
    pp_jit_machine_apply_flags(&machine, k_left, 3600U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_DEBUGGER_LOST);

    // After command 0, leaving is what the host asked for.
    machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 4100U);
    pp_jit_machine_apply_flags(&machine, k_left, 4200U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);
    pp_jit_machine_detach_returned(&machine, 4300U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_WAITING_DETACH);
}

static void probe_failure(void) {
    pp_jit_machine_t machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 4100U);
    pp_jit_machine_detach_returned(&machine, 4200U);
    pp_jit_machine_apply_flags(&machine, k_left, 4300U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_VERIFYING);
    pp_jit_machine_verified(&machine, false, 4400U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_VERIFICATION);
    PP_CHECK_EQUAL(machine.authorization_ms, 0U);
}

static void failed_is_terminal(void) {
    pp_jit_machine_t machine = requesting_machine();
    pp_jit_machine_fail(&machine, PP_JIT_FAILURE_REGION_REFUSED, 3500U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    pp_jit_machine_apply_flags(&machine, k_clear, 3600U);
    pp_jit_machine_apply_flags(&machine, k_broker, 3700U);
    PP_CHECK(!pp_jit_machine_request(&machine, 512U, 8U * GIB, 3800U));
    PP_CHECK(!pp_jit_machine_set_enabler(&machine, true, k_clear, 3900U));
    PP_CHECK(!pp_jit_machine_set_enabler(&machine, false, k_clear, 3900U));
    pp_jit_machine_region_mapped(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 4000U);
    pp_jit_machine_detach_returned(&machine, 4000U);
    pp_jit_machine_verified(&machine, true, 4000U);
    pp_jit_machine_fail(&machine, PP_JIT_FAILURE_SYSTEM, 4000U);
    pp_jit_machine_tick(&machine, 1000000U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_REGION_REFUSED);

    // Ready stays ready.
    machine = requesting_machine();
    complete_pool(&machine, 4000U);
    pp_jit_machine_detach_sent(&machine, 4100U);
    pp_jit_machine_detach_returned(&machine, 4200U);
    pp_jit_machine_apply_flags(&machine, k_left, 4300U);
    pp_jit_machine_verified(&machine, true, 4400U);
    pp_jit_machine_fail(&machine, PP_JIT_FAILURE_SYSTEM, 4500U);
    pp_jit_machine_apply_flags(&machine, k_broker, 4600U);
    pp_jit_machine_tick(&machine, 1000000U);
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_READY);
}

static void request_only_when_attached(void) {
    pp_jit_machine_t machine;
    pp_jit_machine_init(&machine, 0U);
    PP_CHECK(!pp_jit_machine_request(&machine, 512U, 8U * GIB, 100U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(machine.requested_mib, 0U);

    machine = requesting_machine();
    PP_CHECK(!pp_jit_machine_request(&machine, 512U, 8U * GIB, 3100U));
    PP_CHECK(!pp_jit_machine_set_enabler(&machine, true, k_broker, 3100U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_REQUESTING);

    // The enabler opens only once per wait: debugger_attached refuses another opening.
    machine = attached_machine();
    PP_CHECK(!pp_jit_machine_set_enabler(&machine, true, k_broker, 2100U));
    PP_CHECK(pp_jit_machine_takes_enabler(&machine, false));
}

static void memory_limit_fails(void) {
    pp_jit_machine_t machine = attached_machine();
    PP_CHECK(pp_jit_machine_request(&machine, 512U, (2U * GIB) - MIB, 3000U));
    PP_CHECK_EQUAL(machine.state, PP_JIT_STATE_FAILED);
    PP_CHECK_EQUAL(machine.failure, PP_JIT_FAILURE_MEMORY_LIMIT);
    PP_CHECK_EQUAL(machine.pool_mib, 0U);
    PP_CHECK_EQUAL(machine.requested_mib, 512U);
}

static void pool_sizes(void) {
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(PP_JIT_POOL_DEFAULT_MIB, 8U * GIB), 512U);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(100U, 8U * GIB), PP_JIT_POOL_MIN_MIB);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(2000U, 16U * GIB), PP_JIT_POOL_MAX_MIB);
    // A title's jitPoolMiB within the limits is kept.
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(300U, 8U * GIB), 300U);
    // One eighth of the memory limit, in whole MiB.
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(512U, 3U * GIB), 384U);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(896U, 6U * GIB), 768U);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(512U, (3U * GIB) + (7U * MIB)), 384U);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(512U, 2U * GIB), 256U);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(512U, (2U * GIB) - MIB), 0U);
    PP_CHECK_EQUAL(pp_jit_pool_size_mib(512U, 0U), 0U);

    PP_CHECK_EQUAL(pp_jit_region_count(512U), 4U);
    PP_CHECK_EQUAL(pp_jit_region_count(896U), 7U);
    PP_CHECK_EQUAL(pp_jit_region_count(300U), 3U);
    PP_CHECK_EQUAL(pp_jit_region_count(256U), 2U);
    PP_CHECK_EQUAL(pp_jit_region_bytes(300U, 0U), 128U * MIB);
    PP_CHECK_EQUAL(pp_jit_region_bytes(300U, 1U), 128U * MIB);
    PP_CHECK_EQUAL(pp_jit_region_bytes(300U, 2U), 44U * MIB);
    PP_CHECK_EQUAL(pp_jit_region_bytes(300U, 3U), 0U);
    PP_CHECK(PP_JIT_MAX_REGIONS >= pp_jit_region_count(PP_JIT_POOL_MAX_MIB));
}

static void names(void) {
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_NOT_DEBUGGED), "not_debugged");
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_DEBUGGER_ATTACHED), "debugger_attached");
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_REQUESTING), "requesting");
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_WAITING_DETACH), "waiting_detach");
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_VERIFYING), "verifying");
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_READY), "ready");
    PP_CHECK_TEXT(pp_jit_state_name(PP_JIT_STATE_FAILED), "failed");
    PP_CHECK_TEXT(pp_jit_state_name(7), "an unknown state");
    for (int32_t failure = PP_JIT_FAILURE_NONE; failure <= PP_JIT_FAILURE_SYSTEM; failure++) {
        PP_CHECK(strcmp(pp_jit_failure_text(failure), "An unknown failure.") != 0);
    }
    PP_CHECK_TEXT(pp_jit_failure_text(99), "An unknown failure.");
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"transitions_in_order", transitions_in_order},
        {"broker_left_before_request", broker_left_before_request},
        {"foreign_debuggers", foreign_debuggers},
        {"pool_timeout", pool_timeout},
        {"detach_timeout", detach_timeout},
        {"debugger_lost", debugger_lost},
        {"probe_failure", probe_failure},
        {"failed_is_terminal", failed_is_terminal},
        {"request_only_when_attached", request_only_when_attached},
        {"memory_limit_fails", memory_limit_fails},
        {"pool_sizes", pool_sizes},
        {"names", names},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
