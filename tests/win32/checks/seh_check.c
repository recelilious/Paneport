// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// This file alone is built with -fms-extensions, with which clang-tidy's
// readability-duplicate-include reports includes that are not repeated.
// NOLINTBEGIN(readability-duplicate-include)
#include "seh_check.h"

#include <stdint.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
// NOLINTEND(readability-duplicate-include)

// An address in the first page of the address space, which Windows never maps, and neither
// does Wine on macOS, where the first 4 GB are the executable's page zero.
#define PP_SEH_FAULT_ADDRESS ((ULONG_PTR)0x10U)

// The rounds of the race check, which stops at the first wrong division: at least the first
// number, then on until both outcomes have shown, up to the second, because the thread that flips
// the divisor sometimes does not run while the first rounds divide. And how long the check waits
// for that thread to end.
#define PP_SEH_RACE_ROUNDS 20000U
#define PP_SEH_RACE_MAX_ROUNDS 1000000U
#define PP_SEH_WAIT_MS 30000U

// EXCEPTION_SOFTWARE_ORIGINATE, which Windows 11 adds to the flags of every exception that
// RaiseException raises, and which Wine leaves out. mingw-w64's headers do not name it.
#define PP_SEH_SOFTWARE_ORIGINATE 0x80U

// What the vectored handler of pp_seh_extended reports. It reaches the handler as the first
// parameter of the exceptions raised for it, so that no global state is needed.
struct vectored_report {
    bool seen;
    bool continued;
};

// What a filter saw of one exception. report, when it is set, is the vectored handler's report,
// which the filter reads to learn whether that handler ran first.
struct seh_record {
    const struct vectored_report *report;
    bool seen;
    bool vectored_seen_first;
    DWORD code;
    DWORD flags;
    DWORD parameters;
    ULONG_PTR information[2];
};

static int record_exception(struct seh_record *record, const EXCEPTION_POINTERS *pointers,
                            DWORD expected) {
    const EXCEPTION_RECORD *exception = pointers->ExceptionRecord;
    if (exception->ExceptionCode != expected) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    record->seen = true;
    record->code = exception->ExceptionCode;
    record->flags = exception->ExceptionFlags;
    record->parameters = exception->NumberParameters;
    for (DWORD index = 0; index < 2U && index < exception->NumberParameters; index++) {
        record->information[index] = exception->ExceptionInformation[index];
    }
    if (record->report != NULL) {
        record->vectored_seen_first = record->report->seen;
    }
    return EXCEPTION_EXECUTE_HANDLER;
}

__attribute__((noinline)) static void write_to_fault_address(void) {
    volatile int *target =
        (volatile int *)PP_SEH_FAULT_ADDRESS; // NOLINT(performance-no-int-to-ptr)
    *target = 1; // NOLINT(clang-analyzer-core.NullDereference): the fault is the point.
}

// IDIV with a zero divisor raises the divide error, which Windows reports as
// EXCEPTION_INT_DIVIDE_BY_ZERO. Inline assembly makes sure that the instruction is there, since
// a division by zero in C is undefined behavior that a compiler may assume away.
__attribute__((noinline)) static int32_t divide(int32_t dividend, int32_t divisor) {
    int32_t quotient = dividend;
    int32_t remainder = 0;
    // NOLINTNEXTLINE(portability-no-assembler): the test needs the IDIV instruction itself.
    __asm__ volatile("cltd\n\tidivl %2" : "+a"(quotient), "=&d"(remainder) : "r"(divisor) : "cc");
    (void)remainder;
    return quotient;
}

// The division of the race check: 100 divided by the divisor in memory, which the instruction
// reads itself, once, as x86 does. The quotient and remainder come back through result.
struct division {
    ULONG_PTR quotient;
    ULONG_PTR remainder;
};

__attribute__((noinline)) static void divide_by_memory(const volatile ULONG_PTR *divisor,
                                                       struct division *result) {
    ULONG_PTR quotient = 100U;
    ULONG_PTR high = 0U;
#ifdef __x86_64__
    // NOLINTNEXTLINE(portability-no-assembler): the divisor must be the instruction's operand.
    __asm__ volatile("divq %2" : "+a"(quotient), "+d"(high) : "m"(*divisor) : "cc");
#else
    // NOLINTNEXTLINE(portability-no-assembler): the divisor must be the instruction's operand.
    __asm__ volatile("divl %2" : "+a"(quotient), "+d"(high) : "m"(*divisor) : "cc");
#endif
    result->quotient = quotient;
    result->remainder = high;
}

__attribute__((noinline)) static void raise_with_parameters(DWORD code, ULONG_PTR first) {
    ULONG_PTR parameters[2] = {first, 0x5678U};
    RaiseException(code, 0, 2, parameters);
}

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wlanguage-extension-token"

static bool catch_access_violation(struct seh_record *record) {
    bool handled = false;
    __try {
        write_to_fault_address();
    } __except (record_exception(record, (EXCEPTION_POINTERS *)_exception_info(),
                                 EXCEPTION_ACCESS_VIOLATION)) {
        handled = true;
    }
    return handled;
}

static bool catch_divide_by_zero(struct seh_record *record, int32_t divisor) {
    bool handled = false;
    volatile int32_t quotient = 0;
    __try {
        quotient = divide(7, divisor);
    } __except (record_exception(record, (EXCEPTION_POINTERS *)_exception_info(),
                                 EXCEPTION_INT_DIVIDE_BY_ZERO)) {
        handled = true;
    }
    (void)quotient;
    return handled;
}

// Returns whether the division raised the divide error; otherwise result holds its outcome.
static bool divide_or_catch(const volatile ULONG_PTR *divisor, struct division *result) {
    bool raised = false;
    __try {
        divide_by_memory(divisor, result);
    } __except (_exception_code() == EXCEPTION_INT_DIVIDE_BY_ZERO ? EXCEPTION_EXECUTE_HANDLER
                                                                  : EXCEPTION_CONTINUE_SEARCH) {
        raised = true;
    }
    return raised;
}

static bool catch_raised(struct seh_record *record, ULONG_PTR first) {
    bool handled = false;
    __try {
        raise_with_parameters(PP_SEH_RAISED_CODE, first);
    } __except (
        record_exception(record, (EXCEPTION_POINTERS *)_exception_info(), PP_SEH_RAISED_CODE)) {
        handled = true;
    }
    return handled;
}

// What unwind_through_finally saw: whether the outer handler ran, and whether the __finally
// block ran and saw an abnormal termination.
struct unwind_record {
    bool handled;
    bool finally_ran;
    bool finally_abnormal;
};

// The exception passes through a __finally block on its way to the outer handler, which must
// run the block during the unwind, as an abnormal termination.
static void unwind_through_finally(struct seh_record *record, struct unwind_record *unwind) {
    volatile bool finally_ran = false;
    volatile bool finally_abnormal = false;
    __try {
        __try {
            raise_with_parameters(PP_SEH_RAISED_CODE, 0U);
        } __finally {
            finally_ran = true;
            finally_abnormal = _abnormal_termination() != 0;
        }
    } __except (
        record_exception(record, (EXCEPTION_POINTERS *)_exception_info(), PP_SEH_RAISED_CODE)) {
        unwind->handled = true;
    }
    unwind->finally_ran = finally_ran;
    unwind->finally_abnormal = finally_abnormal;
}

#pragma clang diagnostic pop

// Registered only while pp_seh_extended raises its exceptions, each of which carries a
// vectored_report as its first parameter.
static LONG CALLBACK vectored_handler(EXCEPTION_POINTERS *pointers) {
    const EXCEPTION_RECORD *exception = pointers->ExceptionRecord;
    DWORD code = exception->ExceptionCode;
    if (code != PP_SEH_RAISED_CODE && code != PP_SEH_CONTINUED_CODE) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    if (exception->NumberParameters < 1U || exception->ExceptionInformation[0] == 0U) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    // NOLINTNEXTLINE(performance-no-int-to-ptr): the parameter is the report's address.
    struct vectored_report *report = (struct vectored_report *)exception->ExceptionInformation[0];
    report->seen = true;
    if (code == PP_SEH_CONTINUED_CODE) {
        report->continued = true;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

bool pp_seh_basic(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;

    struct seh_record violation;
    memset(&violation, 0, sizeof violation);
    pp_test_check(run, catch_access_violation(&violation),
                  "a write to address 0x10 raises an access violation");
    pp_test_check(run, violation.parameters >= 2U, "the access violation has two parameters");
    pp_test_check(run, violation.information[0] == 1U, "the access violation names a write");
    pp_test_check(run, violation.information[1] == PP_SEH_FAULT_ADDRESS,
                  "the access violation names address 0x10");

    struct seh_record division;
    memset(&division, 0, sizeof division);
    pp_test_check(run, catch_divide_by_zero(&division, 0),
                  "an integer division by zero raises EXCEPTION_INT_DIVIDE_BY_ZERO");

    struct seh_record raised;
    memset(&raised, 0, sizeof raised);
    pp_test_check(run, catch_raised(&raised, 0x1234U),
                  "RaiseException reaches the handler of its __try block");
    // RaiseException got no flags, so the exception is continuable. Windows 11 also sets
    // PP_SEH_SOFTWARE_ORIGINATE, which says no more than that software raised it.
    pp_test_check(run, (raised.flags & ~(DWORD)PP_SEH_SOFTWARE_ORIGINATE) == 0U,
                  "the raised exception is continuable");
    pp_test_check(run, raised.parameters == 2U, "the raised exception keeps two parameters");
    pp_test_check(run, raised.information[0] == 0x1234U,
                  "the raised exception keeps its first parameter");
    pp_test_check(run, raised.information[1] == 0x5678U,
                  "the raised exception keeps its second parameter");

    bool passed = run->failures == failures;
    pp_test_line(run, "%s: access violation, divide by zero and RaiseException caught: %s", area,
                 pp_test_verdict(passed));
    return passed;
}

static bool check_finally(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    struct seh_record unwound;
    memset(&unwound, 0, sizeof unwound);
    struct unwind_record unwind = {false, false, false};
    unwind_through_finally(&unwound, &unwind);
    pp_test_check(run, unwind.handled, "the exception reaches the outer handler");
    pp_test_check(run, unwind.finally_ran, "a __finally block runs while an exception unwinds");
    pp_test_check(run, unwind.finally_abnormal, "the __finally block sees an abnormal end");
    bool passed = run->failures == failures;
    pp_test_line(run, "%s: a __finally block ran during the unwind: %s", area,
                 pp_test_verdict(passed));
    return passed;
}

static bool check_vectored(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    struct vectored_report report = {false, false};
    PVOID handle = AddVectoredExceptionHandler(1, vectored_handler);
    if (pp_test_check(run, handle != NULL, "AddVectoredExceptionHandler works")) {
        struct seh_record ordered;
        memset(&ordered, 0, sizeof ordered);
        ordered.report = &report;
        pp_test_check(run, catch_raised(&ordered, (ULONG_PTR)&report),
                      "a raised exception reaches the frame handler");
        pp_test_check(run, ordered.vectored_seen_first,
                      "the vectored handler runs before the frame handler");

        report.seen = false;
        raise_with_parameters(PP_SEH_CONTINUED_CODE, (ULONG_PTR)&report);
        pp_test_check(run, report.seen, "the vectored handler sees the second exception");
        pp_test_check(run, report.continued,
                      "a vectored handler continues execution after RaiseException");
        pp_test_check(run, RemoveVectoredExceptionHandler(handle) != 0U,
                      "RemoveVectoredExceptionHandler works");
    }
    bool passed = run->failures == failures;
    pp_test_line(run, "%s: vectored handlers ran first and continued execution: %s", area,
                 pp_test_verdict(passed));
    return passed;
}

// The divisor that a second thread flips between 0 and 1 while the race check divides by it, and
// whether that thread has started and should stop.
struct divisor_race {
    volatile ULONG_PTR divisor;
    volatile LONG started;
    volatile LONG stop;
};

static DWORD WINAPI flip_divisor(void *parameter) {
    struct divisor_race *race = parameter;
    InterlockedExchange(&race->started, 1);
    while (race->stop == 0) {
        race->divisor = 0U;
        race->divisor = 1U;
    }
    return 0;
}

// One thread flips a divisor in memory between 0 and 1 while this one divides 100 by it. x86
// reads the divisor once, so each division either raises the divide error or gives 100 with a
// remainder of 0; an emulator that tests one read of the divisor and divides by another can give
// a quotient of 0 instead, or raise the error for a divisor of 1.
static bool check_divisor_race(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    struct divisor_race race = {1U, 0, 0};
    HANDLE thread = CreateThread(NULL, 0, flip_divisor, &race, 0, NULL);
    if (!pp_test_check(run, thread != NULL,
                       "CreateThread starts the thread that flips the divisor")) {
        return false;
    }
    // Wine starts a thread more slowly than the divisions run, so they wait for it to flip.
    for (unsigned waited = 0; race.started == 0 && waited < PP_SEH_WAIT_MS; waited++) {
        Sleep(1);
    }
    pp_test_check(run, race.started != 0, "the thread that flips the divisor runs");
    unsigned raised = 0;
    unsigned divided = 0;
    unsigned wrong = 0;
    for (unsigned round = 0; round < PP_SEH_RACE_MAX_ROUNDS && wrong == 0U; round++) {
        if (round >= PP_SEH_RACE_ROUNDS && raised > 0U && divided > 0U) {
            break;
        }
        struct division result = {0U, 0U};
        if (divide_or_catch(&race.divisor, &result)) {
            raised++;
        } else if (result.quotient == 100U && result.remainder == 0U) {
            divided++;
        } else {
            wrong++;
        }
    }
    InterlockedExchange(&race.stop, 1);
    pp_test_check(run, WaitForSingleObject(thread, PP_SEH_WAIT_MS) == WAIT_OBJECT_0,
                  "the thread that flips the divisor ends");
    CloseHandle(thread);
    pp_test_check(run, wrong == 0U,
                  "each division by a changing divisor raises the divide error or divides by 1");
    pp_test_check(run, raised > 0U, "some divisions found the divisor 0");
    pp_test_check(run, divided > 0U, "some divisions found the divisor 1");
    bool passed = run->failures == failures;
    pp_test_line(run, "%s: a division reads its divisor once while another thread changes it: %s",
                 area, pp_test_verdict(passed));
    return passed;
}

bool pp_seh_extended(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    check_finally(run, area);
    check_vectored(run, area);
    check_divisor_race(run, area);
    return run->failures == failures;
}
