// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The native part of the signal gate's stress harness, and its exports of paneport/testing.h
// (docs/design/signals.md section 9). CMake compiles this file into test builds only. Marked
// threads fault on a guard page and on a read-only page, unmarked threads fault inside a
// registered guest reservation, and the harness's guest fault handler counts each fault by
// its route and skips the faulting instruction. A fault that takes another route counts as
// unexpected; a fault outside the harness's pages is declined, which ends the app.

#include <pthread.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>
#include <time.h>
#include <unistd.h>

#include "error.h"
#include "paneport/testing.h"
#include "signal_harness.h"
#include "signals/signal_context.h"
#include "signals/signals.h"

#define PP_HARNESS_MAX_THREADS_PER_KIND 16U
#define PP_HARNESS_MAX_PAUSE_MICROSECONDS 1000000U

// The harness: a process-wide singleton (C-0010 section 3), because the device test page and
// the CTest runner start and stop one run at a time. The mutex serializes start and stop; the
// threads and the handler share only the atomics and the pages of pp_harness_faults, which
// are set before the threads start and unmapped after they end.
static struct {
    pthread_mutex_t mutex;
    bool running;
    _Atomic(bool) stopping;
    pthread_t threads[2U * PP_HARNESS_MAX_THREADS_PER_KIND];
    uint32_t numbers[2U * PP_HARNESS_MAX_THREADS_PER_KIND]; // Each thread's argument.
    uint32_t thread_count;
    uint64_t reservation;
    uint32_t pause_microseconds;
    _Atomic(bool) active;
    _Atomic(uint64_t) raised_by_mark;
    _Atomic(uint64_t) raised_by_address;
} s_harness = {.mutex = PTHREAD_MUTEX_INITIALIZER};

static void pause_thread(void) {
    const uint32_t pause = s_harness.pause_microseconds;
    if (pause == 0U) {
        return;
    }
    const struct timespec duration = {
        .tv_sec = (time_t)(pause / 1000000U),
        .tv_nsec = (long)(pause % 1000000U) * 1000L,
    };
    (void)nanosleep(&duration, NULL);
}

static void *run_marked_thread(void *argument) {
    (void)argument;
    if (pp_signals_mark_thread(PP_THREAD_MARK_GUEST) != PP_OK) {
        (void)atomic_fetch_add_explicit(&pp_harness_faults.unexpected, 1U, memory_order_relaxed);
        return NULL;
    }
    unsigned char *guard =
        pp_harness_faults.pages + ((size_t)PP_HARNESS_GUARD_PAGE * pp_harness_faults.page_size);
    unsigned char *read_only =
        pp_harness_faults.pages + ((size_t)PP_HARNESS_READ_ONLY_PAGE * pp_harness_faults.page_size);
    // The thread reads the guard page and writes the read-only page in turn.
    uint32_t round = 0U;
    while (!atomic_load_explicit(&s_harness.stopping, memory_order_acquire)) {
        (void)atomic_fetch_add_explicit(&s_harness.raised_by_mark, 1U, memory_order_relaxed);
        if ((round % 2U) == 0U) {
            pp_signal_plant_read(guard);
        } else {
            pp_signal_plant_write(read_only);
        }
        round++;
        pause_thread();
    }
    (void)pp_signals_unmark_thread();
    return NULL;
}

static void *run_unmarked_thread(void *argument) {
    // Each unmarked thread reads at an offset of its own.
    const uint32_t *number = argument;
    const size_t offset = ((size_t)*number * 64U) % pp_harness_faults.page_size;
    unsigned char *reservation = pp_harness_faults.pages + ((size_t)PP_HARNESS_RESERVATION_PAGE *
                                                            pp_harness_faults.page_size);
    uint32_t page = 0U;
    while (!atomic_load_explicit(&s_harness.stopping, memory_order_acquire)) {
        (void)atomic_fetch_add_explicit(&s_harness.raised_by_address, 1U, memory_order_relaxed);
        pp_signal_plant_read(reservation + ((size_t)page * pp_harness_faults.page_size) + offset);
        page = (page + 1U) % PP_HARNESS_RESERVATION_PAGES;
        pause_thread();
    }
    return NULL;
}

// Maps the pages: the guard page and the reservation can be neither read nor written, and the
// read-only page only read.
static pp_status_t map_pages(void) {
    const long page = sysconf(_SC_PAGESIZE);
    if (page <= 0) {
        return pp_error_set(PP_E_SYSTEM, "The system did not report its page size.");
    }
    pp_harness_faults.page_size = (size_t)page;
    const size_t size = (size_t)PP_HARNESS_PAGE_COUNT * pp_harness_faults.page_size;
    unsigned char *pages = mmap(NULL, size, PROT_NONE, MAP_PRIVATE | MAP_ANON, -1, 0);
    if (pages == MAP_FAILED) {
        return pp_error_set(PP_E_OUT_OF_MEMORY, "The harness's pages could not be mapped.");
    }
    if (mprotect(pages + ((size_t)PP_HARNESS_READ_ONLY_PAGE * pp_harness_faults.page_size),
                 pp_harness_faults.page_size, PROT_READ) != 0) {
        (void)munmap(pages, size);
        return pp_error_set(PP_E_SYSTEM, "The harness's read-only page could not be set.");
    }
    pp_harness_faults.pages = pages;
    return PP_OK;
}

static void unmap_pages(void) {
    if (pp_harness_faults.pages != NULL) {
        (void)munmap(pp_harness_faults.pages,
                     (size_t)PP_HARNESS_PAGE_COUNT * pp_harness_faults.page_size);
        pp_harness_faults.pages = NULL;
    }
}

// Ends the threads and takes the handler, the reservation and the pages away. Called with the
// mutex held.
static void stop_locked(void) {
    atomic_store_explicit(&s_harness.stopping, true, memory_order_release);
    for (uint32_t index = 0U; index < s_harness.thread_count; index++) {
        (void)pthread_join(s_harness.threads[index], NULL);
    }
    s_harness.thread_count = 0U;
    (void)pp_signals_set_guest_handler(NULL, NULL);
    if (s_harness.reservation != 0U) {
        (void)pp_signals_remove_reservation(s_harness.reservation);
        s_harness.reservation = 0U;
    }
    unmap_pages();
    s_harness.running = false;
    atomic_store_explicit(&s_harness.active, false, memory_order_release);
}

static pp_status_t check_config(const pp_test_signal_harness_config_t *config) {
    if (config == NULL || config->struct_size < sizeof(pp_test_signal_harness_config_t) ||
        config->marked_threads > PP_HARNESS_MAX_THREADS_PER_KIND ||
        config->unmarked_threads > PP_HARNESS_MAX_THREADS_PER_KIND ||
        config->pause_microseconds > PP_HARNESS_MAX_PAUSE_MICROSECONDS) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "The harness needs a whole configuration with at most %u threads of "
                            "each kind and a pause of at most one second.",
                            PP_HARNESS_MAX_THREADS_PER_KIND);
    }
    if (pp_signals_check_installed(NULL) == PP_E_NOT_INITIALIZED) {
        return pp_error_set(PP_E_NOT_INITIALIZED,
                            "The harness needs the signal dispatcher: call pp_init first.");
    }
    return PP_OK;
}

static void reset_counters(void) {
    atomic_store_explicit(&s_harness.raised_by_mark, 0U, memory_order_relaxed);
    atomic_store_explicit(&pp_harness_faults.handled_by_mark, 0U, memory_order_relaxed);
    atomic_store_explicit(&s_harness.raised_by_address, 0U, memory_order_relaxed);
    atomic_store_explicit(&pp_harness_faults.handled_by_address, 0U, memory_order_relaxed);
    atomic_store_explicit(&pp_harness_faults.unexpected, 0U, memory_order_relaxed);
}

static pp_status_t start_threads(uint32_t marked, uint32_t unmarked) {
    for (uint32_t index = 0U; index < marked + unmarked; index++) {
        void *(*run)(void *) = index < marked ? run_marked_thread : run_unmarked_thread;
        s_harness.numbers[index] = index;
        if (pthread_create(&s_harness.threads[s_harness.thread_count], NULL, run,
                           &s_harness.numbers[index]) != 0) {
            return pp_error_set(PP_E_SYSTEM, "A harness thread could not start.");
        }
        s_harness.thread_count++;
    }
    return PP_OK;
}

PP_EXPORT pp_status_t pp_test_signal_harness_start(const pp_test_signal_harness_config_t *config) {
    pp_error_clear();
    pp_status_t status = check_config(config);
    if (status != PP_OK) {
        return status;
    }

    (void)pthread_mutex_lock(&s_harness.mutex);
    if (s_harness.running) {
        (void)pthread_mutex_unlock(&s_harness.mutex);
        return pp_error_set(PP_E_BUSY, "The harness is running already.");
    }
    if (pp_signals_has_guest_handler()) {
        (void)pthread_mutex_unlock(&s_harness.mutex);
        return pp_error_set(PP_E_BUSY, "Another guest fault handler is set.");
    }

    reset_counters();
    s_harness.pause_microseconds = config->pause_microseconds;
    atomic_store_explicit(&s_harness.stopping, false, memory_order_release);
    status = map_pages();
    if (status == PP_OK) {
        status = pp_signals_add_reservation(
            (uint64_t)(uintptr_t)(pp_harness_faults.pages + ((size_t)PP_HARNESS_RESERVATION_PAGE *
                                                             pp_harness_faults.page_size)),
            (uint64_t)PP_HARNESS_RESERVATION_PAGES * pp_harness_faults.page_size,
            &s_harness.reservation);
    }
    if (status == PP_OK) {
        (void)pp_signals_set_guest_handler(pp_harness_handle_fault, NULL);
        s_harness.running = true;
        atomic_store_explicit(&s_harness.active, true, memory_order_release);
        status = start_threads(config->marked_threads, config->unmarked_threads);
    }
    if (status != PP_OK) {
        stop_locked();
    }
    (void)pthread_mutex_unlock(&s_harness.mutex);
    return status;
}

PP_EXPORT pp_status_t pp_test_signal_harness_stop(void) {
    pp_error_clear();
    (void)pthread_mutex_lock(&s_harness.mutex);
    if (s_harness.running) {
        stop_locked();
    }
    (void)pthread_mutex_unlock(&s_harness.mutex);
    return PP_OK;
}

PP_EXPORT pp_status_t pp_test_signal_harness_read(pp_test_signal_counters_t *counters) {
    pp_error_clear();
    if (counters == NULL) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "pp_test_signal_harness_read needs an output.");
    }
    pp_signal_counters_t dispatcher;
    pp_signals_read_counters(&dispatcher);
    counters->struct_size = (uint32_t)sizeof(pp_test_signal_counters_t);
    counters->running = atomic_load_explicit(&s_harness.active, memory_order_acquire) ? 1U : 0U;
    counters->guest_faults_raised_by_mark =
        atomic_load_explicit(&s_harness.raised_by_mark, memory_order_relaxed);
    counters->guest_faults_handled_by_mark =
        atomic_load_explicit(&pp_harness_faults.handled_by_mark, memory_order_relaxed);
    counters->guest_faults_raised_by_address =
        atomic_load_explicit(&s_harness.raised_by_address, memory_order_relaxed);
    counters->guest_faults_handled_by_address =
        atomic_load_explicit(&pp_harness_faults.handled_by_address, memory_order_relaxed);
    counters->unexpected_faults =
        atomic_load_explicit(&pp_harness_faults.unexpected, memory_order_relaxed);
    counters->runtime_faults = dispatcher.runtime;
    return PP_OK;
}
