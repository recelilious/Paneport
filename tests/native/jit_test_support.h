// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Helpers of the JIT tests (M3.1): a fake platform whose debugger, flags and clock the test
// controls, around the real region mapping of the macOS backend; a recorder of the JIT
// events; and waits for a state. The fake is a process-wide singleton, because the provider
// keeps the platform for the life of the process; each case runs in a process of its own.

#ifndef PANEPORT_TESTS_JIT_TEST_SUPPORT_H
#define PANEPORT_TESTS_JIT_TEST_SUPPORT_H

#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "event_thread.h"
#include "jit/jit_platform.h"
#include "jit/jit_provider.h"
#include "memory_budget.h"
#include "paneport/host.h"
#include "paneport/jit.h"
#include "pp_check.h"
#include "test_support.h"

// No region index, for the fake's switches below.
#define PP_FAKE_NO_REGION UINT32_MAX

// The most regions whose command the fake records.
#define PP_FAKE_REGIONS 8U

typedef struct pp_fake_jit_t {
    pp_jit_platform_t platform;
    _Atomic(bool) debugged;
    _Atomic(bool) attached;
    _Atomic(bool) attach_on_enabler; // The broker attaches when the app opens the enabler.
    _Atomic(bool) detach_on_command; // The broker detaches on command 0.
    _Atomic(uint64_t) clock_ms;
    _Atomic(uint32_t) reads;
    _Atomic(uint32_t) prepares; // Commands 1.
    _Atomic(uint32_t) detaches; // Commands 0.
    // Commands sent while no debugger was attached; the provider must never send one.
    _Atomic(uint32_t) commands_without_debugger;
    _Atomic(uint32_t) refuse_region;     // The region whose command 1 returns no region.
    _Atomic(uint32_t) lose_after_region; // The broker detaches after this region's command 1.
    _Atomic(bool) hold;                  // Command 1 waits while this is true.
    _Atomic(bool) holding;               // A command 1 is waiting now.
    _Atomic(uint64_t) lengths[PP_FAKE_REGIONS];
} pp_fake_jit_t;

static inline pp_fake_jit_t *pp_fake_jit(void) {
    static pp_fake_jit_t fake;
    return &fake;
}

static inline bool pp_fake_read_flags(void *context, pp_jit_flags_t *flags) {
    pp_fake_jit_t *fake = context;
    (void)atomic_fetch_add(&fake->reads, 1U);
    flags->debugged_since_launch = atomic_load(&fake->debugged);
    flags->attached_now = atomic_load(&fake->attached);
    return true;
}

static inline void pp_fake_enabler_opened(void *context) {
    pp_fake_jit_t *fake = context;
    if (atomic_load(&fake->attach_on_enabler)) {
        atomic_store(&fake->debugged, true);
        atomic_store(&fake->attached, true);
    }
}

static inline uint64_t pp_fake_prepare_region(void *context, uint64_t length) {
    pp_fake_jit_t *fake = context;
    const uint32_t index = atomic_fetch_add(&fake->prepares, 1U);
    if (index < PP_FAKE_REGIONS) {
        atomic_store(&fake->lengths[index], length);
    }
    if (!atomic_load(&fake->attached)) {
        (void)atomic_fetch_add(&fake->commands_without_debugger, 1U);
    }
    while (atomic_load(&fake->hold)) {
        atomic_store(&fake->holding, true);
        pp_sleep_milliseconds(1);
    }
    atomic_store(&fake->holding, false);
    if (index == atomic_load(&fake->lose_after_region)) {
        atomic_store(&fake->attached, false);
    }
    if (index == atomic_load(&fake->refuse_region)) {
        return 0U;
    }
    return pp_jit_macos_prepare_region(length);
}

static inline void pp_fake_detach(void *context) {
    pp_fake_jit_t *fake = context;
    (void)atomic_fetch_add(&fake->detaches, 1U);
    if (!atomic_load(&fake->attached)) {
        (void)atomic_fetch_add(&fake->commands_without_debugger, 1U);
    }
    if (atomic_load(&fake->detach_on_command)) {
        atomic_store(&fake->attached, false);
    }
}

static inline uint64_t pp_fake_now_ms(void *context) {
    pp_fake_jit_t *fake = context;
    return atomic_load(&fake->clock_ms);
}

// Makes the provider use the fake, with a broker that attaches when the app opens the enabler
// and detaches on command 0, and a clock that stands still at 1,000 ms. Call before pp_init.
static inline pp_fake_jit_t *pp_fake_jit_install(void) {
    pp_fake_jit_t *fake = pp_fake_jit();
    fake->platform.context = fake;
    fake->platform.read_flags = pp_fake_read_flags;
    fake->platform.enabler_opened = pp_fake_enabler_opened;
    fake->platform.prepare_region = pp_fake_prepare_region;
    fake->platform.detach = pp_fake_detach;
    fake->platform.now_ms = pp_fake_now_ms;
    fake->platform.poll_ms = 2U;
    atomic_store(&fake->attach_on_enabler, true);
    atomic_store(&fake->detach_on_command, true);
    atomic_store(&fake->clock_ms, 1000U);
    atomic_store(&fake->refuse_region, PP_FAKE_NO_REGION);
    atomic_store(&fake->lose_after_region, PP_FAKE_NO_REGION);
    pp_jit_provider_use_platform(&fake->platform);
    return fake;
}

// Moves the fake's clock on, and wakes the supervisor so that it applies the timeouts.
static inline void pp_fake_advance(pp_fake_jit_t *fake, uint64_t milliseconds) {
    (void)atomic_fetch_add(&fake->clock_ms, milliseconds);
    pp_jit_provider_wake();
}

// Gives the memory stub a limit of limit_mib, all of it available.
static inline void pp_jit_set_limit_mib(uint64_t limit_mib) {
    PP_CHECK_EQUAL(pp_memory_set_stub(limit_mib * 1024U * 1024U, 0U), PP_OK);
}

static inline pp_jit_status_t pp_jit_read_status(void) {
    pp_jit_status_t status;
    memset(&status, 0, sizeof status);
    status.struct_size = (uint32_t)sizeof status;
    PP_CHECK_EQUAL(pp_jit_get_state(&status), PP_OK);
    return status;
}

// Waits up to a minute for the state, and fails the test when another terminal state comes.
static inline pp_jit_status_t pp_jit_wait_for_state(pp_jit_state_t state) {
    pp_jit_status_t status = pp_jit_read_status();
    for (int round = 0; round < 60000 && status.state != state; round++) {
        if (status.state == PP_JIT_STATE_FAILED || status.state == PP_JIT_STATE_READY) {
            break;
        }
        pp_sleep_milliseconds(1);
        status = pp_jit_read_status();
    }
    if (status.state != state) {
        (void)fprintf(stderr, "The JIT state is %d with failure %d, not %d.\n", status.state,
                      status.failure, state);
    }
    PP_CHECK_EQUAL(status.state, state);
    return status;
}

// Reads and writes one byte at an address that the test holds as an integer, as the pool keeps
// its views, with a single instruction.
static inline void pp_jit_test_store_byte(uint64_t address, uint8_t value) {
    __asm__ volatile("strb %w1, [%0]" : : "r"(address), "r"((uint32_t)value) : "memory");
}

static inline uint8_t pp_jit_test_load_byte(uint64_t address) {
    uint32_t value = 0U;
    __asm__ volatile("ldrb %w0, [%1]" : "=r"(value) : "r"(address) : "memory");
    return (uint8_t)value;
}

// Leaves the probe's routine that returns value on a page, as jit_probe.c writes it: movz w0,
// #value and ret, written through the alias at write_address and made visible to instruction
// fetches at exec_address. Unlike the probe, it does not take the routine away.
static inline void pp_jit_test_plant_routine(uint64_t write_address, uint64_t exec_address,
                                             uint32_t value) {
    const uint32_t movz = 0x52800000U | ((value & 0xFFFFU) << 5U);
    const uint32_t ret = 0xD65F03C0U;
    __asm__ volatile("str %w1, [%0]\n"
                     "str %w2, [%0, #4]\n"
                     "dc cvau, %0\n"
                     "dsb ish\n"
                     "ic ivau, %3\n"
                     "dsb ish\n"
                     "isb\n"
                     :
                     : "r"(write_address), "r"(movz), "r"(ret), "r"(exec_address)
                     : "memory");
}

// The JIT events that reached the event callback, in order.
#define PP_JIT_EVENTS 32U

typedef struct pp_jit_events_t {
    _Atomic(uint32_t) count;
    _Atomic(int32_t) states[PP_JIT_EVENTS];
    _Atomic(uint32_t) off_thread; // Events delivered on another thread than the event thread.
} pp_jit_events_t;

static inline void pp_jit_record_event(const pp_event_t *event, void *user_data) {
    pp_jit_events_t *events = user_data;
    if (event->kind != PP_EVENT_JIT_STATE_CHANGED) {
        return;
    }
    if (!pp_event_thread_is_current()) {
        (void)atomic_fetch_add(&events->off_thread, 1U);
    }
    const uint32_t index = atomic_fetch_add(&events->count, 1U);
    if (index < PP_JIT_EVENTS) {
        atomic_store(&events->states[index], (int32_t)event->code);
    }
}

// Checks that the recorded events are exactly the states given, in order.
static inline void pp_jit_check_events(pp_jit_events_t *events, const pp_jit_state_t *states,
                                       uint32_t count) {
    const uint32_t recorded = atomic_load(&events->count);
    for (uint32_t index = 0U; index < recorded && index < PP_JIT_EVENTS; index++) {
        (void)fprintf(stderr, "event %u: state %d\n", index, atomic_load(&events->states[index]));
    }
    PP_CHECK_EQUAL(recorded, count);
    for (uint32_t index = 0U; index < count; index++) {
        PP_CHECK_EQUAL(atomic_load(&events->states[index]), states[index]);
    }
    PP_CHECK_EQUAL(atomic_load(&events->off_thread), 0U);
}

#endif // PANEPORT_TESTS_JIT_TEST_SUPPORT_H
