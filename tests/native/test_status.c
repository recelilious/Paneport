// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.1: the interface version, pp_last_error and pp_free.

#include <pthread.h>
#include <stdint.h>
#include <string.h>

#include "paneport/host.h"
#include "paneport/memory.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "test_support.h"

static void abi_version(void) {
    const uint32_t version = pp_abi_version();
    PP_CHECK_EQUAL(version >> 16U, PP_ABI_VERSION_MAJOR);
    PP_CHECK_EQUAL(version & 0xFFFFU, PP_ABI_VERSION_MINOR);
    PP_CHECK(PP_ABI_VERSION_MAJOR >= 1);
}

// What the other thread saw, read by the main thread after joining it.
static char s_other_message[256];
static bool s_other_started_empty;

static void *fail_on_other_thread(void *argument) {
    (void)argument;
    s_other_started_empty = strcmp(pp_last_error(), "") == 0;
    pp_memory_budget_t budget = {.struct_size = 0U};
    PP_CHECK_EQUAL(pp_memory_get_budget(&budget), PP_E_INVALID_ARGUMENT);
    PP_CHECK(snprintf(s_other_message, sizeof s_other_message, "%s", pp_last_error()) > 0);
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
    pp_join_thread(pp_start_thread(fail_on_other_thread, NULL));
    PP_CHECK(s_other_started_empty);
    PP_CHECK(strstr(s_other_message, "at least 4") != NULL);
    PP_CHECK(strcmp(s_other_message, copy) != 0);

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

static void free_host_memory(void) {
    char *copy = NULL;
    PP_CHECK_EQUAL(pp_test_duplicate_text("host memory", &copy), PP_OK);
    PP_CHECK(copy != NULL);
    PP_CHECK_TEXT(copy, "host memory");
    pp_free(copy);
    pp_free(NULL);

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
