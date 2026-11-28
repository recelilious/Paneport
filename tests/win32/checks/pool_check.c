// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "pool_check.h"

#include <stdint.h>
#include <string.h>

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#ifdef __x86_64__

// The request that no region of the pool can hold, 1 GiB: a region is 128 MiB at most.
#define PP_POOL_TOO_LARGE ((SIZE_T)1024U * 1024U * 1024U)

// The size of the served requests, the allocation granularity.
#define PP_POOL_SMALL ((SIZE_T)64U * 1024U)

// How many functions pp_pool_generated_code writes, how many bytes each takes, and how often it
// calls every one of them.
#define PP_POOL_FUNCTIONS 40000U
#define PP_POOL_FUNCTION_SIZE 16U
#define PP_POOL_PASSES 2U

// The extended parameter of VirtualAlloc2 that carries attribute flags, and the attribute that
// asks for memory that holds ARM64EC code, as the Windows SDK documents them
// (MEM_EXTENDED_PARAMETER, MemExtendedParameterAttributeFlags, MEM_EXTENDED_PARAMETER_EC_CODE).
// The declarations are our own, so that the program builds with any version of the headers.
#define PP_POOL_PARAMETER_ATTRIBUTE_FLAGS 5U
#define PP_POOL_ATTRIBUTE_EC_CODE 0x40U

struct pool_extended_parameter {
    uint64_t type;
    uint64_t value;
};

typedef void *(WINAPI *virtual_alloc2_function)(HANDLE process, void *base, SIZE_T size, DWORD type,
                                                DWORD protection,
                                                struct pool_extended_parameter *parameters,
                                                ULONG count);

// A generated function: it takes one argument and returns a value.
typedef uint32_t (*generated_function)(uint32_t argument);

// Turns the address of generated code into a function pointer. ISO C has no conversion from an
// object pointer to a function pointer, so the address is copied.
static generated_function function_at(uint8_t *code) {
    generated_function function = NULL;
    memcpy(&function, &code, sizeof function);
    return function;
}

// Asks VirtualAlloc2 for memory that holds ARM64EC code. Returns NULL with the error in *error
// when the request fails, or when kernelbase has no VirtualAlloc2.
static void *allocate_ec_code(SIZE_T size, DWORD *error) {
    *error = ERROR_PROC_NOT_FOUND;
    HMODULE kernelbase = GetModuleHandleA("kernelbase.dll");
    if (kernelbase == NULL) {
        return NULL;
    }
    FARPROC address = GetProcAddress(kernelbase, "VirtualAlloc2");
    if (address == NULL) {
        return NULL;
    }
    virtual_alloc2_function virtual_alloc2 = NULL;
    memcpy(&virtual_alloc2, &address, sizeof virtual_alloc2);
    struct pool_extended_parameter parameter = {PP_POOL_PARAMETER_ATTRIBUTE_FLAGS,
                                                PP_POOL_ATTRIBUTE_EC_CODE};
    SetLastError(ERROR_SUCCESS);
    void *memory = virtual_alloc2(NULL, NULL, size, MEM_COMMIT | MEM_RESERVE,
                                  PAGE_EXECUTE_READWRITE, &parameter, 1U);
    *error = GetLastError();
    return memory;
}

// Whether every byte of a range is zero.
static bool is_zero(const uint8_t *bytes, size_t size) {
    for (size_t index = 0U; index < size; index++) {
        if (bytes[index] != 0U) {
            return false;
        }
    }
    return true;
}

// Writes "lea eax, [rcx + constant]; ret" at code, padded with int3 to PP_POOL_FUNCTION_SIZE
// bytes: a function that returns its argument plus the constant.
static void write_function(uint8_t *code, uint32_t constant) {
    memset(code, 0xCC, PP_POOL_FUNCTION_SIZE);
    code[0] = 0x8D;
    code[1] = 0x81;
    memcpy(code + 2, &constant, sizeof constant);
    code[6] = 0xC3;
}

// The constant of the function at index, which differs from function to function.
static uint32_t function_constant(uint32_t index) { return index * 2654435761U + 12345U; }

bool pp_pool_requests(struct pp_test_run *run, const char *area) {
    bool passed = true;
    DWORD error = ERROR_SUCCESS;

    // More ARM64EC code than any region of the pool holds: refused, with nothing mapped instead.
    void *too_large = allocate_ec_code(PP_POOL_TOO_LARGE, &error);
    passed =
        pp_test_check(run, too_large == NULL, "a request for 1 GiB of ARM64EC code memory fails") &&
        passed;
    passed = pp_test_check(run, too_large != NULL || error == ERROR_NOT_ENOUGH_MEMORY,
                           "the refused request reports ERROR_NOT_ENOUGH_MEMORY") &&
             passed;
    if (too_large != NULL) {
        (void)VirtualFree(too_large, 0U, MEM_RELEASE);
    }

    // A request that the pool can serve: zeroed, and freed again.
    uint8_t *served = allocate_ec_code(PP_POOL_SMALL, &error);
    passed = pp_test_check(run, served != NULL,
                           "a request for 64 KiB of ARM64EC code memory "
                           "succeeds") &&
             passed;
    if (served != NULL) {
        passed = pp_test_check(run, is_zero(served, PP_POOL_SMALL),
                               "ARM64EC code memory starts as zeroes") &&
                 passed;
        passed = pp_test_check(run, VirtualFree(served, 0U, MEM_RELEASE) != 0,
                               "ARM64EC code memory can be freed") &&
                 passed;
    }

    // Plain executable memory: readable and writable, and x86-64 code in it runs.
    uint8_t *plain =
        VirtualAlloc(NULL, PP_POOL_SMALL, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    passed =
        pp_test_check(run, plain != NULL, "VirtualAlloc gives PAGE_EXECUTE_READWRITE memory") &&
        passed;
    if (plain != NULL) {
        MEMORY_BASIC_INFORMATION information;
        memset(&information, 0, sizeof information);
        const SIZE_T queried = VirtualQuery(plain, &information, sizeof information);
        passed = pp_test_check(run, queried != 0U && information.Protect == PAGE_EXECUTE_READWRITE,
                               "the memory reports PAGE_EXECUTE_READWRITE") &&
                 passed;
        passed = pp_test_check(run, is_zero(plain, PP_POOL_SMALL), "the memory starts as zeroes") &&
                 passed;
        write_function(plain, 0x1234U);
        passed = pp_test_check(run, plain[0] == 0x8DU && plain[6] == 0xC3U,
                               "the memory holds what is written to it") &&
                 passed;
        passed = pp_test_check(run, function_at(plain)(0x10000U) == 0x11234U,
                               "a function written into the memory runs") &&
                 passed;
        passed = pp_test_check(run, VirtualFree(plain, 0U, MEM_RELEASE) != 0,
                               "the memory can be freed") &&
                 passed;
    }
    pp_test_line(run,
                 "%s: executable requests: 1 GiB of ARM64EC code refused, 64 KiB served, and "
                 "PAGE_EXECUTE_READWRITE memory holds and runs x86-64 code: %s",
                 area, pp_test_verdict(passed));
    return passed;
}

bool pp_pool_generated_code(struct pp_test_run *run, const char *area) {
    const SIZE_T size = (SIZE_T)PP_POOL_FUNCTIONS * PP_POOL_FUNCTION_SIZE;
    uint8_t *code = VirtualAlloc(NULL, size, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (code == NULL) {
        pp_test_check(run, false, "VirtualAlloc gives the memory for the generated functions");
        pp_test_line(run, "%s: generated code: %s", area, pp_test_verdict(false));
        return false;
    }
    // Every function is written before the first one runs, so no code changes after it ran.
    for (uint32_t index = 0U; index < PP_POOL_FUNCTIONS; index++) {
        write_function(code + (size_t)index * PP_POOL_FUNCTION_SIZE, function_constant(index));
    }
    uint32_t wrong = 0U;
    for (uint32_t pass = 0U; pass < PP_POOL_PASSES; pass++) {
        for (uint32_t index = 0U; index < PP_POOL_FUNCTIONS; index++) {
            const generated_function function =
                function_at(code + (size_t)index * PP_POOL_FUNCTION_SIZE);
            const uint32_t argument = index ^ (pass * 0x9E3779B9U);
            if (function(argument) != argument + function_constant(index)) {
                wrong++;
            }
        }
    }
    bool passed = pp_test_check(run, wrong == 0U, "every generated function returns its value");
    passed = pp_test_check(run, VirtualFree(code, 0U, MEM_RELEASE) != 0,
                           "the memory of the generated functions can be freed") &&
             passed;
    pp_test_line(run, "%s: generated code: %u functions in %u passes return their values: %s", area,
                 PP_POOL_FUNCTIONS, PP_POOL_PASSES, pp_test_verdict(passed));
    return passed;
}

#else

// The checks generate x86-64 code and ask for ARM64EC code memory, which FEX's WoW64 module, the
// translator of i686 programs, does not use; stage M7.3 decides what the i686 version checks.
static bool needs_m7_3(struct pp_test_run *run, const char *area) {
    pp_test_check(run, false, "the pool checks have no i686 version before M7.3");
    pp_test_line(run, "%s: the pool checks wait for M7.3: %s", area, pp_test_verdict(false));
    return false;
}

bool pp_pool_requests(struct pp_test_run *run, const char *area) { return needs_m7_3(run, area); }

bool pp_pool_generated_code(struct pp_test_run *run, const char *area) {
    return needs_m7_3(run, area);
}

#endif
