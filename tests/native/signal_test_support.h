// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Helpers of the signal dispatcher's tests: a stand-in for the runtime's handler, pages to
// fault on, a fault of each of the six signals, child processes that are expected to end by a
// signal, and the crash record they leave. A signal test program runs a child case as
// "<program> child <case> <folder>", which pp_signal_test_main handles before the usual cases.

#ifndef PANEPORT_TESTS_SIGNAL_TEST_SUPPORT_H
#define PANEPORT_TESTS_SIGNAL_TEST_SUPPORT_H

#include <crt_externs.h>
#include <pthread.h>
#include <signal.h>
#include <spawn.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/mman.h>
#include <sys/wait.h>
#include <unistd.h>

#include "paneport/host.h"
#include "pp_check.h"
#include "signals/signal_context.h"
#include "signals/signal_state.h"
#include "signals/signals.h"
#include "test_support.h"

// The environment that a child inherits; Apple declares environ for programs only through this.
#define PP_ENVIRONMENT (*_NSGetEnviron())

// The stand-in for the runtime's handler. It resolves a fault in the pages it owns, or one
// that a thread sent, as the runtime resolves a managed null reference. A fault of an
// instruction elsewhere it treats as Mono does with crash chaining off (signals.md section 5):
// a SIGILL, and a fault in managed code when the test says the fault lies there, end in the
// crash path, without chaining; any other fault it passes to the handler it found when it was
// installed. A handler receives no argument of the test, so each signal test program keeps
// one of these as its documented singleton (C-0010 section 3).
typedef struct pp_stand_in_t {
    _Atomic(uint64_t) counts[PP_SIGNAL_COUNT]; // Faults received, per signal index.
    _Atomic(int) last_signal;
    _Atomic(int) last_code;
    _Atomic(uint64_t) last_address;
    _Atomic(uint64_t) last_program_counter;
    // A copy of the last siginfo, kept when keeps_info is set, for a test that faults on one
    // thread at a time and reads it once that thread ended.
    _Atomic(bool) keeps_info;
    siginfo_t last_info;
    _Atomic(bool) owns_all;         // Whether it resolves every fault of an instruction.
    _Atomic(uintptr_t) owned_start; // Else the pages that it resolves.
    _Atomic(uintptr_t) owned_end;
    _Atomic(bool) in_managed_code; // Whether a fault that it does not own lies in managed code.
    struct sigaction previous[PP_SIGNAL_COUNT];
} pp_stand_in_t;

// The six signals, in the order of the dispatcher's table.
static inline int pp_test_signal_at(size_t index) {
    const int signals[PP_SIGNAL_COUNT] = {SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP, SIGABRT};
    return index < PP_SIGNAL_COUNT ? signals[index] : 0;
}

static inline int pp_test_signal_index(int signal_number) {
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        if (pp_test_signal_at(index) == signal_number) {
            return (int)index;
        }
    }
    return -1;
}

// Whether the tests raise a signal with an instruction: SIGSEGV, SIGBUS, SIGILL and SIGTRAP; they
// send SIGFPE and SIGABRT with pthread_kill (pp_raise_signal). The stand-in tells the two apart
// by the signal, not by si_code, because Darwin gives the SIGTRAP of a breakpoint instruction the
// code of a sent signal.
static inline bool pp_test_planted_instruction(int signal_number) {
    switch (signal_number) {
    case SIGFPE:
    case SIGABRT:
        return false;
    default:
        return true;
    }
}

// Mono's crash path with crash chaining off: SIGABRT goes back to the default action, which
// takes the dispatcher away from it, and abort() ends the process without chaining.
static inline void pp_stand_in_crash_path(void) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_handler = SIG_DFL;
    (void)sigemptyset(&action.sa_mask);
    (void)sigaction(SIGABRT, &action, NULL);
    abort();
}

// The body of a stand-in's handler. Each test program's handler calls it with its singleton.
static inline void pp_stand_in_handle(pp_stand_in_t *stand_in, int signal_number, siginfo_t *info,
                                      void *context) {
    const int index = pp_test_signal_index(signal_number);
    if (index < 0) {
        return;
    }
    const uint64_t address = (uint64_t)(uintptr_t)info->si_addr;
    atomic_store(&stand_in->last_signal, signal_number);
    atomic_store(&stand_in->last_code, info->si_code);
    atomic_store(&stand_in->last_address, address);
    atomic_store(&stand_in->last_program_counter, pp_signal_context_pc(context));
    if (atomic_load(&stand_in->keeps_info)) {
        memcpy(&stand_in->last_info, info, sizeof stand_in->last_info);
    }
    if (!pp_test_planted_instruction(signal_number)) {
        (void)atomic_fetch_add(&stand_in->counts[index], 1U);
        return;
    }
    const bool owned =
        atomic_load(&stand_in->owns_all) || (address >= atomic_load(&stand_in->owned_start) &&
                                             address < atomic_load(&stand_in->owned_end));
    if (owned) {
        (void)atomic_fetch_add(&stand_in->counts[index], 1U);
        pp_signal_context_set_pc(context,
                                 pp_signal_context_pc(context) + PP_SIGNAL_INSTRUCTION_SIZE);
        return;
    }
    if (signal_number == SIGILL || atomic_load(&stand_in->in_managed_code)) {
        pp_stand_in_crash_path();
    }
    // A fault that nothing resolves would fault again for ever, so without a handler to pass
    // it to the process ends.
    const struct sigaction *previous = &stand_in->previous[index];
    if (pp_signal_is_default(previous) || pp_signal_is_ignored(previous) ||
        !pp_signal_has_flag(previous->sa_flags, SA_SIGINFO)) {
        _exit(3);
    }
    previous->sa_sigaction(signal_number, info, context);
}

// Installs a stand-in's handler for the six signals and keeps what each had.
static inline void pp_stand_in_install(pp_stand_in_t *stand_in,
                                       void (*handler)(int, siginfo_t *, void *)) {
    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_sigaction = handler;
    action.sa_flags = SA_SIGINFO;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    for (size_t index = 0U; index < PP_SIGNAL_COUNT; index++) {
        PP_CHECK_EQUAL(sigaction(pp_test_signal_at(index), &action, &stand_in->previous[index]), 0);
    }
}

static inline void pp_stand_in_own(pp_stand_in_t *stand_in, const void *start, size_t size) {
    atomic_store(&stand_in->owned_start, (uintptr_t)start);
    atomic_store(&stand_in->owned_end, (uintptr_t)start + size);
}

static inline uint64_t pp_stand_in_count(pp_stand_in_t *stand_in, int signal_number) {
    return atomic_load(&stand_in->counts[pp_test_signal_index(signal_number)]);
}

static inline size_t pp_test_page_size(void) {
    const long page = sysconf(_SC_PAGESIZE);
    PP_CHECK(page > 0);
    return (size_t)page;
}

// Maps count pages that can be neither read nor written.
static inline unsigned char *pp_map_pages(size_t count) {
    void *pages = mmap(NULL, count * pp_test_page_size(), PROT_NONE, MAP_PRIVATE | MAP_ANON, -1, 0);
    PP_CHECK(pages != MAP_FAILED);
    return pages;
}

static inline void pp_unmap_pages(unsigned char *pages, size_t count) {
    PP_CHECK_EQUAL(munmap(pages, count * pp_test_page_size()), 0);
}

// Runs a function on a new thread and waits for it.
static inline void pp_run_on_thread(void *(*function)(void *), void *argument) {
    pp_join_thread(pp_start_thread(function, argument));
}

// Raises one of the six signals on the calling thread: a fault of an instruction for SIGSEGV
// (an address with no mapping), SIGBUS (a page that cannot be read), SIGILL and SIGTRAP, and a
// signal that the thread sends itself for SIGFPE and SIGABRT, which arm64 raises for no
// instruction. It returns once a handler has resolved the signal. *address receives the
// address that the fault reports, or 0.
static inline void pp_raise_signal(int signal_number, uint64_t *address) {
    *address = 0U;
    switch (signal_number) {
    case SIGSEGV: {
        // A page between two others that is unmapped again: nothing maps there meanwhile.
        unsigned char *pages = pp_map_pages(3U);
        unsigned char *hole = pages + pp_test_page_size();
        PP_CHECK_EQUAL(munmap(hole, pp_test_page_size()), 0);
        *address = (uint64_t)(uintptr_t)hole;
        pp_signal_plant_read(hole);
        PP_CHECK_EQUAL(munmap(pages, pp_test_page_size()), 0);
        PP_CHECK_EQUAL(munmap(hole + pp_test_page_size(), pp_test_page_size()), 0);
        break;
    }
    case SIGBUS: {
        unsigned char *page = pp_map_pages(1U);
        *address = (uint64_t)(uintptr_t)page;
        pp_signal_plant_read(page);
        pp_unmap_pages(page, 1U);
        break;
    }
    case SIGILL:
        pp_signal_plant_illegal();
        break;
    case SIGTRAP:
        pp_signal_plant_breakpoint();
        break;
    default:
        PP_CHECK_EQUAL(pthread_kill(pthread_self(), signal_number), 0);
        break;
    }
}

// Takes stack in steps of 1 KiB until the calling thread's stack runs out, as a runaway
// recursion does. A loop of alloca needs no recursion, and the sanitizers leave this function
// alone, so the fault comes from its own store just below the stack. It returns only if
// *going_on turns false.
__attribute__((noinline, no_sanitize("address", "thread"))) static inline void
pp_exhaust_stack(const volatile bool *going_on) {
    while (*going_on) {
        volatile unsigned char *step = __builtin_alloca(1024U);
        step[0] = 1U;
    }
}

// The crash record of a folder, read at the offsets of docs/design/native-host.md section 8.
typedef struct pp_test_record_t {
    int32_t signal;
    int32_t code;
    uint32_t thread_role;
    uint64_t fault_address;
    uint64_t program_counter;
    char message[64];
} pp_test_record_t;

static inline uint64_t pp_read_le(const unsigned char *bytes, size_t offset, size_t size) {
    uint64_t value = 0U;
    for (size_t index = 0U; index < size; index++) {
        value |= (uint64_t)bytes[offset + index] << (8U * index);
    }
    return value;
}

static inline void pp_read_crash_record(const char *folder, pp_test_record_t *record) {
    char path[1024];
    PP_CHECK(snprintf(path, sizeof path, "%s/crash-record.bin", folder) > 0);
    FILE *file = fopen(path, "rb");
    PP_CHECK(file != NULL);
    unsigned char bytes[128];
    PP_CHECK_EQUAL(fread(bytes, 1U, sizeof bytes, file), sizeof bytes);
    PP_CHECK_EQUAL(fclose(file), 0);
    PP_CHECK(memcmp(bytes, "PPCR", 4U) == 0);
    record->signal = (int32_t)pp_read_le(bytes, 20U, 4U);
    record->code = (int32_t)pp_read_le(bytes, 24U, 4U);
    record->thread_role = (uint32_t)pp_read_le(bytes, 28U, 4U);
    record->fault_address = pp_read_le(bytes, 32U, 8U);
    record->program_counter = pp_read_le(bytes, 40U, 8U);
    memcpy(record->message, bytes + 64, sizeof record->message);
    record->message[sizeof record->message - 1U] = '\0';
}

// Whether a folder holds a crash record.
static inline bool pp_has_crash_record(const char *folder) {
    char path[1024];
    PP_CHECK(snprintf(path, sizeof path, "%s/crash-record.bin", folder) > 0);
    return access(path, F_OK) == 0;
}

static inline void pp_remove_record_folder(const char *folder) {
    pp_remove_file(folder, "crash-record.bin");
    pp_remove_file(folder, "crash-record.tmp");
    PP_CHECK_EQUAL(rmdir(folder), 0);
}

// Runs "<program> child <case> <folder>" and returns its wait status.
static inline int pp_run_child(const char *program, const char *child_case, const char *folder) {
    char *arguments[] = {
        (char *)program, (char *)"child", (char *)child_case, (char *)folder, NULL,
    };
    pid_t child = 0;
    PP_CHECK_EQUAL(posix_spawn(&child, program, NULL, NULL, arguments, PP_ENVIRONMENT), 0);
    int status = 0;
    PP_CHECK_EQUAL(waitpid(child, &status, 0), child);
    return status;
}

// Checks that a child ended by the given signal.
static inline void pp_check_ended_by(int status, int signal_number) {
    PP_CHECK(WIFSIGNALED(status));
    PP_CHECK_EQUAL(WTERMSIG(status), signal_number);
}

// A child writes the address it is about to fault at into its folder, for its parent to compare
// with the record.
static inline void pp_write_planted_value(const char *folder, uint64_t address) {
    char path[1024];
    PP_CHECK(snprintf(path, sizeof path, "%s/planted.txt", folder) > 0);
    FILE *file = fopen(path, "w");
    PP_CHECK(file != NULL);
    PP_CHECK(fprintf(file, "%llu\n", (unsigned long long)address) > 0);
    PP_CHECK_EQUAL(fclose(file), 0);
}

static inline void pp_write_planted(const char *folder, const volatile void *address) {
    pp_write_planted_value(folder, (uint64_t)(uintptr_t)address);
}

// The pointer to an address that a test names by its number, such as one in the first page.
static inline const volatile void *pp_test_address(uint64_t address) {
    const volatile void *pointer = NULL;
    const uintptr_t value = (uintptr_t)address;
    memcpy((void *)&pointer, &value, sizeof pointer);
    return pointer;
}

static inline uint64_t pp_read_planted(const char *folder) {
    char path[1024];
    PP_CHECK(snprintf(path, sizeof path, "%s/planted.txt", folder) > 0);
    FILE *file = fopen(path, "r");
    PP_CHECK(file != NULL);
    char line[64] = {0};
    PP_CHECK(fgets(line, (int)sizeof line, file) != NULL);
    PP_CHECK_EQUAL(fclose(file), 0);
    pp_remove_file(folder, "planted.txt");
    unsigned long long address = 0U;
    PP_CHECK(pp_read_number(line, "", &address, NULL));
    return (uint64_t)address;
}

// How far below a thread's stack a test accepts the fault of an overflow; the dispatcher's
// own reach is the same (signal_handler.c).
#define PP_TEST_OVERFLOW_REACH ((uint64_t)1024U * 1024U)

// How a child case must end, and the record it must leave. The child writes the address it
// faults at with pp_write_planted, 0 when the test cannot know it, or the lowest address of
// its stack before it overflows the stack.
typedef struct pp_child_end_t {
    int ended_by;        // The signal that ends the child, or 0 for the record's own signal.
    int signal;          // The record's signal, or 0 for SIGSEGV or SIGBUS, whichever came.
    uint32_t role;       // A PP_THREAD_ROLE_ value.
    const char *message; // The record's message.
    bool below_stack;    // Whether the planted address is the stack's, which the fault lies below.
    bool any_code;       // Whether the code goes unchecked: Darwin gives a breakpoint code 0.
} pp_child_end_t;

// Runs a child case in a folder of its own and checks how it ended and the record it left.
static inline void pp_check_child_end(const char *program, const char *child_case,
                                      const pp_child_end_t *expected) {
    char folder[64];
    pp_make_folder(folder, sizeof folder);
    const int status = pp_run_child(program, child_case, folder);
    PP_CHECK(WIFSIGNALED(status));
    const uint64_t planted = pp_read_planted(folder);
    pp_test_record_t record;
    pp_read_crash_record(folder, &record);
    if (expected->signal != 0) {
        PP_CHECK_EQUAL(record.signal, expected->signal);
    } else {
        PP_CHECK(record.signal == SIGSEGV || record.signal == SIGBUS);
    }
    pp_check_ended_by(status, expected->ended_by != 0 ? expected->ended_by : record.signal);
    if (!expected->any_code) {
        PP_CHECK(record.code > 0);
    }
    PP_CHECK_EQUAL(record.thread_role, expected->role);
    if (expected->below_stack) {
        PP_CHECK(record.fault_address < planted);
        PP_CHECK(planted - record.fault_address <= PP_TEST_OVERFLOW_REACH);
    } else if (planted != 0U) {
        PP_CHECK_EQUAL(record.fault_address, planted);
    }
    PP_CHECK(record.program_counter != 0U);
    PP_CHECK_TEXT(record.message, expected->message);
    pp_remove_record_folder(folder);
}

// The messages of the dispatcher's crash records (signal_handler.c).
#define PP_TEST_MESSAGE_DEFAULT "A fault outside the guest path that nothing handled."
#define PP_TEST_MESSAGE_LAST_RESORT "A fault that the runtime did not handle."
#define PP_TEST_MESSAGE_CRASH_PATH "A fault that ended in the runtime's crash path."
#define PP_TEST_MESSAGE_OVERFLOW "A stack overflow outside the guest path."
#define PP_TEST_MESSAGE_DECLINED "The guest fault handler declined a fault."

// How long a child that might loop for ever runs before SIGALRM ends it, so that a test of a
// fault that must end the app fails quickly instead of waiting for the CTest timeout.
#define PP_TEST_CHILD_ALARM_SECONDS 20U

// A child case: it receives the folder that its parent made for its crash record.
typedef struct pp_child_case_t {
    const char *name;
    void (*run)(const char *folder);
} pp_child_case_t;

// The main function of a signal test program: a child case when argv says so, else one of
// the usual cases.
static inline int pp_signal_test_main(int argc, char **argv, const pp_test_case_t *cases,
                                      size_t count, const pp_child_case_t *children,
                                      size_t child_count) {
    if (argc == 4 && strcmp(argv[1], "child") == 0) {
        for (size_t index = 0U; index < child_count; index++) {
            if (strcmp(argv[2], children[index].name) == 0) {
                children[index].run(argv[3]);
                return EXIT_SUCCESS;
            }
        }
        return 2;
    }
    return pp_run_case(argc, argv, cases, count);
}

#endif // PANEPORT_TESTS_SIGNAL_TEST_SUPPORT_H
