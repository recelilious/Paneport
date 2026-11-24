// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The exports of paneport/testing.h. CMake compiles this file into test builds only
// (PANEPORT_TEST_BUILD), so the framework that the main app ships exports none of them.

#include <stddef.h>
#include <stdlib.h>
#include <string.h>

#include "crash_record.h"
#include "error.h"
#include "event_thread.h"
#include "host_state.h"
#include "jit/jit_provider.h"
#include "log.h"
#include "memory_budget.h"
#include "paneport/host.h"
#include "paneport/jit.h"
#include "paneport/memory.h"
#include "paneport/testing.h"
#include "signals/signals.h"
#include "thread_role.h"

#define PP_MEMBER(type, member)                                                                    \
    {(uint32_t)sizeof(pp_test_layout_member_t), #member, (uint32_t)offsetof(type, member),         \
     (uint32_t)sizeof(((type *)NULL)->member)}

#define PP_STRUCT(type, members)                                                                   \
    {(uint32_t)sizeof(pp_test_layout_struct_t), #type, (uint32_t)sizeof(type),                     \
     (uint32_t)(sizeof(members) / sizeof((members)[0])), members}

static const pp_test_layout_member_t k_init_config_members[] = {
    PP_MEMBER(pp_init_config_t, struct_size),
    PP_MEMBER(pp_init_config_t, crash_record_folder),
};

static const pp_test_layout_member_t k_event_members[] = {
    PP_MEMBER(pp_event_t, struct_size), PP_MEMBER(pp_event_t, kind),    PP_MEMBER(pp_event_t, code),
    PP_MEMBER(pp_event_t, handle),      PP_MEMBER(pp_event_t, address),
};

static const pp_test_layout_member_t k_log_line_members[] = {
    PP_MEMBER(pp_log_line_t, struct_size),
    PP_MEMBER(pp_log_line_t, level),
    PP_MEMBER(pp_log_line_t, thread_id),
    PP_MEMBER(pp_log_line_t, text),
};

static const pp_test_layout_member_t k_memory_budget_members[] = {
    PP_MEMBER(pp_memory_budget_t, struct_size),
    PP_MEMBER(pp_memory_budget_t, game_mode_level_reached),
    PP_MEMBER(pp_memory_budget_t, limit_bytes),
    PP_MEMBER(pp_memory_budget_t, available_bytes),
    PP_MEMBER(pp_memory_budget_t, footprint_bytes),
};

static const pp_test_layout_member_t k_layout_member_members[] = {
    PP_MEMBER(pp_test_layout_member_t, struct_size),
    PP_MEMBER(pp_test_layout_member_t, name),
    PP_MEMBER(pp_test_layout_member_t, offset),
    PP_MEMBER(pp_test_layout_member_t, size),
};

static const pp_test_layout_member_t k_layout_struct_members[] = {
    PP_MEMBER(pp_test_layout_struct_t, struct_size),
    PP_MEMBER(pp_test_layout_struct_t, name),
    PP_MEMBER(pp_test_layout_struct_t, size),
    PP_MEMBER(pp_test_layout_struct_t, member_count),
    PP_MEMBER(pp_test_layout_struct_t, members),
};

static const pp_test_layout_member_t k_harness_config_members[] = {
    PP_MEMBER(pp_test_signal_harness_config_t, struct_size),
    PP_MEMBER(pp_test_signal_harness_config_t, marked_threads),
    PP_MEMBER(pp_test_signal_harness_config_t, unmarked_threads),
    PP_MEMBER(pp_test_signal_harness_config_t, pause_microseconds),
};

static const pp_test_layout_member_t k_signal_counters_members[] = {
    PP_MEMBER(pp_test_signal_counters_t, struct_size),
    PP_MEMBER(pp_test_signal_counters_t, running),
    PP_MEMBER(pp_test_signal_counters_t, guest_faults_raised_by_mark),
    PP_MEMBER(pp_test_signal_counters_t, guest_faults_handled_by_mark),
    PP_MEMBER(pp_test_signal_counters_t, guest_faults_raised_by_address),
    PP_MEMBER(pp_test_signal_counters_t, guest_faults_handled_by_address),
    PP_MEMBER(pp_test_signal_counters_t, unexpected_faults),
    PP_MEMBER(pp_test_signal_counters_t, runtime_faults),
};

static const pp_test_layout_member_t k_jit_status_members[] = {
    PP_MEMBER(pp_jit_status_t, struct_size),
    PP_MEMBER(pp_jit_status_t, state),
    PP_MEMBER(pp_jit_status_t, failure),
    PP_MEMBER(pp_jit_status_t, debugged_since_launch),
    PP_MEMBER(pp_jit_status_t, attached_now),
    PP_MEMBER(pp_jit_status_t, enabler_opened),
    PP_MEMBER(pp_jit_status_t, foreign_debugger),
    PP_MEMBER(pp_jit_status_t, requested_mib),
    PP_MEMBER(pp_jit_status_t, pool_mib),
    PP_MEMBER(pp_jit_status_t, regions_planned),
    PP_MEMBER(pp_jit_status_t, regions_mapped),
    PP_MEMBER(pp_jit_status_t, in_state_ms),
    PP_MEMBER(pp_jit_status_t, debugger_attached_ms),
    PP_MEMBER(pp_jit_status_t, requesting_ms),
    PP_MEMBER(pp_jit_status_t, waiting_detach_ms),
    PP_MEMBER(pp_jit_status_t, verifying_ms),
    PP_MEMBER(pp_jit_status_t, authorization_ms),
};

// Every struct that crosses the interface (C-0005 section 5). A struct added to a header is
// added here in the same change, and to the C# definitions of Paneport.Interop.
static const pp_test_layout_struct_t k_layout_table[] = {
    PP_STRUCT(pp_init_config_t, k_init_config_members),
    PP_STRUCT(pp_event_t, k_event_members),
    PP_STRUCT(pp_log_line_t, k_log_line_members),
    PP_STRUCT(pp_memory_budget_t, k_memory_budget_members),
    PP_STRUCT(pp_jit_status_t, k_jit_status_members),
    PP_STRUCT(pp_test_layout_member_t, k_layout_member_members),
    PP_STRUCT(pp_test_layout_struct_t, k_layout_struct_members),
    PP_STRUCT(pp_test_signal_harness_config_t, k_harness_config_members),
    PP_STRUCT(pp_test_signal_counters_t, k_signal_counters_members),
};

PP_EXPORT pp_status_t pp_test_get_layout_table(const pp_test_layout_struct_t **table,
                                               uint32_t *count) {
    pp_error_clear();
    if (table == NULL || count == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_test_get_layout_table needs two outputs.");
    }
    *table = k_layout_table;
    *count = (uint32_t)(sizeof k_layout_table / sizeof k_layout_table[0]);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_test_write_crash_record(const char *message) {
    pp_error_clear();
    if (message == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_test_write_crash_record needs a message.");
    }

    const pp_crash_info_t info = {
        .signal = 0,
        .code = 0,
        .fault_address = 0U,
        .program_counter = 0U,
        .thread_role = pp_host_thread_role(),
        .is_test = 1U,
        .message = message,
    };
    const pp_status_t status = pp_crash_record_write(&info);
    switch (status) {
    case PP_OK:
        return PP_OK;
    case PP_E_NOT_INITIALIZED:
        return pp_error_set(status, "pp_init has not set a crash record folder.");
    case PP_E_BUSY:
        return pp_error_set(status, "Another crash record is being written.");
    default:
        return pp_error_set(status, "The crash record could not be written.");
    }
}

PP_EXPORT pp_status_t pp_test_set_memory_stub(uint64_t available_bytes, uint64_t footprint_bytes) {
    pp_error_clear();
    const pp_status_t status = pp_memory_set_stub(available_bytes, footprint_bytes);
    if (status != PP_OK) {
        return pp_error_set(status, "The memory stub exists only where the host cannot read the "
                                    "system, such as macOS.");
    }
    return PP_OK;
}

PP_EXPORT pp_status_t pp_test_jit_plant_status(uint32_t pattern) {
    pp_error_clear();
    if (pattern > 2U) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_test_jit_plant_status takes 0, 1 or 2, not %u.", (unsigned)pattern);
    }
    if (pattern == 0U) {
        pp_jit_provider_plant_status(NULL);
        return PP_OK;
    }
    // The patterns of paneport/testing.h.
    pp_jit_status_t status;
    memset(&status, 0, sizeof status);
    status.struct_size = (uint32_t)sizeof status;
    const uint64_t milliseconds = pattern == 1U ? 1000U : 2000U;
    if (pattern == 1U) {
        status.state = PP_JIT_STATE_FAILED;
        status.failure = PP_JIT_FAILURE_SYSTEM;
        status.attached_now = 1U;
        status.requested_mib = 601U;
        status.pool_mib = 602U;
        status.regions_planned = 5U;
        status.regions_mapped = 7U;
    } else {
        status.state = PP_JIT_STATE_VERIFYING;
        status.failure = PP_JIT_FAILURE_MEMORY_LIMIT;
        status.enabler_opened = 1U;
        status.requested_mib = 603U;
        status.pool_mib = 604U;
        status.regions_planned = 2U;
        status.regions_mapped = 3U;
    }
    status.debugged_since_launch = 1U;
    status.in_state_ms = milliseconds + 1U;
    status.debugger_attached_ms = milliseconds + 2U;
    status.requesting_ms = milliseconds + 3U;
    status.waiting_detach_ms = milliseconds + 4U;
    status.verifying_ms = milliseconds + 5U;
    status.authorization_ms = milliseconds + 6U;
    pp_jit_provider_plant_status(&status);
    return PP_OK;
}

static pp_status_t posted(pp_status_t status, const char *what) {
    switch (status) {
    case PP_OK:
        return PP_OK;
    case PP_E_BUSY:
        return pp_error_set(status, "The host %s queue is full.", what);
    default:
        return pp_error_set(status, "The host is not running, so it takes no %s.", what);
    }
}

PP_EXPORT pp_status_t pp_test_post_event(uint32_t kind, int64_t code) {
    pp_error_clear();
    const pp_event_t event = {
        .struct_size = (uint32_t)sizeof(pp_event_t),
        .kind = kind,
        .code = code,
        .handle = 0U,
        .address = 0U,
    };
    return posted(pp_event_thread_post_event(&event), "event");
}

PP_EXPORT pp_status_t pp_test_log(int32_t level, const char *text) {
    pp_error_clear();
    if (text == NULL || level < PP_LOG_DEBUG || level > PP_LOG_ERROR) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_test_log needs a text and a known level.");
    }
    return posted(pp_log(level, "%s", text), "log");
}

PP_EXPORT pp_status_t pp_test_duplicate_text(const char *text, char **copy) {
    pp_error_clear();
    if (text == NULL || copy == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_test_duplicate_text needs a text and an "
                                                   "output.");
    }
    const size_t size = strlen(text) + 1U;
    char *duplicate = malloc(size);
    if (duplicate == NULL) {
        return pp_error_set(PP_E_OUT_OF_MEMORY, "The host could not allocate the copy.");
    }
    memcpy(duplicate, text, size);
    *copy = duplicate;
    return PP_OK;
}

PP_EXPORT pp_status_t pp_test_signals_check_installed(int32_t *replaced_signal) {
    pp_error_clear();
    return pp_signals_check_installed(replaced_signal);
}
