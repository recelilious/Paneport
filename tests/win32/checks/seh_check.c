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
    pp_test_check(run, raised.flags == 0U, "the raised exception keeps its flags");
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

bool pp_seh_extended(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    check_finally(run, area);
    check_vectored(run, area);
    return run->failures == failures;
}
