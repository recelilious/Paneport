// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The life cycle group of C-0005 section 2, pp_abi_version and pp_free.

#include <pthread.h>
#include <stdatomic.h>
#include <stddef.h>
#include <stdlib.h>

#include "crash_record.h"
#include "error.h"
#include "event_thread.h"
#include "host_state.h"
#include "log.h"
#include "paneport/host.h"
#include "platform.h"

enum {
    PP_HOST_NOT_STARTED = 0,
    PP_HOST_STARTING = 1,
    PP_HOST_RUNNING = 2,
    PP_HOST_STOPPED = 3,
};

// The life cycle: a process-wide singleton (C-0010 section 3). main_thread is written before
// state turns PP_HOST_RUNNING, and read only once state says so.
static struct {
    _Atomic(int) state;
    pthread_t main_thread;
} s_host;

bool pp_host_on_main_thread(void) {
#ifdef PANEPORT_TEST_BUILD
    const int state = atomic_load_explicit(&s_host.state, memory_order_acquire);
    if (state != PP_HOST_RUNNING && state != PP_HOST_STOPPED) {
        return false;
    }
    return pthread_equal(pthread_self(), s_host.main_thread) != 0;
#else
    return pp_platform_is_main_thread();
#endif
}

uint32_t pp_host_thread_role(void) {
    if (pp_event_thread_is_current()) {
        return PP_THREAD_ROLE_EVENT;
    }
    if (pp_host_on_main_thread()) {
        return PP_THREAD_ROLE_MAIN;
    }
    return PP_THREAD_ROLE_OTHER;
}

PP_EXPORT uint32_t pp_abi_version(void) {
    return ((uint32_t)PP_ABI_VERSION_MAJOR << 16U) | (uint32_t)PP_ABI_VERSION_MINOR;
}

PP_EXPORT void pp_free(void *memory) { free(memory); }

// Checks what a [main] function needs: a running host, called on the main thread.
static pp_status_t check_main_call(const char *function) {
    if (atomic_load_explicit(&s_host.state, memory_order_acquire) != PP_HOST_RUNNING) {
        return pp_error_set(PP_E_NOT_INITIALIZED, "%s needs a running host: call pp_init first.",
                            function);
    }
    if (!pp_host_on_main_thread()) {
        return pp_error_set(PP_E_WRONG_THREAD, "%s runs only on the main thread.", function);
    }
    return PP_OK;
}

PP_EXPORT pp_status_t pp_init(const pp_init_config_t *config) {
    pp_error_clear();
#ifndef PANEPORT_TEST_BUILD
    if (!pp_platform_is_main_thread()) {
        return pp_error_set(PP_E_WRONG_THREAD, "pp_init runs only on the main thread.");
    }
#endif
    if (config == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_init needs a configuration.");
    }
    if (config->struct_size < sizeof(uint32_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "pp_init needs a struct_size of at least 4, not %u.",
                            (unsigned)config->struct_size);
    }

    int expected = PP_HOST_NOT_STARTED;
    if (!atomic_compare_exchange_strong(&s_host.state, &expected, PP_HOST_STARTING)) {
        return pp_error_set(PP_E_ALREADY_INITIALIZED,
                            "pp_init runs once per process, and it already ran.");
    }

    const size_t folder_end =
        offsetof(pp_init_config_t, crash_record_folder) + sizeof config->crash_record_folder;
    const char *folder = config->struct_size >= folder_end ? config->crash_record_folder : NULL;
    pp_status_t status = PP_OK;
    if (folder != NULL) {
        status = pp_crash_record_prepare(folder);
    }
    if (status == PP_OK) {
        status = pp_event_thread_start();
        if (status != PP_OK) {
            (void)pp_error_set(status, "The host event thread could not start.");
        }
    }
    if (status != PP_OK) {
        // A folder prepared before the failure must not outlive it.
        pp_crash_record_reset();
        atomic_store_explicit(&s_host.state, PP_HOST_NOT_STARTED, memory_order_release);
        return status;
    }

    s_host.main_thread = pthread_self();
    atomic_store_explicit(&s_host.state, PP_HOST_RUNNING, memory_order_release);
    (void)pp_log(PP_LOG_INFO, "The native host started, with interface version %d.%d.",
                 PP_ABI_VERSION_MAJOR, PP_ABI_VERSION_MINOR);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_shutdown(void) {
    pp_error_clear();
    const pp_status_t status = check_main_call("pp_shutdown");
    if (status != PP_OK) {
        return status;
    }

    (void)pp_log(PP_LOG_INFO, "The native host is stopping.");
    pp_event_thread_stop();
    atomic_store_explicit(&s_host.state, PP_HOST_STOPPED, memory_order_release);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_set_event_callback(pp_event_callback_t callback, void *user_data) {
    pp_error_clear();
    const pp_status_t status = check_main_call("pp_set_event_callback");
    if (status != PP_OK) {
        return status;
    }
    pp_event_thread_set_event_callback(callback, user_data);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_set_log_callback(pp_log_callback_t callback, void *user_data) {
    pp_error_clear();
    const pp_status_t status = check_main_call("pp_set_log_callback");
    if (status != PP_OK) {
        return status;
    }
    pp_event_thread_set_log_callback(callback, user_data);
    return PP_OK;
}
