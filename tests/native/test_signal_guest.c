// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S2.2.3: the guest fault handler registration. The test handler sees only faults of the guest
// path; a declined fault, or a guest fault with no handler set, ends the app with a crash
// record, which the case reads after its child ended by the signal; replacing a handler waits
// for the faults still in it (docs/design/signals.md section 6).

#include <pthread.h>
#include <sched.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "crash_record.h"
#include "pp_check.h"
#include "signal_test_support.h"
#include "test_support.h"

// The documented singletons of this program (C-0010 section 3): the stand-in, which the
// runtime's handler reaches without an argument, and the path that the program started with,
// which a case needs to start a child.
static pp_stand_in_t s_stand_in;
static const char *s_program;

static void stand_in_handler(int signal_number, siginfo_t *info, void *context) {
    pp_stand_in_handle(&s_stand_in, signal_number, info, context);
}

enum { GUEST_PAGE = 0, RESERVATION_PAGE = 1, RUNTIME_PAGE = 2, PAGE_COUNT = 3 };

typedef struct guest_test_t {
    unsigned char *pages;
    size_t page_size;
    _Atomic(uint64_t) seen;
    _Atomic(uint64_t) seen_by_route[PP_FAULT_ROUTE_JIT_PROBE + 1];
    _Atomic(uint64_t) seen_on_runtime_page;
    _Atomic(bool) entered;
    _Atomic(bool) release;
} guest_test_t;

static unsigned char *page_at(const guest_test_t *test, uint32_t index) {
    return test->pages + ((size_t)index * test->page_size);
}

// Counts what it sees and goes on after the faulting instruction; a signal that a thread sent
// has no instruction to skip.
static uint32_t counting_handler(pp_guest_fault_t *fault, void *user_data) {
    guest_test_t *test = user_data;
    PP_CHECK_EQUAL(fault->struct_size, sizeof(pp_guest_fault_t));
    (void)atomic_fetch_add(&test->seen, 1U);
    if (fault->route <= PP_FAULT_ROUTE_JIT_PROBE) {
        (void)atomic_fetch_add(&test->seen_by_route[fault->route], 1U);
    }
    const uint64_t runtime = (uint64_t)(uintptr_t)page_at(test, RUNTIME_PAGE);
    if (fault->fault_address >= runtime && fault->fault_address < runtime + test->page_size &&
        fault->route != PP_FAULT_ROUTE_THREAD_MARK) {
        (void)atomic_fetch_add(&test->seen_on_runtime_page, 1U);
    }
    if (fault->signal == SIGSEGV || fault->signal == SIGBUS) {
        pp_signal_context_set_pc(fault->context,
                                 fault->program_counter + PP_SIGNAL_INSTRUCTION_SIZE);
    }
    return PP_GUEST_FAULT_HANDLED;
}

static void set_up(guest_test_t *test, const char *folder) {
    memset(test, 0, sizeof *test);
    test->page_size = pp_test_page_size();
    test->pages = pp_map_pages(PAGE_COUNT);
    pp_stand_in_install(&s_stand_in, stand_in_handler);
    pp_stand_in_own(&s_stand_in, page_at(test, RUNTIME_PAGE), test->page_size);
    pp_start_host(folder);
}

static void *fault_as_guest(void *argument) {
    guest_test_t *test = argument;
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
    pp_signal_plant_read(page_at(test, RUNTIME_PAGE));
    pp_signal_plant_write(page_at(test, GUEST_PAGE));
    PP_CHECK_EQUAL(pthread_kill(pthread_self(), SIGFPE), 0);
    PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
    return NULL;
}

static void *fault_unmarked(void *argument) {
    guest_test_t *test = argument;
    pp_signal_plant_read(page_at(test, RESERVATION_PAGE));
    pp_signal_plant_read(page_at(test, RUNTIME_PAGE));
    PP_CHECK_EQUAL(pthread_kill(pthread_self(), SIGABRT), 0);
    return NULL;
}

// The handler sees the three faults of the marked thread and the reservation fault of the
// unmarked one; the stand-in sees the unmarked thread's other two signals.
static void handler_sees_only_guest_faults(void) {
    guest_test_t test;
    set_up(&test, NULL);
    uint64_t reservation = 0U;
    PP_CHECK_EQUAL(pp_signals_add_reservation((uint64_t)(uintptr_t)page_at(&test, RESERVATION_PAGE),
                                              test.page_size, &reservation),
                   PP_OK);
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(counting_handler, &test), PP_OK);

    pp_run_on_thread(fault_as_guest, &test);
    pp_run_on_thread(fault_unmarked, &test);

    PP_CHECK_EQUAL(atomic_load(&test.seen), 4U);
    PP_CHECK_EQUAL(atomic_load(&test.seen_by_route[PP_FAULT_ROUTE_THREAD_MARK]), 3U);
    PP_CHECK_EQUAL(atomic_load(&test.seen_by_route[PP_FAULT_ROUTE_RESERVATION]), 1U);
    PP_CHECK_EQUAL(atomic_load(&test.seen_on_runtime_page), 0U);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGBUS), 1U);
    PP_CHECK_EQUAL(pp_stand_in_count(&s_stand_in, SIGABRT), 1U);

    pp_signal_counters_t counters;
    pp_signals_read_counters(&counters);
    PP_CHECK_EQUAL(counters.guest_handled, 4U);
    PP_CHECK_EQUAL(counters.guest_declined, 0U);
    PP_CHECK_EQUAL(counters.runtime, 2U);
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(NULL, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_signals_remove_reservation(reservation), PP_OK);
    pp_unmap_pages(test.pages, PAGE_COUNT);
}

static uint32_t declining_handler(pp_guest_fault_t *fault, void *user_data) {
    (void)fault;
    (void)user_data;
    return PP_GUEST_FAULT_DECLINED;
}

typedef struct child_fault_t {
    unsigned char *page;
    const char *folder;
} child_fault_t;

static void *fault_marked_in_child(void *argument) {
    const child_fault_t *fault = argument;
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
    pp_write_planted(fault->folder, fault->page);
    pp_signal_plant_read(fault->page);
    return NULL;
}

// Child: its handler declines a fault on a marked thread.
static void child_declines(const char *folder) {
    pp_start_host(folder);
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(declining_handler, NULL), PP_OK);
    child_fault_t fault = {.page = pp_map_pages(1U), .folder = folder};
    pp_run_on_thread(fault_marked_in_child, &fault);
}

// Child: a marked thread faults while no handler is set.
static void child_without_handler(const char *folder) {
    pp_start_host(folder);
    child_fault_t fault = {.page = pp_map_pages(1U), .folder = folder};
    pp_run_on_thread(fault_marked_in_child, &fault);
}

static void check_declined(const char *child_case) {
    char folder[64];
    pp_make_folder(folder, sizeof folder);
    pp_check_ended_by(pp_run_child(s_program, child_case, folder), SIGBUS);
    const uint64_t planted = pp_read_planted(folder);
    pp_test_record_t record;
    pp_read_crash_record(folder, &record);
    PP_CHECK_EQUAL(record.signal, SIGBUS);
    PP_CHECK(record.code > 0);
    PP_CHECK_EQUAL(record.thread_role, PP_THREAD_ROLE_GUEST);
    PP_CHECK_EQUAL(record.fault_address, planted);
    PP_CHECK(record.program_counter != 0U);
    PP_CHECK_TEXT(record.message, "The guest fault handler declined a fault.");
    pp_remove_record_folder(folder);
}

static void declined_fault_ends_with_record(void) { check_declined("declines"); }

static void fault_without_handler_ends_with_record(void) { check_declined("without_handler"); }

// Holds the first fault until the test releases it.
static uint32_t holding_handler(pp_guest_fault_t *fault, void *user_data) {
    guest_test_t *test = user_data;
    atomic_store(&test->entered, true);
    while (!atomic_load(&test->release)) {
        (void)sched_yield();
    }
    pp_signal_context_set_pc(fault->context, fault->program_counter + PP_SIGNAL_INSTRUCTION_SIZE);
    return PP_GUEST_FAULT_HANDLED;
}

typedef struct clear_test_t {
    guest_test_t *test;
    _Atomic(bool) cleared;
} clear_test_t;

static void *fault_held(void *argument) {
    guest_test_t *test = argument;
    PP_CHECK_EQUAL(pp_signals_mark_thread(PP_THREAD_MARK_GUEST), PP_OK);
    pp_signal_plant_read(page_at(test, GUEST_PAGE));
    PP_CHECK_EQUAL(pp_signals_unmark_thread(), PP_OK);
    return NULL;
}

static void *clear_handler(void *argument) {
    clear_test_t *clear = argument;
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(NULL, NULL), PP_OK);
    atomic_store(&clear->cleared, true);
    return NULL;
}

// Removing the handler returns only once the fault inside it has left.
static void removal_waits_for_handler(void) {
    guest_test_t test;
    set_up(&test, NULL);
    PP_CHECK_EQUAL(pp_signals_set_guest_handler(holding_handler, &test), PP_OK);
    const pthread_t faulting = pp_start_thread(fault_held, &test);
    pp_wait_for_flag(&test.entered);

    clear_test_t clear = {.test = &test};
    atomic_init(&clear.cleared, false);
    const pthread_t clearing = pp_start_thread(clear_handler, &clear);
    pp_sleep_milliseconds(200);
    PP_CHECK(!atomic_load(&clear.cleared));
    atomic_store(&test.release, true);
    pp_join_thread(clearing);
    pp_join_thread(faulting);
    PP_CHECK(atomic_load(&clear.cleared));
    PP_CHECK(!pp_signals_has_guest_handler());
    pp_unmap_pages(test.pages, PAGE_COUNT);
}

int main(int argc, char **argv) {
    s_program = argv[0];
    static const pp_test_case_t cases[] = {
        {"handler_sees_only_guest_faults", handler_sees_only_guest_faults},
        {"declined_fault_ends_with_record", declined_fault_ends_with_record},
        {"fault_without_handler_ends_with_record", fault_without_handler_ends_with_record},
        {"removal_waits_for_handler", removal_waits_for_handler},
    };
    static const pp_child_case_t children[] = {
        {"declines", child_declines},
        {"without_handler", child_without_handler},
    };
    return pp_signal_test_main(argc, argv, cases, PP_CASE_COUNT(cases), children,
                               PP_CASE_COUNT(children));
}
