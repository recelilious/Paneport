// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.2: the routing registries. Writers change ranges, marks and the guest fault handler
// while other threads fault, and every fault must take the route that its thread's mark or its
// address calls for; a fault in the JIT pool goes to the probe only while the probe mode is on;
// full tables refuse. ThreadSanitizer checks the lock-free reads (docs/design/signals.md
// sections 3 and 4).

#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "pp_check.h"
#include "signal_test_support.h"
#include "test_support.h"

#define CHURN_PAGES 8U
#define FAULTS_PER_THREAD 20000U
#define MARKED_THREAD_LIMIT_STACK ((size_t)256U * 1024U)

// The pages of a test: one in the pool, one in a reservation, one that only the stand-in
// owns, and pages that writers register and remove while the faulting threads run.
enum { POOL_PAGE = 0, RESERVATION_PAGE = 1, RUNTIME_PAGE = 2, CHURN_PAGE = 3 };

// What the guest fault handler and the probe handler counted, by route.
typedef struct route_counts_t {
    _Atomic(uint64_t) by_route[PP_FAULT_ROUTE_JIT_PROBE + 1];
    _Atomic(uint64_t) wrong_page;
} route_counts_t;

// The documented singletons of this program (C-0010 section 3): the stand-in, which the
// runtime's handler reaches without an argument.
static pp_stand_in_t s_stand_in;

static void stand_in_handler(int signal_number, siginfo_t *info, void *context) {
    pp_stand_in_handle(&s_stand_in, signal_number, info, context);
}

typedef struct routing_test_t {
    unsigned char *pages;
    size_t page_size;
    route_counts_t guest;
    route_counts_t probe;
    _Atomic(bool) stop;
    _Atomic(uint64_t) churn_rounds;
} routing_test_t;

static unsigned char *page_at(const routing_test_t *test, uint32_t index) {
    return test->pages + ((size_t)index * test->page_size);
}

// Counts a fault by its route and skips the faulting instruction. A fault on a churn page
// would mean a faulting thread touched one, which none does.
static uint32_t count_fault(route_counts_t *counts, const routing_test_t *test,
                            pp_guest_fault_t *fault) {
    const uint64_t churn = (uint64_t)(uintptr_t)page_at(test, CHURN_PAGE);
    if (fault->fault_address >= churn &&
        fault->fault_address < churn + ((uint64_t)CHURN_PAGES * test->page_size)) {
        (void)atomic_fetch_add(&counts->wrong_page, 1U);
    }
    if (fault->route <= PP_FAULT_ROUTE_JIT_PROBE) {
        (void)atomic_fetch_add(&counts->by_route[fault->route], 1U);
    }
    pp_signal_context_set_pc(fault->context, fault->program_counter + PP_SIGNAL_INSTRUCTION_SIZE);
    return PP_GUEST_FAULT_HANDLED;
}

static uint32_t guest_handler(pp_guest_fault_t *fault, void *user_data) {
    routing_test_t *test = user_data;
    return count_fault(&test->guest, test, fault);
}

// The same handler under another name, which a writer swaps in and out.
static uint32_t other_guest_handler(pp_guest_fault_t *fault, void *user_data) {
    routing_test_t *test = user_data;
    return count_fault(&test->guest, test, fault);
}

static uint32_t probe_handler(pp_guest_fault_t *fault, void *user_data) {
    routing_test_t *test = user_data;
    return count_fault(&test->probe, test, fault);
}

static uint64_t guest_count(routing_test_t *test, uint32_t route) {
    return atomic_load(&test->guest.by_route[route]);
}

static void set_up(routing_test_t *test, uint64_t *pool, uint64_t *reservation) {
    memset(test, 0, sizeof *test);
    test->page_size = pp_test_page_size();
    test->pages = pp_map_pages(CHURN_PAGE + CHURN_PAGES);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_stand_in_own(&s_stand_in, page_at(test, RUNTIME_PAGE), test->page_size);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_signals_add_pool_range((uint64_t)(uintptr_t)page_at(test, POOL_PAGE),
                                             test->page_size, pool),
                   PP_OK);
    PP_CHECK_EQUAL(pp_signals_add_reservation((uint64_t)(uintptr_t)page_at(test, RESERVATION_PAGE),
                                              test->page_size, reservation),
                   PP_OK);
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(guest_handler, test), PP_OK);
    PP_CHECK_EQUAL(pp_signals_set_probe_handler(probe_handler, test), PP_OK);
}

// A marked thread faults on all three pages; each fault takes the route of its mark.
static void *fault_marked(void *argument) {
    const pp_thread_argument_t *thread = argument;
    routing_test_t *test = thread->context;
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_TRANSLATOR), PP_OK);
    for (uint32_t round = 0U; round < FAULTS_PER_THREAD; round++) {
        pp_signal_plant_read(page_at(test, round % 3U));
    }
    PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
    return NULL;
}

// An unmarked thread faults on all three pages; each fault takes the route of its address.
static void *fault_unmarked(void *argument) {
    const pp_thread_argument_t *thread = argument;
    routing_test_t *test = thread->context;
    for (uint32_t round = 0U; round < FAULTS_PER_THREAD; round++) {
        pp_signal_plant_read(page_at(test, round % 3U));
    }
    return NULL;
}

// Registers and removes churn pages as pool ranges and reservations, marks and unmarks
// itself, and swaps the guest fault handler, until the faulting threads are done.
static void *churn(void *argument) {
    const pp_thread_argument_t *thread = argument;
    routing_test_t *test = thread->context;
    uint64_t handles[CHURN_PAGES];
    while (!atomic_load(&test->stop)) {
        for (uint32_t index = 0U; index < CHURN_PAGES; index++) {
            const uint64_t start = (uint64_t)(uintptr_t)page_at(test, CHURN_PAGE + index);
            const pp_status_t status =
                (index % 2U) == 0U
                    ? pp_signals_add_pool_range(start, test->page_size, &handles[index])
                    : pp_signals_add_reservation(start, test->page_size, &handles[index]);
            PP_CHECK_EQUAL(status, PP_OK);
        }
        PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
        PP_CHECK_EQUAL(pp_signals_set_guest_handler(
                           (thread->number % 2U) == 0U ? other_guest_handler : guest_handler, test),
                       PP_OK);
        for (uint32_t index = 0U; index < CHURN_PAGES; index++) {
            const pp_status_t status = (index % 2U) == 0U
                                           ? pp_signals_remove_pool_range(handles[index])
                                           : pp_signals_remove_reservation(handles[index]);
            PP_CHECK_EQUAL(status, PP_OK);
        }
        PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
        (void)atomic_fetch_add(&test->churn_rounds, 1U);
    }
    return NULL;
}

// How many of a thread's faults land on a page: round r faults on page r % 3.
static uint64_t faults_on(uint32_t page) { return (FAULTS_PER_THREAD - page + 2U) / 3U; }

static void routes_hold_while_writers_change(void) {
    routing_test_t test;
    uint64_t pool = 0U;
    uint64_t reservation = 0U;
    set_up(&test, &pool, &reservation);

    enum { WRITERS = 2, MARKED = 2, UNMARKED = 2 };
    pthread_t writers[WRITERS];
    pp_thread_argument_t writer_arguments[WRITERS];
    pp_start_threads(writers, writer_arguments, WRITERS, churn, &test);
    pthread_t faulting[MARKED + UNMARKED];
    pp_thread_argument_t faulting_arguments[MARKED + UNMARKED];
    pp_start_threads(faulting, faulting_arguments, MARKED, fault_marked, &test);
    pp_start_threads(faulting + MARKED, faulting_arguments + MARKED, UNMARKED, fault_unmarked,
                     &test);
    pp_join_threads(faulting, MARKED + UNMARKED);
    atomic_store(&test.stop, true);
    pp_join_threads(writers, WRITERS);
    PP_CHECK(atomic_load(&test.churn_rounds) > 0U);

    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_THREAD_MARK),
                   (uint64_t)MARKED * FAULTS_PER_THREAD);
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_JIT_POOL),
                   (uint64_t)UNMARKED * faults_on(POOL_PAGE));
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_RESERVATION),
                   (uint64_t)UNMARKED * faults_on(RESERVATION_PAGE));
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_RUNTIME), 0U);
    PP_CHECK_EQUAL(atomic_load(&test.guest.wrong_page), 0U);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGBUS),
                   (uint64_t)UNMARKED * faults_on(RUNTIME_PAGE));
    PP_CHECK_EQUAL(atomic_load(&test.probe.by_route[PP_FAULT_ROUTE_JIT_PROBE]), 0U);

    PP_CHECK_EQUAL(pp_signals_set_guest_handler(NULL, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_signals_set_probe_handler(NULL, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_signals_remove_pool_range(pool), PP_OK);
    PP_CHECK_EQUAL(pp_signals_remove_reservation(reservation), PP_OK);
    pp_unmap_pages(test.pages, CHURN_PAGE + CHURN_PAGES);
}

static void *fault_pool_once(void *argument) {
    routing_test_t *test = argument;
    pp_signal_plant_read(page_at(test, POOL_PAGE));
    return NULL;
}

static void *fault_pool_marked(void *argument) {
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
    (void)fault_pool_once(argument);
    PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
    return NULL;
}

static void *fault_reservation_once(void *argument) {
    routing_test_t *test = argument;
    pp_signal_plant_read(page_at(test, RESERVATION_PAGE));
    return NULL;
}

// A fault in the pool goes to the probe, on a marked thread too, only while the probe mode is
// on; a fault in a reservation never does.
static void pool_fault_goes_to_probe_only_in_probe_mode(void) {
    routing_test_t test;
    uint64_t pool = 0U;
    uint64_t reservation = 0U;
    set_up(&test, &pool, &reservation);

    pp_run_on_thread(fault_pool_once, &test);
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_JIT_POOL), 1U);
    PP_CHECK_EQUAL(pp_signals_set_probe_mode(1U), PP_OK);
    pp_run_on_thread(fault_pool_once, &test);
    pp_run_on_thread(fault_pool_marked, &test);
    pp_run_on_thread(fault_reservation_once, &test);
    PP_CHECK_EQUAL(atomic_load(&test.probe.by_route[PP_FAULT_ROUTE_JIT_PROBE]), 2U);
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_RESERVATION), 1U);
    PP_CHECK_EQUAL(pp_signals_set_probe_mode(0U), PP_OK);
    pp_run_on_thread(fault_pool_once, &test);
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_JIT_POOL), 2U);
    PP_CHECK_EQUAL(atomic_load(&test.probe.by_route[PP_FAULT_ROUTE_JIT_PROBE]), 2U);
    PP_CHECK_EQUAL(pp_signals_set_probe_mode(2U), PP_E_INVALID_ARGUMENT);

    pp_signal_counters_t counters;
    pp_signals_read_counters(&counters);
    PP_CHECK_EQUAL(counters.probe_handled, 2U);
    PP_CHECK_EQUAL(counters.guest_handled, 3U);
}

static void *mark_and_end(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
    return NULL;
}

static void *fault_runtime_once(void *argument) {
    routing_test_t *test = argument;
    pp_signal_plant_read(page_at(test, RUNTIME_PAGE));
    return NULL;
}

// A thread that ends with its mark loses it, so a later thread that may get the same pthread_t
// is not taken for a guest thread.
static void mark_ends_with_its_thread(void) {
    routing_test_t test;
    uint64_t pool = 0U;
    uint64_t reservation = 0U;
    set_up(&test, &pool, &reservation);
    for (int round = 0; round < 8; round++) {
        pp_run_on_thread(mark_and_end, NULL);
        pp_run_on_thread(fault_runtime_once, &test);
    }
    PP_CHECK_EQUAL(guest_count(&test, PP_FAULT_ROUTE_THREAD_MARK), 0U);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGBUS), 8U);
}

// Full tables refuse a range; handles of removed ranges and of the other table name nothing;
// bad ranges and marks are refused.
static void tables_refuse_when_full(void) {
    pp_start_host(NULL);
    uint64_t pool[PP_SIGNAL_POOL_RANGES];
    for (uint32_t index = 0U; index < PP_SIGNAL_POOL_RANGES; index++) {
        PP_CHECK_EQUAL(
            pp_signals_add_pool_range(UINT64_C(0x10000) * (index + 1U), 0x1000U, &pool[index]),
            PP_OK);
        PP_CHECK(pool[index] != 0U);
    }
    uint64_t extra = 0U;
    PP_CHECK_EQUAL(pp_signals_add_pool_range(0x7000000U, 0x1000U, &extra), PP_E_OUT_OF_MEMORY);
    PP_CHECK_EQUAL(pp_signals_remove_reservation(pool[0]), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_remove_pool_range(pool[0]), PP_OK);
    PP_CHECK_EQUAL(pp_signals_remove_pool_range(pool[0]), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_add_pool_range(0x7000000U, 0x1000U, &extra), PP_OK);
    PP_CHECK(extra != pool[0]);

    uint64_t reservations[PP_SIGNAL_RESERVATIONS];
    for (uint32_t index = 0U; index < PP_SIGNAL_RESERVATIONS; index++) {
        PP_CHECK_EQUAL(pp_signals_add_reservation(UINT64_C(0x10000) * (index + 1U), 0x1000U,
                                                  &reservations[index]),
                       PP_OK);
    }
    PP_CHECK_EQUAL(pp_signals_add_reservation(0x7000000U, 0x1000U, &extra), PP_E_OUT_OF_MEMORY);

    PP_CHECK_EQUAL(pp_signals_add_reservation(0U, 0x1000U, &extra), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_add_reservation(0x1000U, 0U, &extra), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_add_reservation(UINT64_MAX - 0xFFFU, 0x1000U, &extra),
                   PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_add_reservation(0x1000U, 0x1000U, NULL), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_remove_reservation(0U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_NONE), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_signals_mark_thread(3U), PP_E_INVALID_ARGUMENT);
}

typedef struct mark_table_test_t {
    _Atomic(uint32_t) marked;
    _Atomic(uint32_t) failed;
    _Atomic(bool) release;
} mark_table_test_t;

static void *mark_and_wait(void *argument) {
    const pp_thread_argument_t *thread = argument;
    mark_table_test_t *test = thread->context;
    if (pp_signals_mark_thread(PP_THREAD_MARK_GUEST) == PP_OK) {
        (void)atomic_fetch_add(&test->marked, 1U);
    } else {
        (void)atomic_fetch_add(&test->failed, 1U);
    }
    pp_wait_for_flag(&test->release);
    PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
    return NULL;
}

// The thread table holds PP_SIGNAL_MARKED_THREADS threads, and refuses one more.
static void marked_thread_table_full(void) {
    pp_start_host(NULL);
    mark_table_test_t test;
    memset(&test, 0, sizeof test);
    enum { COUNT = PP_SIGNAL_MARKED_THREADS };
    pthread_t threads[COUNT];
    pp_thread_argument_t arguments[COUNT];
    pthread_attr_t attributes;
    PP_CHECK_EQUAL(pthread_attr_init(&attributes), 0);
    PP_CHECK_EQUAL(pthread_attr_setstacksize(&attributes, MARKED_THREAD_LIMIT_STACK), 0);
    for (uint32_t index = 0U; index < COUNT; index++) {
        arguments[index].context = &test;
        arguments[index].number = index;
        PP_CHECK_EQUAL(
            pthread_create(&threads[index], &attributes, mark_and_wait, &arguments[index]), 0);
    }
    PP_CHECK_EQUAL(pthread_attr_destroy(&attributes), 0);
    for (int round = 0;
         round < 60000 && atomic_load(&test.marked) + atomic_load(&test.failed) < (uint32_t)COUNT;
         round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK_EQUAL(atomic_load(&test.marked), COUNT);
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_E_OUT_OF_MEMORY);
    atomic_store(&test.release, true);
    pp_join_threads(threads, COUNT);
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
    PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"routes_hold_while_writers_change", routes_hold_while_writers_change},
        {
            "pool_fault_goes_to_probe_only_in_probe_mode",
            pool_fault_goes_to_probe_only_in_probe_mode,
        },
        {"mark_ends_with_its_thread", mark_ends_with_its_thread},
        {"tables_refuse_when_full", tables_refuse_when_full},
        {"marked_thread_table_full", marked_thread_table_full},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
