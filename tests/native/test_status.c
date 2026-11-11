// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.1: the interface version, pp_last_error and pp_free.

#include <pthread.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#include "paneport/host.h"
#include "paneport/memory.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "test_support.h"

// The sanitizers count the bytes that the program holds, which shows a leak on macOS too, where
// LeakSanitizer is off.
#ifdef __has_feature
#if __has_feature(address_sanitizer) || __has_feature(thread_sanitizer)
#include <sanitizer/allocator_interface.h>
#define PP_ALLOCATOR_STATISTICS 1
#endif
#endif

static void abi_version(void) {
    const uint32_t version = pp_abi_version();
    PP_CHECK_EQUAL(version >> 16U, PP_ABI_VERSION_MAJOR);
    PP_CHECK_EQUAL(version & 0xFFFFU, PP_ABI_VERSION_MINOR);
    PP_CHECK(PP_ABI_VERSION_MAJOR >= 1);
}

// What the other thread saw, which the main thread reads after joining it.
typedef struct other_thread_t {
    char message[256];
    bool started_empty;
} other_thread_t;

static void *fail_on_other_thread(void *argument) {
    other_thread_t *other = argument;
    other->started_empty = strcmp(pp_last_error(), "") == 0;
    pp_memory_budget_t budget = {.struct_size = 0U};
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_E_INVALID_ARGUMENT);
    PP_CHECK(snprintf(other->message, sizeof other->message, "%s", pp_last_error()) > 0);
    return NULL;
}

static void last_error_per_thread(void) {
    PP_CHECK_TEXT(pp_last_error(), "");
    PP_CHECK_EQUAL(pp_memory_get_budget(NULL), PP_E_INVALID_ARGUMENT);
    const char *message = pp_last_error();
    char copy[256];
    PP_CHECK(snprintf(copy, sizeof copy, "%s", message) > 0);
    PP_CHECK(strstr(copy, "needs a struct") != NULL);

    // A failure on another thread sets only that thread's message.
    other_thread_t other = {.started_empty = false};
    pp_join_thread(pp_start_thread(fail_on_other_thread, &other));
    PP_CHECK(other.started_empty);
    PP_CHECK(strstr(other.message, "at least 4") != NULL);
    PP_CHECK(strcmp(other.message, copy) != 0);

    // This thread's message is still there, at the same address, until its next call.
    PP_CHECK(pp_last_error() == message);
    PP_CHECK_TEXT(message, copy);
    PP_CHECK_EQUAL(pp_abi_version() >> 16U, PP_ABI_VERSION_MAJOR);
    PP_CHECK_TEXT(pp_last_error(), copy);

    // The next call that clears it is one that can fail, here one that succeeds.
    pp_memory_budget_t budget = {.struct_size = (uint32_t)sizeof(pp_memory_budget_t)};
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_OK);
    PP_CHECK_TEXT(pp_last_error(), "");
}

static size_t allocated_bytes(void) {
#ifdef PP_ALLOCATOR_STATISTICS
    return __sanitizer_get_current_allocated_bytes();
#else
    return 0U;
#endif
}

// Host memory freed with pp_free is gone: the bytes that the program holds are back where they
// were before the copy.
static void free_host_memory(void) {
    char *copy = NULL;
    // A first round, so that anything allocated once on the first call is counted before.
    PP_CHECK_EQUAL(pp_test_duplicate_text("warm-up", &copy), PP_OK);
    pp_free(copy);

    const size_t before = allocated_bytes();
    PP_CHECK_EQUAL(pp_test_duplicate_text("host memory", &copy), PP_OK);
    PP_CHECK(copy != NULL);
    PP_CHECK_TEXT(copy, "host memory");
#ifdef PP_ALLOCATOR_STATISTICS
    PP_CHECK(allocated_bytes() >= before + sizeof "host memory");
#endif
    pp_free(copy);
    pp_free(NULL);
    PP_CHECK_EQUAL(allocated_bytes(), before);

    PP_CHECK_EQUAL(pp_test_duplicate_text(NULL, &copy), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_test_duplicate_text("text", NULL), PP_E_INVALID_ARGUMENT);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"abi_version", abi_version},
        {"last_error_per_thread", last_error_per_thread},
        {"free_host_memory", free_host_memory},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
