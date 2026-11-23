// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The JIT group of paneport/jit.h and the provider behind it (jit_provider.h).

#include "jit/jit_provider.h"

#include <pthread.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <time.h>

#include "error.h"
#include "event_thread.h"
#include "host_state.h"
#include "jit/jit_machine.h"
#include "jit/jit_map.h"
#include "jit/jit_platform.h"
#include "jit/jit_probe.h"
#include "log.h"
#include "paneport/host.h"
#include "paneport/jit.h"
#include "paneport/memory.h"
#include "platform.h"
#include "signals/signals.h"

// The provider: a process-wide singleton (C-0010 section 3). The mutex guards all of it. The
// supervisor sleeps on wake between two readings. The commands and the probe run without the
// mutex, so that pp_jit_get_state() answers while they take seconds.
static struct {
    pthread_mutex_t mutex;
    pthread_cond_t wake;
    const pp_jit_platform_t *platform;
    bool machine_started;
    pp_jit_machine_t machine;
    bool supervisor_started; // Created and not yet joined.
    bool stopping;
    bool woken;
    pthread_t supervisor;
    bool probe_started;
    pp_jit_pool_region_t regions[PP_JIT_MAX_REGIONS];
    uint32_t region_count;
} s_jit = {
    .mutex = PTHREAD_MUTEX_INITIALIZER,
    .wake = PTHREAD_COND_INITIALIZER,
};

// The helpers below run with the mutex held.

static const pp_jit_platform_t *platform(void) {
    if (s_jit.platform == NULL) {
        s_jit.platform = pp_jit_default_platform();
    }
    return s_jit.platform;
}

static uint64_t now(void) { return platform()->now_ms(platform()->context); }

static pp_jit_machine_t *machine(void) {
    if (!s_jit.machine_started) {
        pp_jit_machine_init(&s_jit.machine, now());
        s_jit.machine_started = true;
    }
    return &s_jit.machine;
}

// Reports a change of the state since before: a log line, and the event of C-0005 section 2.
static void report(pp_jit_state_t before) {
    const pp_jit_machine_t *state = &s_jit.machine;
    if (state->state == before) {
        return;
    }
    if (state->state == PP_JIT_STATE_FAILED) {
        (void)pp_log(PP_LOG_ERROR, "JIT: %s -> failed. %s Relaunch the app to enable JIT again.",
                     pp_jit_state_name(before), pp_jit_failure_text(state->failure));
    } else {
        (void)pp_log(PP_LOG_INFO, "JIT: %s -> %s, after %llu ms in %s in all.",
                     pp_jit_state_name(before), pp_jit_state_name(state->state),
                     (unsigned long long)state->durations_ms[before], pp_jit_state_name(before));
    }
    const pp_event_t event = {
        .struct_size = (uint32_t)sizeof(pp_event_t),
        .kind = PP_EVENT_JIT_STATE_CHANGED,
        .code = state->state,
        .handle = 0U,
        .address = 0U,
    };
    // A full queue counts the loss and logs it once (native-host.md section 5).
    (void)pp_event_thread_post_event(&event);
}

static void fail(pp_jit_failure_t failure) {
    const pp_jit_state_t before = machine()->state;
    pp_jit_machine_fail(machine(), failure, now());
    report(before);
}

// Reads the flags and applies them and the timeouts, reporting each change.
static void step(void) {
    pp_jit_machine_t *state = machine();
    const pp_jit_platform_t *backend = platform();
    pp_jit_flags_t flags = {.debugged_since_launch = false, .attached_now = false};
    if (!backend->read_flags(backend->context, &flags)) {
        fail(PP_JIT_FAILURE_SYSTEM);
        return;
    }
    const pp_jit_state_t before = state->state;
    pp_jit_machine_apply_flags(state, flags, now());
    report(before);
    const pp_jit_state_t after = state->state;
    pp_jit_machine_tick(state, now());
    report(after);
}

static void wake_supervisor(void) {
    s_jit.woken = true;
    (void)pthread_cond_signal(&s_jit.wake);
}

// Runs the probe of C-0006 section 3 step 6 over the registry, without the mutex.
static void verify(void) {
    s_jit.probe_started = true;
    pp_jit_pool_region_t regions[PP_JIT_MAX_REGIONS];
    memcpy(regions, s_jit.regions, sizeof regions);
    const uint32_t count = s_jit.region_count;
    (void)pthread_mutex_unlock(&s_jit.mutex);

    pp_jit_probe_result_t result;
    pp_jit_probe_run(regions, count, &result);
    // A last look: no view may have become writable and executable meanwhile.
    bool protections_hold = true;
    for (uint32_t index = 0U; index < count; index++) {
        if (!pp_jit_map_has_protection(regions[index].exec_address, regions[index].size,
                                       PP_JIT_EXEC_PROTECTION, false) ||
            !pp_jit_map_has_protection(regions[index].write_address, regions[index].size,
                                       PP_JIT_WRITE_PROTECTION, false)) {
            protections_hold = false;
        }
    }

    (void)pthread_mutex_lock(&s_jit.mutex);
    if (!result.passed && result.signal != 0) {
        (void)pp_log(PP_LOG_ERROR,
                     "JIT: the probed page at offset %llu of region %u faulted with signal %d.",
                     (unsigned long long)result.offset, result.region, result.signal);
    } else if (!result.passed) {
        (void)pp_log(PP_LOG_ERROR,
                     "JIT: the probed page at offset %llu of region %u returned %llu, not its "
                     "own value.",
                     (unsigned long long)result.offset, result.region,
                     (unsigned long long)result.returned);
    } else if (!protections_hold) {
        (void)pp_log(PP_LOG_ERROR, "JIT: a view of the pool changed its protection.");
    } else {
        (void)pp_log(PP_LOG_INFO, "JIT: the probe passed on %u pages of %u regions.", result.pages,
                     count);
    }
    bool passed = false;
    if (result.passed && protections_hold) {
        passed = true;
    }
    const pp_jit_state_t before = machine()->state;
    pp_jit_machine_verified(machine(), passed, now());
    report(before);
}

static void *supervisor_main(void *argument) {
    (void)argument;
    pp_platform_name_thread("paneport.jit");
    (void)pthread_mutex_lock(&s_jit.mutex);
    while (!s_jit.stopping) {
        if (!s_jit.woken) {
            const uint32_t pause_ms = platform()->poll_ms;
            const struct timespec pause = {
                .tv_sec = (time_t)(pause_ms / 1000U),
                .tv_nsec = (long)(pause_ms % 1000U) * 1000000L,
            };
            (void)pthread_cond_timedwait_relative_np(&s_jit.wake, &s_jit.mutex, &pause);
        }
        s_jit.woken = false;
        if (s_jit.stopping) {
            break;
        }
        step();
        if (machine()->state == PP_JIT_STATE_VERIFYING && !s_jit.probe_started) {
            verify();
        }
        if (machine()->state == PP_JIT_STATE_READY || machine()->state == PP_JIT_STATE_FAILED) {
            break;
        }
    }
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return NULL;
}

static bool start_supervisor(void) {
    if (s_jit.supervisor_started) {
        return true;
    }
    s_jit.stopping = false;
    if (pthread_create(&s_jit.supervisor, NULL, supervisor_main, NULL) != 0) {
        return false;
    }
    s_jit.supervisor_started = true;
    return true;
}

// The request thread's helpers lock the mutex themselves.

// Reads the flags, and returns whether a command may go out now: the state is requesting and a
// debugger is attached now (C-0006 section 3). For command 0 it also starts the detach timeout.
static bool command_allowed(bool detach) {
    (void)pthread_mutex_lock(&s_jit.mutex);
    step();
    bool allowed = false;
    if (machine()->state == PP_JIT_STATE_REQUESTING && machine()->flags.attached_now) {
        allowed = true;
        if (detach) {
            pp_jit_machine_detach_sent(machine(), now());
        }
    }
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return allowed;
}

// Maps the alias of the region that command 1 returned, registers both views with the
// dispatcher, and adds the region to the registry. Returns whether the request goes on.
static bool keep_region(uint64_t exec_address, uint64_t length) {
    pp_jit_failure_t failure = PP_JIT_FAILURE_REGION_REFUSED;
    pp_jit_map_result_t mapped = {.write_address = 0U, .maxima_lowered = false};
    pp_jit_pool_region_t region = {
        .exec_address = exec_address,
        .write_address = 0U,
        .size = length,
        .exec_range = 0U,
        .write_range = 0U,
    };
    if (exec_address != 0U) {
        failure = pp_jit_map_alias(exec_address, length, &mapped);
    }
    if (failure == PP_JIT_FAILURE_NONE) {
        region.write_address = mapped.write_address;
        // Registered before anything can run in them (docs/design/signals.md section 12).
        if (pp_signals_add_pool_range(exec_address, length, &region.exec_range) != PP_OK ||
            pp_signals_add_pool_range(region.write_address, length, &region.write_range) != PP_OK) {
            failure = PP_JIT_FAILURE_SYSTEM;
        }
    }

    (void)pthread_mutex_lock(&s_jit.mutex);
    bool kept = false;
    if (machine()->state == PP_JIT_STATE_REQUESTING) {
        if (failure != PP_JIT_FAILURE_NONE) {
            fail(failure);
        } else {
            s_jit.regions[s_jit.region_count] = region;
            s_jit.region_count++;
            pp_jit_machine_region_mapped(machine(), now());
            kept = true;
            if (!mapped.maxima_lowered) {
                (void)pp_log(PP_LOG_WARNING,
                             "JIT: the maximum protection of region %u could not "
                             "be lowered; its current protection holds.",
                             s_jit.region_count);
            }
        }
    }
    (void)pthread_mutex_unlock(&s_jit.mutex);

    if (!kept) {
        // A region that came too late, after a timeout, or that failed: nothing keeps it.
        if (region.exec_range != 0U) {
            (void)pp_signals_remove_pool_range(region.exec_range);
        }
        if (region.write_range != 0U) {
            (void)pp_signals_remove_pool_range(region.write_range);
        }
        pp_jit_map_release_alias(region.write_address, length);
    }
    return kept;
}

static void *request_main(void *argument) {
    (void)argument;
    pp_platform_name_thread("paneport.jit-pool");
    (void)pthread_mutex_lock(&s_jit.mutex);
    const pp_jit_platform_t *backend = platform();
    const uint32_t planned = machine()->regions_planned;
    const uint32_t pool_mib = machine()->pool_mib;
    (void)pthread_mutex_unlock(&s_jit.mutex);

    // C-0006 section 3 step 3: command 1 with a NULL address for each region.
    for (uint32_t index = 0U; index < planned; index++) {
        if (!command_allowed(false)) {
            return NULL;
        }
        const uint64_t length = pp_jit_region_bytes(pool_mib, index);
        const uint64_t exec_address = backend->prepare_region(backend->context, length);
        if (!keep_region(exec_address, length)) {
            return NULL;
        }
    }

    // Step 5: command 0, then the wait for the detach.
    if (!command_allowed(true)) {
        return NULL;
    }
    backend->detach(backend->context);
    (void)pthread_mutex_lock(&s_jit.mutex);
    const pp_jit_state_t before = machine()->state;
    pp_jit_machine_detach_returned(machine(), now());
    report(before);
    wake_supervisor();
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return NULL;
}

static bool start_request(void) {
    pthread_attr_t attributes;
    if (pthread_attr_init(&attributes) != 0) {
        return false;
    }
    // Nobody waits for it: a command may never return (jit_provider.h).
    bool started = pthread_attr_setdetachstate(&attributes, PTHREAD_CREATE_DETACHED) == 0;
    pthread_t thread;
    if (started) {
        started = pthread_create(&thread, &attributes, request_main, NULL) == 0;
    }
    (void)pthread_attr_destroy(&attributes);
    return started;
}

// Copies the members of full that lie entirely inside status->struct_size; struct_size stays.
static void copy_status(const pp_jit_status_t *full, pp_jit_status_t *status) {
#define PP_JIT_MEMBER_END(member) (offsetof(pp_jit_status_t, member) + sizeof full->member)
    const size_t ends[] = {
        PP_JIT_MEMBER_END(state),
        PP_JIT_MEMBER_END(failure),
        PP_JIT_MEMBER_END(debugged_since_launch),
        PP_JIT_MEMBER_END(attached_now),
        PP_JIT_MEMBER_END(enabler_opened),
        PP_JIT_MEMBER_END(foreign_debugger),
        PP_JIT_MEMBER_END(requested_mib),
        PP_JIT_MEMBER_END(pool_mib),
        PP_JIT_MEMBER_END(regions_planned),
        PP_JIT_MEMBER_END(regions_mapped),
        PP_JIT_MEMBER_END(in_state_ms),
        PP_JIT_MEMBER_END(debugger_attached_ms),
        PP_JIT_MEMBER_END(requesting_ms),
        PP_JIT_MEMBER_END(waiting_detach_ms),
        PP_JIT_MEMBER_END(verifying_ms),
        PP_JIT_MEMBER_END(authorization_ms),
    };
#undef PP_JIT_MEMBER_END
    // The members follow each other without padding, so the bytes up to the last whole member
    // are exactly the whole members.
    size_t end = sizeof(uint32_t);
    for (size_t index = 0U; index < sizeof ends / sizeof ends[0]; index++) {
        if (ends[index] <= status->struct_size) {
            end = ends[index];
        }
    }
    const unsigned char *from = (const unsigned char *)full;
    unsigned char *to = (unsigned char *)status;
    memcpy(to + sizeof(uint32_t), from + sizeof(uint32_t), end - sizeof(uint32_t));
}

PP_EXPORT pp_status_t pp_jit_get_state(pp_jit_status_t *status) {
    pp_error_clear();
    if (status == NULL || status->struct_size < sizeof(uint32_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_jit_get_state needs a struct with a struct_size of at least 4.");
    }
    if (!pp_host_is_running()) {
        return pp_error_set(PP_E_NOT_INITIALIZED,
                            "pp_jit_get_state needs a running host: call pp_init first.");
    }
    pp_jit_status_t full;
    (void)pthread_mutex_lock(&s_jit.mutex);
    pp_jit_machine_status(machine(), now(), &full);
    (void)pthread_mutex_unlock(&s_jit.mutex);
    copy_status(&full, status);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_jit_set_enabler_opened(uint8_t opened) {
    pp_error_clear();
    const pp_status_t status = pp_host_check_main_call("pp_jit_set_enabler_opened");
    if (status != PP_OK) {
        return status;
    }
    if (opened > 1U) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_jit_set_enabler_opened takes 0 or 1, not %u.", (unsigned)opened);
    }

    (void)pthread_mutex_lock(&s_jit.mutex);
    pp_jit_machine_t *state = machine();
    if (!pp_jit_machine_takes_enabler(state, opened == 1U)) {
        const char *name = pp_jit_state_name(state->state);
        (void)pthread_mutex_unlock(&s_jit.mutex);
        return pp_error_set(PP_E_JIT_STATE, "The JIT enabler cannot be %s in the %s state.",
                            opened == 1U ? "opened" : "taken back", name);
    }
    const pp_jit_platform_t *backend = platform();
    pp_jit_flags_t flags = {.debugged_since_launch = false, .attached_now = false};
    if (!backend->read_flags(backend->context, &flags)) {
        fail(PP_JIT_FAILURE_SYSTEM);
    } else {
        const pp_jit_state_t before = state->state;
        (void)pp_jit_machine_set_enabler(state, opened == 1U, flags, now());
        report(before);
        if (opened == 1U) {
            (void)pp_log(PP_LOG_INFO, "JIT: the app opens the JIT enabler.");
            if (backend->enabler_opened != NULL) {
                backend->enabler_opened(backend->context);
            }
            // From now on the supervisor watches for the broker.
            if (!start_supervisor()) {
                fail(PP_JIT_FAILURE_SYSTEM);
            }
        } else {
            (void)pp_log(PP_LOG_INFO, "JIT: the JIT enabler did not open.");
        }
    }
    wake_supervisor();
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_jit_note_foreground(void) {
    pp_error_clear();
    const pp_status_t status = pp_host_check_main_call("pp_jit_note_foreground");
    if (status != PP_OK) {
        return status;
    }
    (void)pthread_mutex_lock(&s_jit.mutex);
    step();
    const pp_jit_machine_t *state = machine();
    if (state->state == PP_JIT_STATE_READY && state->flags.attached_now) {
        (void)pp_log(PP_LOG_WARNING, "JIT: a debugger is attached again; the pool stays "
                                     "authorized, but a debugger takes the faults of a session.");
    }
    wake_supervisor();
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_jit_request_pool(uint32_t size_mib) {
    pp_error_clear();
    const pp_status_t status = pp_host_check_main_call("pp_jit_request_pool");
    if (status != PP_OK) {
        return status;
    }
    if (size_mib == 0U) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_jit_request_pool needs a size above 0.");
    }

    pp_memory_budget_t budget;
    memset(&budget, 0, sizeof budget);
    budget.struct_size = (uint32_t)sizeof budget;
    const bool limit_read = pp_memory_get_budget(&budget) == PP_OK;
    pp_error_clear();

    (void)pthread_mutex_lock(&s_jit.mutex);
    // The state follows the flags of this moment, so a broker that left is seen.
    step();
    pp_jit_machine_t *state = machine();
    if (state->state != PP_JIT_STATE_DEBUGGER_ATTACHED) {
        const char *name = pp_jit_state_name(state->state);
        (void)pthread_mutex_unlock(&s_jit.mutex);
        return pp_error_set(PP_E_JIT_STATE,
                            "pp_jit_request_pool needs the debugger_attached state, not %s.", name);
    }
    if (!limit_read) {
        fail(PP_JIT_FAILURE_SYSTEM);
    } else {
        const pp_jit_state_t before = state->state;
        (void)pp_jit_machine_request(state, size_mib, budget.limit_bytes, now());
        report(before);
        if (state->state == PP_JIT_STATE_REQUESTING) {
            (void)pp_log(PP_LOG_INFO,
                         "JIT: a pool of %u MiB, of %u MiB asked, is requested in %u regions.",
                         state->pool_mib, size_mib, state->regions_planned);
            if (!start_supervisor() || !start_request()) {
                fail(PP_JIT_FAILURE_SYSTEM);
            }
        }
    }
    wake_supervisor();
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return PP_OK;
}

void pp_jit_provider_stop(void) {
    (void)pthread_mutex_lock(&s_jit.mutex);
    const bool started = s_jit.supervisor_started;
    s_jit.stopping = true;
    wake_supervisor();
    (void)pthread_mutex_unlock(&s_jit.mutex);
    if (started) {
        (void)pthread_join(s_jit.supervisor, NULL);
        (void)pthread_mutex_lock(&s_jit.mutex);
        s_jit.supervisor_started = false;
        (void)pthread_mutex_unlock(&s_jit.mutex);
    }
}

bool pp_jit_is_ready(void) {
    (void)pthread_mutex_lock(&s_jit.mutex);
    bool ready = false;
    if (s_jit.machine_started && s_jit.machine.state == PP_JIT_STATE_READY) {
        ready = true;
    }
    (void)pthread_mutex_unlock(&s_jit.mutex);
    return ready;
}

void pp_jit_pool_copy(pp_jit_pool_region_t *regions, uint32_t capacity, uint32_t *count) {
    (void)pthread_mutex_lock(&s_jit.mutex);
    const uint32_t copied = s_jit.region_count < capacity ? s_jit.region_count : capacity;
    if (copied > 0U) {
        memcpy(regions, s_jit.regions, copied * sizeof regions[0]);
    }
    *count = copied;
    (void)pthread_mutex_unlock(&s_jit.mutex);
}

#ifdef PANEPORT_TEST_BUILD
void pp_jit_provider_use_platform(const pp_jit_platform_t *replacement) {
    (void)pthread_mutex_lock(&s_jit.mutex);
    s_jit.platform = replacement;
    (void)pthread_mutex_unlock(&s_jit.mutex);
}

void pp_jit_provider_wake(void) {
    (void)pthread_mutex_lock(&s_jit.mutex);
    wake_supervisor();
    (void)pthread_mutex_unlock(&s_jit.mutex);
}
#endif
