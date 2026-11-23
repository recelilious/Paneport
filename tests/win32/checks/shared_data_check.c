// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "shared_data_check.h"

#include <stdint.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifdef __x86_64__

// The fixed address of the page, and the fields that the checks read and write, at their
// offsets in KUSER_SHARED_DATA of Wine's include/ddk/wdm.h. The assembly below takes them as
// constant operands, which %c prints as an absolute address.
#define PP_SD_PAGE 0x7ffe0000U
#define PP_SD_TICK_MULTIPLIER 0x7ffe0004U     // ULONG TickCountMultiplier
#define PP_SD_INTERRUPT_TIME 0x7ffe0008U      // KSYSTEM_TIME InterruptTime: LowPart,
#define PP_SD_INTERRUPT_HIGH1 0x7ffe000cU     // High1Time
#define PP_SD_INTERRUPT_HIGH2 0x7ffe0010U     // and High2Time
#define PP_SD_SYSTEM_TIME 0x7ffe0014U         // KSYSTEM_TIME SystemTime
#define PP_SD_BUILD_NUMBER 0x7ffe0260U        // ULONG NtBuildNumber, on a 16-byte boundary
#define PP_SD_PRODUCT_TYPE 0x7ffe0264U        // NT_PRODUCT_TYPE NtProductType
#define PP_SD_PRODUCT_TYPE_VALID 0x7ffe0268U  // BOOLEAN ProductTypeIsValid
#define PP_SD_MAJOR_VERSION 0x7ffe026cU       // ULONG NtMajorVersion
#define PP_SD_SUITE_MASK 0x7ffe02d0U          // ULONG SuiteMask
#define PP_SD_DEBUGGER_ENABLED 0x7ffe02d4U    // BOOLEAN KdDebuggerEnabled and the next 3 bytes
#define PP_SD_ACTIVE_CONSOLE 0x7ffe02d8U      // ULONG ActiveConsoleId
#define PP_SD_DISMOUNT_COUNT 0x7ffe02dcU      // ULONG DismountCount
#define PP_SD_COM_PLUS_PACKAGE 0x7ffe02e0U    // ULONG ComPlusPackage, on a 16-byte boundary
#define PP_SD_TICK_COUNT 0x7ffe0320U          // KSYSTEM_TIME TickCount
#define PP_SD_INTERRUPT_TIME_BIAS 0x7ffe03b0U // ULONGLONG InterruptTimeBias

// The slack of the clock comparisons, in the 100 ns units of the page's times and in
// milliseconds. Windows updates the page's clocks at every timer tick, and Wine's wineserver
// every 16 ms, so a reading lags the clock that an API reads by about that much; the slack
// leaves room for a busy machine, and still tells a live clock from a stale or wrong field.
#define PP_SD_TIME_SLACK (5ULL * 10000000ULL)
#define PP_SD_TICK_SLACK_MS 5000ULL

// NtDelayExecution waits 20 ms, a relative interval in 100 ns units, and must take between 10 ms
// and 10 s by the performance counter.
#define PP_SD_DELAY_INTERVAL (-200000LL)
#define PP_SD_DELAY_MIN_MS 10LL
#define PP_SD_DELAY_MAX_MS 10000LL

// How many times a three-part reading of a time is tried before the check gives up.
#define PP_SD_TIME_TRIES 1000U

// ntdll's syscall exports that the checks call. On x86-64, ntdll exports each syscall through a
// thunk of the form that Windows uses, which tests the SystemCall field of the page at
// 0x7ffe0308 before its syscall instruction; Wine's ARM64X ntdll keeps that form for x86-64
// callers (include/wine/asm.h).
DECLSPEC_IMPORT LONG NTAPI NtQueryPerformanceCounter(LARGE_INTEGER *counter,
                                                     LARGE_INTEGER *frequency);
DECLSPEC_IMPORT LONG NTAPI NtQuerySystemTime(LARGE_INTEGER *time);
DECLSPEC_IMPORT LONG NTAPI NtDelayExecution(BOOLEAN alertable, const LARGE_INTEGER *interval);

typedef LONG(NTAPI *query_performance_counter_function)(LARGE_INTEGER *counter,
                                                        LARGE_INTEGER *frequency);
typedef LONG(NTAPI *query_system_time_function)(LARGE_INTEGER *time);
typedef LONG(NTAPI *delay_execution_function)(BOOLEAN alertable, const LARGE_INTEGER *interval);
typedef LONG(NTAPI *get_version_function)(OSVERSIONINFOEXW *information);

// The three syscall exports, as the program reaches them.
struct syscall_exports {
    query_performance_counter_function query_performance_counter;
    query_system_time_function query_system_time;
    delay_execution_function delay_execution;
};

// A function pointer of no particular type: a cast through it is the conversion that clang's
// function-cast warnings leave alone.
typedef void (*any_function)(void);

// The address of an export of ntdll, which every process has loaded.
static any_function ntdll_export(const char *name) {
    HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    if (ntdll == NULL) {
        return NULL;
    }
    return (any_function)GetProcAddress(ntdll, name);
}

static uint64_t file_time_value(const FILETIME *time) {
    return ((uint64_t)time->dwHighDateTime << 32U) | time->dwLowDateTime;
}

// Whether value lies between low and high, give or take slack, in one function so that no
// caller needs a compound condition.
static bool within(uint64_t value, uint64_t low, uint64_t high, uint64_t slack) {
    if (value + slack < low) {
        return false;
    }
    return value <= high + slack;
}

static void check_syscalls(struct pp_test_run *run, const struct syscall_exports *exports) {
    LARGE_INTEGER api_frequency;
    LARGE_INTEGER before;
    LARGE_INTEGER counter;
    LARGE_INTEGER frequency;
    LARGE_INTEGER after;
    memset(&counter, 0, sizeof counter);
    memset(&frequency, 0, sizeof frequency);
    pp_test_check(run, QueryPerformanceFrequency(&api_frequency) != FALSE,
                  "QueryPerformanceFrequency works");
    pp_test_check(run, QueryPerformanceCounter(&before) != FALSE, "QueryPerformanceCounter works");
    pp_test_check(run, exports->query_performance_counter(&counter, &frequency) == 0,
                  "NtQueryPerformanceCounter succeeds");
    pp_test_check(run, QueryPerformanceCounter(&after) != FALSE,
                  "QueryPerformanceCounter works again");
    pp_test_check(run, frequency.QuadPart == api_frequency.QuadPart,
                  "NtQueryPerformanceCounter gives the frequency of QueryPerformanceFrequency");
    pp_test_check(run, counter.QuadPart >= before.QuadPart,
                  "NtQueryPerformanceCounter's counter is not before the counter read before it");
    pp_test_check(run, counter.QuadPart <= after.QuadPart,
                  "NtQueryPerformanceCounter's counter is not after the counter read after it");

    FILETIME file_before;
    FILETIME file_after;
    LARGE_INTEGER time;
    memset(&time, 0, sizeof time);
    GetSystemTimeAsFileTime(&file_before);
    pp_test_check(run, exports->query_system_time(&time) == 0, "NtQuerySystemTime succeeds");
    GetSystemTimeAsFileTime(&file_after);
    pp_test_check(run,
                  within((uint64_t)time.QuadPart, file_time_value(&file_before),
                         file_time_value(&file_after), PP_SD_TIME_SLACK),
                  "NtQuerySystemTime agrees with GetSystemTimeAsFileTime");

    LARGE_INTEGER interval;
    interval.QuadPart = PP_SD_DELAY_INTERVAL;
    LARGE_INTEGER start;
    LARGE_INTEGER end;
    QueryPerformanceCounter(&start);
    pp_test_check(run, exports->delay_execution(FALSE, &interval) == 0,
                  "NtDelayExecution succeeds");
    QueryPerformanceCounter(&end);
    if (api_frequency.QuadPart > 0) {
        LONGLONG elapsed_ms = ((end.QuadPart - start.QuadPart) * 1000LL) / api_frequency.QuadPart;
        pp_test_check(run, elapsed_ms >= PP_SD_DELAY_MIN_MS,
                      "NtDelayExecution waits for at least 10 ms of a 20 ms interval");
        pp_test_check(run, elapsed_ms <= PP_SD_DELAY_MAX_MS,
                      "NtDelayExecution returns within 10 s");
    }
}

bool pp_shared_data_imported_syscalls(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    // The addresses that the loader wrote into the program's import table for ntdll.
    struct syscall_exports exports = {
        NtQueryPerformanceCounter,
        NtQuerySystemTime,
        NtDelayExecution,
    };
    check_syscalls(run, &exports);
    bool passed = run->failures == failures;
    pp_test_line(run,
                 "%s: imported syscall exports: NtQueryPerformanceCounter, NtQuerySystemTime and "
                 "NtDelayExecution: %s",
                 area, pp_test_verdict(passed));
    return passed;
}

bool pp_shared_data_syscalls_by_address(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    struct syscall_exports exports = {
        (query_performance_counter_function)ntdll_export("NtQueryPerformanceCounter"),
        (query_system_time_function)ntdll_export("NtQuerySystemTime"),
        (delay_execution_function)ntdll_export("NtDelayExecution"),
    };
    pp_test_check(run, exports.query_performance_counter != NULL,
                  "GetProcAddress finds NtQueryPerformanceCounter");
    pp_test_check(run, exports.query_system_time != NULL, "GetProcAddress finds NtQuerySystemTime");
    pp_test_check(run, exports.delay_execution != NULL, "GetProcAddress finds NtDelayExecution");
    if (exports.query_performance_counter != NULL && exports.query_system_time != NULL &&
        exports.delay_execution != NULL) {
        check_syscalls(run, &exports);
    }
    bool passed = run->failures == failures;
    pp_test_line(run,
                 "%s: syscall exports from GetProcAddress: NtQueryPerformanceCounter, "
                 "NtQuerySystemTime and NtDelayExecution: %s",
                 area, pp_test_verdict(passed));
    return passed;
}

// What a read form gives, from the fields of the version block.
enum read_expectation {
    EXPECT_MAJOR,           // NtMajorVersion
    EXPECT_MAJOR_LOW_WORD,  // the low 16 bits of NtMajorVersion
    EXPECT_MAJOR_AND_MINOR, // NtMajorVersion, and NtMinorVersion in the upper 32 bits
    EXPECT_PRODUCT_TYPE,    // NtProductType
    EXPECT_VALID,           // ProductTypeIsValid, which is 1
    EXPECT_TRUE,            // 1, for a comparison with the major version that held
};

// One instruction form that reads the page at its fixed address. The function returns what it
// read; it gets the major version that RtlGetVersion reports, for the comparison forms.
struct read_form {
    const char *name;
    uint64_t (*read)(uint32_t major);
    enum read_expectation expectation;
};

// NOLINTBEGIN(portability-no-assembler): each form is the instruction that its name gives.

// An address as a constant operand encodes as a 32-bit displacement with neither base nor index,
// the form of the syscall thunks' testb.
__attribute__((noinline)) static uint64_t read_absolute(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movl %c1, %0" : "=r"(value) : "i"(PP_SD_MAJOR_VERSION));
    return value;
}

__attribute__((noinline)) static uint64_t read_absolute_product_type(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movl %c1, %0" : "=r"(value) : "i"(PP_SD_PRODUCT_TYPE));
    return value;
}

// The moffs form of MOV, with a 64-bit address in the instruction.
__attribute__((noinline)) static uint64_t read_moffs(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movabsl %c1, %%eax" : "=a"(value) : "i"(PP_SD_MAJOR_VERSION));
    return value;
}

__attribute__((noinline)) static uint64_t read_register(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movl (%1), %0" : "=r"(value) : "r"((uintptr_t)PP_SD_MAJOR_VERSION));
    return value;
}

__attribute__((noinline)) static uint64_t read_register_displacement(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movl 0x6c(%1), %0" : "=r"(value) : "r"((uintptr_t)PP_SD_PAGE + 0x200U));
    return value;
}

// 0x7ffe0000 + 0x9a * 4 + 4 is 0x7ffe026c.
__attribute__((noinline)) static uint64_t read_base_index_scale(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movl 4(%1,%2,4), %0"
                     : "=r"(value)
                     : "r"((uintptr_t)PP_SD_PAGE), "r"((uintptr_t)0x9aU));
    return value;
}

__attribute__((noinline)) static uint64_t read_byte_zero_extended(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movzbl %c1, %0" : "=r"(value) : "i"(PP_SD_PRODUCT_TYPE_VALID));
    return value;
}

__attribute__((noinline)) static uint64_t read_word_zero_extended(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movzwl %c1, %0" : "=r"(value) : "i"(PP_SD_MAJOR_VERSION));
    return value;
}

__attribute__((noinline)) static uint64_t read_byte_sign_extended(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movsbl %c1, %0" : "=r"(value) : "i"(PP_SD_PRODUCT_TYPE_VALID));
    return value;
}

__attribute__((noinline)) static uint64_t read_doubleword_sign_extended(uint32_t major) {
    (void)major;
    uint64_t value = 0;
    __asm__ volatile("movslq %c1, %0" : "=r"(value) : "i"(PP_SD_MAJOR_VERSION));
    return value;
}

__attribute__((noinline)) static uint64_t read_quadword(uint32_t major) {
    (void)major;
    uint64_t value = 0;
    __asm__ volatile("movq %c1, %0" : "=r"(value) : "i"(PP_SD_MAJOR_VERSION));
    return value;
}

__attribute__((noinline)) static uint64_t read_add_source(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("xorl %0, %0\n\t"
                     "addl %c1, %0"
                     : "=&r"(value)
                     : "i"(PP_SD_MAJOR_VERSION)
                     : "cc");
    return value;
}

__attribute__((noinline)) static uint64_t read_compare(uint32_t major) {
    uint8_t equal = 0;
    __asm__ volatile("cmpl %1, %c2\n\t"
                     "sete %0"
                     : "=q"(equal)
                     : "r"(major), "i"(PP_SD_MAJOR_VERSION)
                     : "cc");
    return equal;
}

// testb $1, <address>, the instruction of the syscall thunks, on ProductTypeIsValid, which is
// 1, instead of SystemCall, which Windows and Wine set differently.
__attribute__((noinline)) static uint64_t read_test(uint32_t major) {
    (void)major;
    uint8_t set = 0;
    __asm__ volatile("testb $1, %c1\n\t"
                     "setne %0"
                     : "=q"(set)
                     : "i"(PP_SD_PRODUCT_TYPE_VALID)
                     : "cc");
    return set;
}

__attribute__((noinline)) static uint64_t read_sse_doubleword(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movd %c1, %%xmm0\n\t"
                     "movd %%xmm0, %0"
                     : "=r"(value)
                     : "i"(PP_SD_MAJOR_VERSION)
                     : "xmm0");
    return value;
}

__attribute__((noinline)) static uint64_t read_sse_quadword(uint32_t major) {
    (void)major;
    uint64_t value = 0;
    __asm__ volatile("movq %c1, %%xmm0\n\t"
                     "movq %%xmm0, %0"
                     : "=r"(value)
                     : "i"(PP_SD_MAJOR_VERSION)
                     : "xmm0");
    return value;
}

// 16 bytes from 0x7ffe0260, whose fourth doubleword is NtMajorVersion.
__attribute__((noinline)) static uint64_t read_sse_unaligned(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movdqu %c1, %%xmm0\n\t"
                     "pshufd $3, %%xmm0, %%xmm0\n\t"
                     "movd %%xmm0, %0"
                     : "=r"(value)
                     : "i"(PP_SD_BUILD_NUMBER)
                     : "xmm0");
    return value;
}

__attribute__((noinline)) static uint64_t read_sse_aligned(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    __asm__ volatile("movaps %c1, %%xmm0\n\t"
                     "pshufd $3, %%xmm0, %%xmm0\n\t"
                     "movd %%xmm0, %0"
                     : "=r"(value)
                     : "i"(PP_SD_BUILD_NUMBER)
                     : "xmm0");
    return value;
}

// 8 bytes from 0x7ffe0268 into the upper half of a register; NtMajorVersion is their upper half.
__attribute__((noinline)) static uint64_t read_sse_high_half(uint32_t major) {
    (void)major;
    uint64_t value = 0;
    __asm__ volatile("xorps %%xmm0, %%xmm0\n\t"
                     "movhps %c1, %%xmm0\n\t"
                     "movhlps %%xmm0, %%xmm0\n\t"
                     "movq %%xmm0, %0"
                     : "=r"(value)
                     : "i"(PP_SD_PRODUCT_TYPE_VALID)
                     : "xmm0");
    return value >> 32U;
}

// Windows x64 has no red zone, so the push may use the stack below RSP.
__attribute__((noinline)) static uint64_t read_push(uint32_t major) {
    (void)major;
    uint64_t value = 0;
    __asm__ volatile("pushq %c1\n\t"
                     "popq %0"
                     : "=r"(value)
                     : "i"(PP_SD_MAJOR_VERSION)
                     : "memory");
    return value;
}

// The direction flag is clear on entry, as the Windows x64 calling convention requires.
__attribute__((noinline)) static uint64_t read_string_load(uint32_t major) {
    (void)major;
    uint32_t value = 0;
    uintptr_t source = PP_SD_MAJOR_VERSION;
    __asm__ volatile("lodsl" : "=a"(value), "+S"(source) : : "memory");
    return value;
}

__attribute__((noinline)) static uint64_t read_string_copy(uint32_t major) {
    (void)major;
    uint32_t copy[4] = {0U, 0U, 0U, 0U};
    uintptr_t source = PP_SD_BUILD_NUMBER;
    uint32_t *destination = copy;
    uintptr_t count = sizeof copy;
    __asm__ volatile("rep movsb" : "+S"(source), "+D"(destination), "+c"(count) : : "memory");
    return copy[3];
}

__attribute__((noinline)) static uint64_t read_x87(uint32_t major) {
    (void)major;
    int32_t value = 0;
    __asm__ volatile("fildl %c1\n\t"
                     "fistpl %0"
                     : "=m"(value)
                     : "i"(PP_SD_MAJOR_VERSION));
    return (uint32_t)value;
}

// LEA computes the address and reads nothing, so it must keep the fixed address.
__attribute__((noinline)) static uint64_t address_of_major_version(void) {
    uint64_t address = 0;
    __asm__ volatile("leaq %c1, %0" : "=r"(address) : "i"(PP_SD_MAJOR_VERSION));
    return address;
}

// NOLINTEND(portability-no-assembler)

static const struct read_form k_read_forms[] = {
    {"a 32-bit displacement", read_absolute, EXPECT_MAJOR},
    {"a 32-bit displacement to NtProductType", read_absolute_product_type, EXPECT_PRODUCT_TYPE},
    {"a 64-bit moffs address", read_moffs, EXPECT_MAJOR},
    {"a base register", read_register, EXPECT_MAJOR},
    {"a base register and a displacement", read_register_displacement, EXPECT_MAJOR},
    {"a base, a scaled index and a displacement", read_base_index_scale, EXPECT_MAJOR},
    {"movzbl", read_byte_zero_extended, EXPECT_VALID},
    {"movzwl", read_word_zero_extended, EXPECT_MAJOR_LOW_WORD},
    {"movsbl", read_byte_sign_extended, EXPECT_VALID},
    {"movslq", read_doubleword_sign_extended, EXPECT_MAJOR},
    {"movq to a general register", read_quadword, EXPECT_MAJOR_AND_MINOR},
    {"addl from memory", read_add_source, EXPECT_MAJOR},
    {"cmpl with memory", read_compare, EXPECT_TRUE},
    {"testb $1, as the syscall thunks test the page", read_test, EXPECT_TRUE},
    {"movd to an SSE register", read_sse_doubleword, EXPECT_MAJOR},
    {"movq to an SSE register", read_sse_quadword, EXPECT_MAJOR_AND_MINOR},
    {"movdqu", read_sse_unaligned, EXPECT_MAJOR},
    {"movaps", read_sse_aligned, EXPECT_MAJOR},
    {"movhps", read_sse_high_half, EXPECT_MAJOR},
    {"pushq from memory", read_push, EXPECT_MAJOR_AND_MINOR},
    {"lodsl", read_string_load, EXPECT_MAJOR},
    {"rep movsb", read_string_copy, EXPECT_MAJOR},
    {"fildl", read_x87, EXPECT_MAJOR},
};

static uint64_t expected_value(enum read_expectation expectation, const OSVERSIONINFOEXW *version) {
    switch (expectation) {
    case EXPECT_MAJOR:
        return version->dwMajorVersion;
    case EXPECT_MAJOR_LOW_WORD:
        return version->dwMajorVersion & 0xffffU;
    case EXPECT_MAJOR_AND_MINOR:
        return ((uint64_t)version->dwMinorVersion << 32U) | version->dwMajorVersion;
    case EXPECT_PRODUCT_TYPE:
        return version->wProductType;
    case EXPECT_VALID:
    case EXPECT_TRUE:
        return 1U;
    default:
        return UINT64_MAX;
    }
}

bool pp_shared_data_version_fields(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    const size_t form_count = sizeof k_read_forms / sizeof k_read_forms[0];
    OSVERSIONINFOEXW version;
    memset(&version, 0, sizeof version);
    version.dwOSVersionInfoSize = sizeof version;
    get_version_function get_version = (get_version_function)ntdll_export("RtlGetVersion");
    pp_test_check(run, get_version != NULL, "GetProcAddress finds RtlGetVersion");
    LONG status = -1;
    if (get_version != NULL) {
        status = get_version(&version);
    }
    if (pp_test_check(run, status == 0, "RtlGetVersion succeeds")) {
        pp_test_check(run, version.dwMajorVersion != 0U, "RtlGetVersion gives a major version");
        for (size_t index = 0; index < form_count; index++) {
            const struct read_form *form = &k_read_forms[index];
            uint64_t value = form->read((uint32_t)version.dwMajorVersion);
            uint64_t expected = expected_value(form->expectation, &version);
            if (!pp_test_check(run, value == expected,
                               "a read of the page gives what RtlGetVersion reports")) {
                pp_test_line(run, "%s: the read through %s gave 0x%llx, not 0x%llx", area,
                             form->name, (unsigned long long)value, (unsigned long long)expected);
            }
        }
    }
    pp_test_check(run, address_of_major_version() == (uint64_t)PP_SD_MAJOR_VERSION,
                  "LEA of an address in the page gives that address");
    bool passed = run->failures == failures;
    pp_test_line(run,
                 "%s: version fields through %u instruction forms agree with RtlGetVersion: %s",
                 area, (unsigned)form_count, pp_test_verdict(passed));
    return passed;
}

// NOLINTBEGIN(portability-no-assembler): the clocks are read with the loads that they name.

// A KSYSTEM_TIME as Windows documents its reading: High1Time, LowPart, then High2Time, until
// both high parts agree. This one reads the interrupt time with constant addresses, as
// compilers emit for a field of the page. Returns 0 when no try agreed.
static uint64_t read_interrupt_time(void) {
    for (unsigned attempt = 0; attempt < PP_SD_TIME_TRIES; attempt++) {
        uint32_t high1 = 0;
        uint32_t low = 0;
        uint32_t high2 = 0;
        __asm__ volatile("movl %c3, %0\n\t"
                         "movl %c4, %1\n\t"
                         "movl %c5, %2"
                         : "=&r"(high1), "=&r"(low), "=r"(high2)
                         : "i"(PP_SD_INTERRUPT_HIGH1), "i"(PP_SD_INTERRUPT_TIME),
                           "i"(PP_SD_INTERRUPT_HIGH2));
        if (high1 == high2) {
            return ((uint64_t)high1 << 32U) | low;
        }
    }
    return 0;
}

// The same reading through a base register, for any KSYSTEM_TIME of the page.
static uint64_t read_time_at(uintptr_t address) {
    for (unsigned attempt = 0; attempt < PP_SD_TIME_TRIES; attempt++) {
        uint32_t high1 = 0;
        uint32_t low = 0;
        uint32_t high2 = 0;
        __asm__ volatile("movl 4(%3), %0\n\t"
                         "movl (%3), %1\n\t"
                         "movl 8(%3), %2"
                         : "=&r"(high1), "=&r"(low), "=&r"(high2)
                         : "r"(address));
        if (high1 == high2) {
            return ((uint64_t)high1 << 32U) | low;
        }
    }
    return 0;
}

// LowPart and High1Time of the interrupt time in one 64-bit load through a register, as Go's
// Windows runtime reads its clock.
static uint64_t read_interrupt_time_at_once(void) {
    uint64_t value = 0;
    __asm__ volatile("movq (%1), %0" : "=r"(value) : "r"((uintptr_t)PP_SD_INTERRUPT_TIME));
    return value;
}

static uint64_t read_interrupt_time_bias(void) {
    uint64_t value = 0;
    __asm__ volatile("movq %c1, %0" : "=r"(value) : "i"(PP_SD_INTERRUPT_TIME_BIAS));
    return value;
}

static uint32_t read_tick_multiplier(void) {
    uint32_t value = 0;
    __asm__ volatile("movl %c1, %0" : "=r"(value) : "i"(PP_SD_TICK_MULTIPLIER));
    return value;
}

// NOLINTEND(portability-no-assembler)

static void check_interrupt_time(struct pp_test_run *run) {
    uint64_t bias = read_interrupt_time_bias();
    uint64_t before = read_interrupt_time();
    uint64_t at_once_before = read_interrupt_time_at_once();
    ULONGLONG unbiased = 0;
    BOOL queried = QueryUnbiasedInterruptTime(&unbiased);
    uint64_t at_once_after = read_interrupt_time_at_once();
    uint64_t after = read_interrupt_time();
    pp_test_check(run, queried != FALSE, "QueryUnbiasedInterruptTime reads the interrupt time");
    pp_test_check(run, before != 0U, "the page's interrupt time reads consistently");
    pp_test_check(run, after >= before, "the page's interrupt time does not go back");
    pp_test_check(run, before >= bias, "the page's interrupt time is not below its bias");
    pp_test_check(run, within(unbiased, before - bias, after - bias, PP_SD_TIME_SLACK),
                  "QueryUnbiasedInterruptTime agrees with the page's interrupt time less its bias");
    pp_test_check(run, within(at_once_before, before, after, PP_SD_TIME_SLACK),
                  "a 64-bit load of the interrupt time agrees with the three-part reading");
    pp_test_check(run, within(at_once_after, before, after, PP_SD_TIME_SLACK),
                  "a second 64-bit load of the interrupt time agrees with the three-part reading");
}

static void check_system_time(struct pp_test_run *run) {
    FILETIME file_before;
    FILETIME file_after;
    GetSystemTimeAsFileTime(&file_before);
    uint64_t system_time = read_time_at(PP_SD_SYSTEM_TIME);
    GetSystemTimeAsFileTime(&file_after);
    pp_test_check(run, system_time != 0U, "the page's system time reads consistently");
    pp_test_check(run,
                  within(system_time, file_time_value(&file_before), file_time_value(&file_after),
                         PP_SD_TIME_SLACK),
                  "the page's system time agrees with GetSystemTimeAsFileTime");
}

// The tick count in milliseconds is TickCount times TickCountMultiplier, shifted right by 24
// bits; the product is split so that it cannot overflow.
static void check_tick_count(struct pp_test_run *run) {
    ULONGLONG ticks_before = GetTickCount64();
    uint64_t count = read_time_at(PP_SD_TICK_COUNT);
    uint32_t multiplier = read_tick_multiplier();
    ULONGLONG ticks_after = GetTickCount64();
    uint64_t milliseconds =
        ((count >> 24U) * multiplier) + (((count & 0xffffffU) * multiplier) >> 24U);
    pp_test_check(run, count != 0U, "the page's tick count reads consistently");
    pp_test_check(run, multiplier != 0U, "the page's tick count multiplier is set");
    pp_test_check(run, within(milliseconds, ticks_before, ticks_after, PP_SD_TICK_SLACK_MS),
                  "the page's tick count agrees with GetTickCount64");
}

bool pp_shared_data_clocks(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    check_interrupt_time(run);
    check_system_time(run);
    check_tick_count(run);
    bool passed = run->failures == failures;
    pp_test_line(run,
                 "%s: clocks: the interrupt time, system time and tick count agree with their "
                 "APIs: %s",
                 area, pp_test_verdict(passed));
    return passed;
}

// What the vectored handler saw of the access violation of an armed write. Only the main
// thread arms it, around one write at a time.
struct write_report {
    volatile LONG armed;
    volatile LONG seen;
    DWORD parameters;
    ULONG_PTR information[2];
};

static struct write_report g_write_report;

// Registered only while pp_shared_data_writes runs. Every write form puts the address of the
// instruction that follows its write in RDX, where execution continues.
static LONG CALLBACK write_handler(EXCEPTION_POINTERS *pointers) {
    const EXCEPTION_RECORD *exception = pointers->ExceptionRecord;
    if (g_write_report.armed == 0 || exception->ExceptionCode != EXCEPTION_ACCESS_VIOLATION) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    g_write_report.armed = 0;
    g_write_report.seen = 1;
    g_write_report.parameters = exception->NumberParameters;
    for (DWORD index = 0; index < 2U && index < exception->NumberParameters; index++) {
        g_write_report.information[index] = exception->ExceptionInformation[index];
    }
    pointers->ContextRecord->Rip = pointers->ContextRecord->Rdx;
    return EXCEPTION_CONTINUE_EXECUTION;
}

// One instruction form that writes to the page, and the address that it writes.
struct write_form {
    const char *name;
    void (*write)(void);
    uintptr_t address;
};

// NOLINTBEGIN(portability-no-assembler): each form is the write that its name gives.

__attribute__((noinline)) static void write_absolute(void) {
    __asm__ volatile("leaq 1f(%%rip), %%rdx\n\t"
                     "movl %%eax, %c1\n"
                     "1:"
                     :
                     : "a"(1U), "i"(PP_SD_SUITE_MASK)
                     : "rdx", "memory");
}

__attribute__((noinline)) static void write_register(void) {
    __asm__ volatile("leaq 1f(%%rip), %%rdx\n\t"
                     "movl %%eax, (%%rcx)\n"
                     "1:"
                     :
                     : "a"(1U), "c"((uintptr_t)PP_SD_DEBUGGER_ENABLED)
                     : "rdx", "memory");
}

__attribute__((noinline)) static void write_sse(void) {
    __asm__ volatile("leaq 1f(%%rip), %%rdx\n\t"
                     "pxor %%xmm0, %%xmm0\n\t"
                     "movdqu %%xmm0, %c0\n"
                     "1:"
                     :
                     : "i"(PP_SD_COM_PLUS_PACKAGE)
                     : "rdx", "xmm0", "memory");
}

__attribute__((noinline)) static void write_add(void) {
    __asm__ volatile("leaq 1f(%%rip), %%rdx\n\t"
                     "addl $1, %c0\n"
                     "1:"
                     :
                     : "i"(PP_SD_ACTIVE_CONSOLE)
                     : "rdx", "cc", "memory");
}

__attribute__((noinline)) static void write_locked_add(void) {
    __asm__ volatile("leaq 1f(%%rip), %%rdx\n\t"
                     "lock addl $1, %c0\n"
                     "1:"
                     :
                     : "i"(PP_SD_DISMOUNT_COUNT)
                     : "rdx", "cc", "memory");
}

__attribute__((noinline)) static void write_exchange(void) {
    uint32_t value = 1U;
    __asm__ volatile("leaq 1f(%%rip), %%rdx\n\t"
                     "xchgl %0, %c1\n"
                     "1:"
                     : "+a"(value)
                     : "i"(PP_SD_SUITE_MASK)
                     : "rdx", "memory");
}

static uint32_t read_doubleword_at(uintptr_t address) {
    uint32_t value = 0;
    __asm__ volatile("movl (%1), %0" : "=r"(value) : "r"(address));
    return value;
}

// NOLINTEND(portability-no-assembler)

static const struct write_form k_write_forms[] = {
    {"a store to a 32-bit displacement", write_absolute, PP_SD_SUITE_MASK},
    {"a store through a base register", write_register, PP_SD_DEBUGGER_ENABLED},
    {"a 16-byte SSE store", write_sse, PP_SD_COM_PLUS_PACKAGE},
    {"addl to memory", write_add, PP_SD_ACTIVE_CONSOLE},
    {"lock addl", write_locked_add, PP_SD_DISMOUNT_COUNT},
    {"xchgl with memory", write_exchange, PP_SD_SUITE_MASK},
};

static void check_write(struct pp_test_run *run, const char *area, const struct write_form *form) {
    unsigned failures = run->failures;
    uint32_t before = read_doubleword_at(form->address);
    memset(&g_write_report, 0, sizeof g_write_report);
    g_write_report.armed = 1;
    form->write();
    g_write_report.armed = 0;
    pp_test_check(run, g_write_report.seen != 0, "a write to the page raises an access violation");
    pp_test_check(run, g_write_report.parameters >= 2U, "the access violation has two parameters");
    pp_test_check(run, g_write_report.information[0] == EXCEPTION_WRITE_FAULT,
                  "the access violation names a write");
    pp_test_check(run, g_write_report.information[1] == form->address,
                  "the access violation names the written address");
    pp_test_check(run, read_doubleword_at(form->address) == before,
                  "the page keeps its value after the write");
    if (run->failures != failures) {
        pp_test_line(run, "%s: the write was %s, which reported 0x%llx at 0x%llx", area, form->name,
                     (unsigned long long)g_write_report.information[0],
                     (unsigned long long)g_write_report.information[1]);
    }
}

bool pp_shared_data_writes(struct pp_test_run *run, const char *area) {
    unsigned failures = run->failures;
    const size_t form_count = sizeof k_write_forms / sizeof k_write_forms[0];
    PVOID handle = AddVectoredExceptionHandler(1, write_handler);
    if (pp_test_check(run, handle != NULL, "AddVectoredExceptionHandler works")) {
        for (size_t index = 0; index < form_count; index++) {
            check_write(run, area, &k_write_forms[index]);
        }
        pp_test_check(run, RemoveVectoredExceptionHandler(handle) != 0U,
                      "RemoveVectoredExceptionHandler works");
    }
    bool passed = run->failures == failures;
    pp_test_line(run, "%s: writes through %u instruction forms raise write access violations: %s",
                 area, (unsigned)form_count, pp_test_verdict(passed));
    return passed;
}

#else

// The page is the same for i686 guests, but the thunks of the i386 ntdll have another form and
// FEX's WoW64 module is another translator, so stage M7.3 writes these checks for i686 together
// with the 32-bit layout of the address space.
static bool needs_m7_3(struct pp_test_run *run, const char *area) {
    pp_test_check(run, false, "the shared user data checks have no i686 version before M7.3");
    pp_test_line(run, "%s: the shared user data checks wait for M7.3: %s", area,
                 pp_test_verdict(false));
    return false;
}

bool pp_shared_data_imported_syscalls(struct pp_test_run *run, const char *area) {
    return needs_m7_3(run, area);
}

bool pp_shared_data_syscalls_by_address(struct pp_test_run *run, const char *area) {
    return needs_m7_3(run, area);
}

bool pp_shared_data_version_fields(struct pp_test_run *run, const char *area) {
    return needs_m7_3(run, area);
}

bool pp_shared_data_clocks(struct pp_test_run *run, const char *area) {
    return needs_m7_3(run, area);
}

bool pp_shared_data_writes(struct pp_test_run *run, const char *area) {
    return needs_m7_3(run, area);
}

#endif
