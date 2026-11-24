// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S3.1.1: the debugger flags. The decoding of CS_DEBUGGED and P_TRACED, the system's reader on
// a process without a debugger, and, with the fake platform, the broker-recognition rule of
// C-0006 section 3 step 2 and the reading again on every return to the foreground.

#include <pthread.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "jit/jit_platform.h"
#include "jit_test_support.h"
#include "paneport/jit.h"
#include "pp_check.h"
#include "test_support.h"

static void decodes_both_flags(void) {
    pp_jit_flags_t flags = pp_jit_decode_flags(0x10000000U, 0);
    PP_CHECK(flags.debugged_since_launch);
    PP_CHECK(!flags.attached_now);
    flags = pp_jit_decode_flags(0U, 0x800);
    PP_CHECK(!flags.debugged_since_launch);
    PP_CHECK(flags.attached_now);
    // The words that the probe of M3.1 read on the macOS runner under lldb, and without it.
    flags = pp_jit_decode_flags(0x32020021U, 0x5804);
    PP_CHECK(flags.debugged_since_launch);
    PP_CHECK(flags.attached_now);
    flags = pp_jit_decode_flags(0x22020001U, 0x4004);
    PP_CHECK(!flags.debugged_since_launch);
    PP_CHECK(!flags.attached_now);
}

static void system_reader_without_debugger(void) {
    pp_jit_flags_t flags = {.debugged_since_launch = true, .attached_now = true};
    PP_CHECK(pp_jit_read_darwin_flags(&flags));
    // CTest runs the case with no debugger, and none attached since it started.
    PP_CHECK(!flags.attached_now);
    PP_CHECK(!flags.debugged_since_launch);
    PP_CHECK(pp_jit_monotonic_ms() > 0U);
}

static void broker_after_enabler(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_read_status().state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    const pp_jit_status_t status = pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(status.enabler_opened, 1U);
    PP_CHECK_EQUAL(status.attached_now, 1U);
    PP_CHECK_EQUAL(status.debugged_since_launch, 1U);
    PP_CHECK_EQUAL(status.foreign_debugger, 0U);
    PP_CHECK(atomic_load(&fake->reads) >= 2U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void debugger_before_enabler_is_foreign(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->debugged, true);
    atomic_store(&fake->attached, true);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    pp_jit_status_t status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(status.foreign_debugger, 1U);

    // Opening the enabler while it is attached does not make it the broker.
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    pp_sleep_milliseconds(50);
    status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(status.foreign_debugger, 1U);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 0U);

    // Once it detached, a debugger that attaches is the broker.
    atomic_store(&fake->attached, false);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    PP_CHECK_EQUAL(pp_jit_read_status().foreign_debugger, 0U);
    atomic_store(&fake->attached, true);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    PP_CHECK_EQUAL(pp_jit_read_status().state, PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void debugger_without_enabler_is_foreign(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    atomic_store(&fake->debugged, true);
    atomic_store(&fake->attached, true);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    const pp_jit_status_t status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(status.foreign_debugger, 1U);
    PP_CHECK_EQUAL(status.enabler_opened, 0U);
    // No command goes to a foreign debugger.
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB), PP_E_JIT_STATE);
    PP_CHECK_EQUAL(atomic_load(&fake->prepares), 0U);
    PP_CHECK_EQUAL(atomic_load(&fake->detaches), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void refused_enabler_makes_debugger_foreign(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->attach_on_enabler, false);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(0U), PP_OK);
    atomic_store(&fake->debugged, true);
    atomic_store(&fake->attached, true);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    pp_sleep_milliseconds(50);
    const pp_jit_status_t status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(status.foreign_debugger, 1U);
    PP_CHECK_EQUAL(status.enabler_opened, 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void waits_for_cs_debugged(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    atomic_store(&fake->attach_on_enabler, false);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    atomic_store(&fake->attached, true);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    pp_sleep_milliseconds(50);
    pp_jit_status_t status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(status.foreign_debugger, 0U);
    atomic_store(&fake->debugged, true);
    status = pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(status.debugged_since_launch, 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void foreground_reads_again(void) {
    pp_fake_jit_t *fake = pp_fake_jit_install();
    pp_start_host(NULL);
    // No supervisor runs before the enabler opens, so only the notice reads the flags.
    pp_sleep_milliseconds(20);
    const uint32_t before = atomic_load(&fake->reads);
    PP_CHECK_EQUAL(before, 0U);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&fake->reads), 1U);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&fake->reads), 2U);

    // In the ready state the reading is recorded, and the state stays.
    pp_jit_set_limit_mib(8192U);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_DEBUGGER_ATTACHED);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_MIN_MIB), PP_OK);
    (void)pp_jit_wait_for_state(PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(pp_jit_read_status().attached_now, 0U);
    atomic_store(&fake->attached, true);
    const uint32_t ready_reads = atomic_load(&fake->reads);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_OK);
    PP_CHECK(atomic_load(&fake->reads) > ready_reads);
    const pp_jit_status_t status = pp_jit_read_status();
    PP_CHECK_EQUAL(status.state, PP_JIT_STATE_READY);
    PP_CHECK_EQUAL(status.attached_now, 1U);
    PP_CHECK_EQUAL(status.foreign_debugger, 1U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void *call_from_another_thread(void *argument) {
    pp_status_t *results = argument;
    results[0] = pp_jit_set_enabler_opened(1U);
    results[1] = pp_jit_note_foreground();
    results[2] = pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB);
    pp_jit_status_t status;
    memset(&status, 0, sizeof status);
    status.struct_size = (uint32_t)sizeof status;
    results[3] = pp_jit_get_state(&status);
    return NULL;
}

static void calls_are_checked(void) {
    (void)pp_fake_jit_install();
    pp_jit_status_t status;
    memset(&status, 0, sizeof status);
    status.struct_size = (uint32_t)sizeof status;
    PP_CHECK_EQUAL(pp_jit_get_state(&status), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(1U), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_jit_note_foreground(), PP_E_NOT_INITIALIZED);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB), PP_E_NOT_INITIALIZED);

    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_jit_get_state(NULL), PP_E_INVALID_ARGUMENT);
    status.struct_size = 2U;
    PP_CHECK_EQUAL(pp_jit_get_state(&status), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(2U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_jit_request_pool(0U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_jit_request_pool(PP_JIT_POOL_DEFAULT_MIB), PP_E_JIT_STATE);
    PP_CHECK(strstr(pp_last_error(), "not_debugged") != NULL);
    PP_CHECK_EQUAL(pp_jit_set_enabler_opened(0U), PP_OK);

    pp_status_t results[4] = {PP_OK, PP_OK, PP_OK, PP_E_SYSTEM};
    pp_join_thread(pp_start_thread(call_from_another_thread, results));
    PP_CHECK_EQUAL(results[0], PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(results[1], PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(results[2], PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(results[3], PP_OK);

    // A caller that knows fewer members gets only those, and keeps the rest of its bytes.
    _Alignas(pp_jit_status_t) unsigned char bytes[sizeof(pp_jit_status_t)];
    memset(bytes, 0xAB, sizeof bytes);
    pp_jit_status_t *short_status = (pp_jit_status_t *)(void *)bytes;
    short_status->struct_size = (uint32_t)offsetof(pp_jit_status_t, debugged_since_launch) + 2U;
    PP_CHECK_EQUAL(pp_jit_get_state(short_status), PP_OK);
    PP_CHECK_EQUAL(short_status->state, PP_JIT_STATE_NOT_DEBUGGED);
    PP_CHECK_EQUAL(short_status->failure, PP_JIT_FAILURE_NONE);
    PP_CHECK_EQUAL(short_status->debugged_since_launch, 0U);
    PP_CHECK_EQUAL(short_status->attached_now, 0U);
    PP_CHECK_EQUAL(bytes[offsetof(pp_jit_status_t, enabler_opened)], 0xAB);
    PP_CHECK_EQUAL(bytes[sizeof bytes - 1U], 0xAB);
    PP_CHECK_EQUAL(short_status->struct_size,
                   (uint32_t)offsetof(pp_jit_status_t, debugged_since_launch) + 2U);

    // Once the host stopped, the JIT group refuses again.
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    status.struct_size = (uint32_t)sizeof status;
    PP_CHECK_EQUAL(pp_jit_get_state(&status), PP_E_NOT_INITIALIZED);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"decodes_both_flags", decodes_both_flags},
        {"system_reader_without_debugger", system_reader_without_debugger},
        {"broker_after_enabler", broker_after_enabler},
        {"debugger_before_enabler_is_foreign", debugger_before_enabler_is_foreign},
        {"debugger_without_enabler_is_foreign", debugger_without_enabler_is_foreign},
        {"refused_enabler_makes_debugger_foreign", refused_enabler_makes_debugger_foreign},
        {"waits_for_cs_debugged", waits_for_cs_debugged},
        {"foreground_reads_again", foreground_reads_again},
        {"calls_are_checked", calls_are_checked},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
