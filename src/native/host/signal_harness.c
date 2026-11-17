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

// The members of a configuration that a caller built against an older paneport/testing.h does
// not know take these values: the run of the signal gate page (signals.md section 9).
#define PP_HARNESS_DEFAULT_MARKED_THREADS 4U
#define PP_HARNESS_DEFAULT_UNMARKED_THREADS 4U
#define PP_HARNESS_DEFAULT_PAUSE_MICROSECONDS 200U

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

// Whether a member at offset with size lies entirely inside the caller's struct_size, the
// rule of docs/design/native-host.md section 2.
static bool fits(uint32_t struct_size, size_t offset, size_t size) {
    return offset + size <= (size_t)struct_size;
}

// Reads the members of a configuration that lie inside its struct_size into full, with the
// defaults for the others.
static void read_config(const pp_test_signal_harness_config_t *config,
                        pp_test_signal_harness_config_t *full) {
    full->struct_size = (uint32_t)sizeof *full;
    full->marked_threads = PP_HARNESS_DEFAULT_MARKED_THREADS;
    full->unmarked_threads = PP_HARNESS_DEFAULT_UNMARKED_THREADS;
    full->pause_microseconds = PP_HARNESS_DEFAULT_PAUSE_MICROSECONDS;
    if (fits(config->struct_size, offsetof(pp_test_signal_harness_config_t, marked_threads),
             sizeof config->marked_threads)) {
        full->marked_threads = config->marked_threads;
    }
    if (fits(config->struct_size, offsetof(pp_test_signal_harness_config_t, unmarked_threads),
             sizeof config->unmarked_threads)) {
        full->unmarked_threads = config->unmarked_threads;
    }
    if (fits(config->struct_size, offsetof(pp_test_signal_harness_config_t, pause_microseconds),
             sizeof config->pause_microseconds)) {
        full->pause_microseconds = config->pause_microseconds;
    }
}

static pp_status_t check_config(const pp_test_signal_harness_config_t *config) {
    if (config->marked_threads > PP_HARNESS_MAX_THREADS_PER_KIND ||
        config->unmarked_threads > PP_HARNESS_MAX_THREADS_PER_KIND ||
        config->pause_microseconds > PP_HARNESS_MAX_PAUSE_MICROSECONDS) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "The harness needs at most %u threads of each kind and a pause of at "
                            "most one second.",
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
    if (config == NULL || config->struct_size < sizeof(uint32_t)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "The harness needs a configuration with a struct_size of at least 4.");
    }
    pp_test_signal_harness_config_t run;
    read_config(config, &run);
    pp_status_t status = check_config(&run);
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
    s_harness.pause_microseconds = run.pause_microseconds;
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
        status = start_threads(run.marked_threads, run.unmarked_threads);
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
    const uint32_t struct_size = counters->struct_size;
    if (struct_size < sizeof(uint32_t)) {
        return pp_error_set(
            PP_E_INVALID_ARGUMENT,
            "pp_test_signal_harness_read needs a struct_size of at least 4, not %u.",
            (unsigned)struct_size);
    }
    pp_signal_counters_t dispatcher;
    pp_signals_read_counters(&dispatcher);
    pp_test_signal_counters_t full = {
        .struct_size = struct_size,
        .running = atomic_load_explicit(&s_harness.active, memory_order_acquire) ? 1U : 0U,
        .guest_faults_raised_by_mark =
            atomic_load_explicit(&s_harness.raised_by_mark, memory_order_relaxed),
        .guest_faults_handled_by_mark =
            atomic_load_explicit(&pp_harness_faults.handled_by_mark, memory_order_relaxed),
        .guest_faults_raised_by_address =
            atomic_load_explicit(&s_harness.raised_by_address, memory_order_relaxed),
        .guest_faults_handled_by_address =
            atomic_load_explicit(&pp_harness_faults.handled_by_address, memory_order_relaxed),
        .unexpected_faults =
            atomic_load_explicit(&pp_harness_faults.unexpected, memory_order_relaxed),
        .runtime_faults = dispatcher.runtime,
    };

    // A caller built against an older header knows fewer members; write only those, and leave
    // its struct_size as it set it.
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, running), sizeof full.running)) {
        counters->running = full.running;
    }
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, guest_faults_raised_by_mark),
             sizeof full.guest_faults_raised_by_mark)) {
        counters->guest_faults_raised_by_mark = full.guest_faults_raised_by_mark;
    }
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, guest_faults_handled_by_mark),
             sizeof full.guest_faults_handled_by_mark)) {
        counters->guest_faults_handled_by_mark = full.guest_faults_handled_by_mark;
    }
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, guest_faults_raised_by_address),
             sizeof full.guest_faults_raised_by_address)) {
        counters->guest_faults_raised_by_address = full.guest_faults_raised_by_address;
    }
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, guest_faults_handled_by_address),
             sizeof full.guest_faults_handled_by_address)) {
        counters->guest_faults_handled_by_address = full.guest_faults_handled_by_address;
    }
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, unexpected_faults),
             sizeof full.unexpected_faults)) {
        counters->unexpected_faults = full.unexpected_faults;
    }
    if (fits(struct_size, offsetof(pp_test_signal_counters_t, runtime_faults),
             sizeof full.runtime_faults)) {
        counters->runtime_faults = full.runtime_faults;
    }
    return PP_OK;
}
