// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "jit/jit_machine.h"

#include <stddef.h>
#include <string.h>

static bool is_state(int32_t state) {
    if (state < PP_JIT_STATE_NOT_DEBUGGED) {
        return false;
    }
    return state <= PP_JIT_STATE_FAILED;
}

// The one place where the state changes: it books the time spent in the state it leaves.
static void enter(pp_jit_machine_t *machine, pp_jit_state_t state, uint64_t now_ms) {
    const uint64_t spent =
        now_ms > machine->state_entered_ms ? now_ms - machine->state_entered_ms : 0U;
    if (is_state(machine->state)) {
        machine->durations_ms[machine->state] += spent;
    }
    machine->state = state;
    machine->state_entered_ms = now_ms;
}

static bool is_broker(const pp_jit_machine_t *machine) {
    if (!machine->enabler_opened) {
        return false;
    }
    return machine->untraced_since_enabler;
}

void pp_jit_machine_init(pp_jit_machine_t *machine, uint64_t now_ms) {
    memset(machine, 0, sizeof *machine);
    machine->state = PP_JIT_STATE_NOT_DEBUGGED;
    machine->failure = PP_JIT_FAILURE_NONE;
    machine->state_entered_ms = now_ms;
}

void pp_jit_machine_apply_flags(pp_jit_machine_t *machine, pp_jit_flags_t flags, uint64_t now_ms) {
    machine->flags = flags;
    if (!flags.attached_now && machine->enabler_opened) {
        machine->untraced_since_enabler = true;
    }

    switch (machine->state) {
    case PP_JIT_STATE_NOT_DEBUGGED:
        // The broker-recognition rule (C-0006 section 3 step 2). CS_DEBUGGED comes with the
        // attach; INTEGRATION.md has the app wait for it before any command.
        if (flags.attached_now && flags.debugged_since_launch && is_broker(machine)) {
            enter(machine, PP_JIT_STATE_DEBUGGER_ATTACHED, now_ms);
        }
        break;
    case PP_JIT_STATE_DEBUGGER_ATTACHED:
        // The broker left before any command: nothing is lost, and the app may ask again.
        if (!flags.attached_now) {
            enter(machine, PP_JIT_STATE_NOT_DEBUGGED, now_ms);
        }
        break;
    case PP_JIT_STATE_REQUESTING:
        // Before command 0 the broker must stay; after it, leaving is what was asked for.
        if (!flags.attached_now && !machine->detach_sent) {
            machine->failure = PP_JIT_FAILURE_DEBUGGER_LOST;
            enter(machine, PP_JIT_STATE_FAILED, now_ms);
        }
        break;
    case PP_JIT_STATE_WAITING_DETACH:
        if (!flags.attached_now) {
            enter(machine, PP_JIT_STATE_VERIFYING, now_ms);
        }
        break;
    default:
        // verifying, ready and failed only record the reading.
        break;
    }
}

bool pp_jit_machine_takes_enabler(const pp_jit_machine_t *machine, bool opened) {
    if (machine->state == PP_JIT_STATE_NOT_DEBUGGED) {
        return true;
    }
    if (opened) {
        return false;
    }
    return machine->state == PP_JIT_STATE_DEBUGGER_ATTACHED;
}

bool pp_jit_machine_set_enabler(pp_jit_machine_t *machine, bool opened, pp_jit_flags_t flags,
                                uint64_t now_ms) {
    if (!pp_jit_machine_takes_enabler(machine, opened)) {
        return false;
    }
    if (opened) {
        machine->enabler_opened = true;
        // A debugger attached at this moment attached before the enabler opened.
        machine->untraced_since_enabler = true;
        if (flags.attached_now) {
            machine->untraced_since_enabler = false;
        }
        pp_jit_machine_apply_flags(machine, flags, now_ms);
        return true;
    }

    machine->enabler_opened = false;
    machine->untraced_since_enabler = false;
    machine->flags = flags;
    if (machine->state == PP_JIT_STATE_DEBUGGER_ATTACHED) {
        enter(machine, PP_JIT_STATE_NOT_DEBUGGED, now_ms);
    }
    return true;
}

bool pp_jit_machine_request(pp_jit_machine_t *machine, uint32_t requested_mib, uint64_t limit_bytes,
                            uint64_t now_ms) {
    if (machine->state != PP_JIT_STATE_DEBUGGER_ATTACHED) {
        return false;
    }
    machine->requested_mib = requested_mib;
    machine->pool_mib = pp_jit_pool_size_mib(requested_mib, limit_bytes);
    if (machine->pool_mib == 0U) {
        machine->failure = PP_JIT_FAILURE_MEMORY_LIMIT;
        enter(machine, PP_JIT_STATE_FAILED, now_ms);
        return true;
    }
    machine->regions_planned = pp_jit_region_count(machine->pool_mib);
    machine->regions_mapped = 0U;
    machine->requesting_entered_ms = now_ms;
    enter(machine, PP_JIT_STATE_REQUESTING, now_ms);
    return true;
}

void pp_jit_machine_region_mapped(pp_jit_machine_t *machine, uint64_t now_ms) {
    (void)now_ms;
    if (machine->state == PP_JIT_STATE_REQUESTING &&
        machine->regions_mapped < machine->regions_planned) {
        machine->regions_mapped++;
    }
}

void pp_jit_machine_detach_sent(pp_jit_machine_t *machine, uint64_t now_ms) {
    if (machine->state == PP_JIT_STATE_REQUESTING &&
        machine->regions_mapped == machine->regions_planned && !machine->detach_sent) {
        machine->detach_sent = true;
        machine->detach_sent_ms = now_ms;
    }
}

void pp_jit_machine_detach_returned(pp_jit_machine_t *machine, uint64_t now_ms) {
    if (machine->state == PP_JIT_STATE_REQUESTING && machine->detach_sent) {
        enter(machine, PP_JIT_STATE_WAITING_DETACH, now_ms);
    }
}

void pp_jit_machine_verified(pp_jit_machine_t *machine, bool passed, uint64_t now_ms) {
    if (machine->state != PP_JIT_STATE_VERIFYING) {
        return;
    }
    if (passed) {
        machine->authorization_ms =
            now_ms > machine->requesting_entered_ms ? now_ms - machine->requesting_entered_ms : 0U;
        enter(machine, PP_JIT_STATE_READY, now_ms);
    } else {
        machine->failure = PP_JIT_FAILURE_VERIFICATION;
        enter(machine, PP_JIT_STATE_FAILED, now_ms);
    }
}

void pp_jit_machine_fail(pp_jit_machine_t *machine, pp_jit_failure_t failure, uint64_t now_ms) {
    if (machine->state == PP_JIT_STATE_READY || machine->state == PP_JIT_STATE_FAILED) {
        return;
    }
    machine->failure = failure;
    enter(machine, PP_JIT_STATE_FAILED, now_ms);
}

void pp_jit_machine_tick(pp_jit_machine_t *machine, uint64_t now_ms) {
    if (machine->state == PP_JIT_STATE_REQUESTING &&
        machine->regions_mapped < machine->regions_planned &&
        now_ms >= machine->requesting_entered_ms &&
        now_ms - machine->requesting_entered_ms >= PP_JIT_POOL_TIMEOUT_MS) {
        pp_jit_machine_fail(machine, PP_JIT_FAILURE_POOL_TIMEOUT, now_ms);
        return;
    }
    if ((machine->state == PP_JIT_STATE_REQUESTING ||
         machine->state == PP_JIT_STATE_WAITING_DETACH) &&
        machine->detach_sent && now_ms >= machine->detach_sent_ms &&
        now_ms - machine->detach_sent_ms >= PP_JIT_DETACH_TIMEOUT_MS) {
        pp_jit_machine_fail(machine, PP_JIT_FAILURE_DETACH_TIMEOUT, now_ms);
    }
}

bool pp_jit_machine_foreign(const pp_jit_machine_t *machine) {
    if (!machine->flags.attached_now) {
        return false;
    }
    switch (machine->state) {
    case PP_JIT_STATE_NOT_DEBUGGED:
        if (is_broker(machine)) {
            return false;
        }
        return true;
    case PP_JIT_STATE_VERIFYING:
    case PP_JIT_STATE_READY:
        // The broker has detached, so a debugger attached now is another one.
        return true;
    default:
        // debugger_attached, requesting and waiting_detach: the broker. failed: the host no
        // longer tells them apart.
        return false;
    }
}

void pp_jit_machine_status(const pp_jit_machine_t *machine, uint64_t now_ms,
                           pp_jit_status_t *status) {
    memset(status, 0, sizeof *status);
    status->struct_size = (uint32_t)sizeof *status;
    status->state = machine->state;
    status->failure = machine->failure;
    status->debugged_since_launch = (uint8_t)machine->flags.debugged_since_launch;
    status->attached_now = (uint8_t)machine->flags.attached_now;
    status->enabler_opened = (uint8_t)machine->enabler_opened;
    status->foreign_debugger = (uint8_t)pp_jit_machine_foreign(machine);
    status->requested_mib = machine->requested_mib;
    status->pool_mib = machine->pool_mib;
    status->regions_planned = machine->regions_planned;
    status->regions_mapped = machine->regions_mapped;
    status->in_state_ms =
        now_ms > machine->state_entered_ms ? now_ms - machine->state_entered_ms : 0U;
    status->debugger_attached_ms = machine->durations_ms[PP_JIT_STATE_DEBUGGER_ATTACHED];
    status->requesting_ms = machine->durations_ms[PP_JIT_STATE_REQUESTING];
    status->waiting_detach_ms = machine->durations_ms[PP_JIT_STATE_WAITING_DETACH];
    status->verifying_ms = machine->durations_ms[PP_JIT_STATE_VERIFYING];
    status->authorization_ms = machine->authorization_ms;
}

uint32_t pp_jit_pool_size_mib(uint32_t requested_mib, uint64_t limit_bytes) {
    uint64_t size = requested_mib;
    if (size < PP_JIT_POOL_MIN_MIB) {
        size = PP_JIT_POOL_MIN_MIB;
    }
    if (size > PP_JIT_POOL_MAX_MIB) {
        size = PP_JIT_POOL_MAX_MIB;
    }
    const uint64_t eighth_mib = limit_bytes / 8U / PP_JIT_MIB;
    if (size > eighth_mib) {
        size = eighth_mib;
    }
    if (size < PP_JIT_POOL_MIN_MIB) {
        return 0U;
    }
    return (uint32_t)size;
}

uint32_t pp_jit_region_count(uint32_t pool_mib) {
    return (pool_mib + PP_JIT_REGION_MIB - 1U) / PP_JIT_REGION_MIB;
}

uint64_t pp_jit_region_bytes(uint32_t pool_mib, uint32_t index) {
    const uint64_t start_mib = (uint64_t)index * PP_JIT_REGION_MIB;
    if (start_mib >= pool_mib) {
        return 0U;
    }
    const uint64_t left_mib = pool_mib - start_mib;
    return (left_mib < PP_JIT_REGION_MIB ? left_mib : PP_JIT_REGION_MIB) * PP_JIT_MIB;
}

const char *pp_jit_state_name(pp_jit_state_t state) {
    static const char *const names[PP_JIT_STATE_COUNT] = {
        "not_debugged", "debugger_attached", "requesting", "waiting_detach", "verifying", "ready",
        "failed",
    };
    if (!is_state(state)) {
        return "an unknown state";
    }
    return names[state];
}

const char *pp_jit_failure_text(pp_jit_failure_t failure) {
    switch (failure) {
    case PP_JIT_FAILURE_NONE:
        return "No failure.";
    case PP_JIT_FAILURE_MEMORY_LIMIT:
        return "One eighth of the memory limit is smaller than the smallest pool.";
    case PP_JIT_FAILURE_POOL_TIMEOUT:
        return "The pool was not complete within 60 seconds.";
    case PP_JIT_FAILURE_DETACH_TIMEOUT:
        return "The debugger did not detach within 30 seconds.";
    case PP_JIT_FAILURE_REGION_REFUSED:
        return "The debugger returned no executable region.";
    case PP_JIT_FAILURE_MAPPING:
        return "The host could not map a region's writable alias.";
    case PP_JIT_FAILURE_DEBUGGER_LOST:
        return "The debugger detached before the pool was complete.";
    case PP_JIT_FAILURE_VERIFICATION:
        return "A probed page of the pool did not run its test routine.";
    case PP_JIT_FAILURE_SYSTEM:
        return "The system refused something the host needs.";
    default:
        return "An unknown failure.";
    }
}
