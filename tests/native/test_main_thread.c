// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.3: the main-thread rule that ships. This program links the host as the shipped
// framework compiles it, without PANEPORT_TEST_BUILD, so the main thread is the process's own
// (pthread_main_np), not the first caller of pp_init. CTest runs main() on that thread.

#include <pthread.h>
#include <stdint.h>
#include <string.h>

#include "paneport/host.h"
#include "pp_check.h"
#include "test_support.h"

static void ignore_event(const pp_event_t *event, void *user_data) {
    (void)event;
    (void)user_data;
}

static void ignore_line(const pp_log_line_t *line, void *user_data) {
    (void)line;
    (void)user_data;
}

static void *call_main_functions(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pp_set_event_callback(ignore_event, NULL), PP_E_WRONG_THREAD);
    PP_CHECK(strstr(pp_last_error(), "main thread") != NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(ignore_line, NULL), PP_E_WRONG_THREAD);
    PP_CHECK_EQUAL(pp_shutdown(), PP_E_WRONG_THREAD);
    const pp_init_config_t config = {.struct_size = (uint32_t)sizeof(pp_init_config_t)};
    PP_CHECK_EQUAL(pp_init(&config), PP_E_WRONG_THREAD);
    return NULL;
}

// On the process's main thread every [main] function works; on another thread each fails with
// PP_E_WRONG_THREAD.
static void main_functions_need_the_main_thread(void) {
    pp_start_host(NULL);
    pp_join_thread(pp_start_thread(call_main_functions, NULL));
    PP_CHECK_EQUAL(pp_set_event_callback(ignore_event, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_set_log_callback(ignore_line, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static void *init_elsewhere(void *argument) {
    (void)argument;
    const pp_init_config_t config = {.struct_size = (uint32_t)sizeof(pp_init_config_t)};
    PP_CHECK_EQUAL(pp_init(&config), PP_E_WRONG_THREAD);
    PP_CHECK(strstr(pp_last_error(), "pp_init runs only on the main thread") != NULL);
    return NULL;
}

// A pp_init that another thread calls first fails, and the main thread can still start the host:
// in the shipped builds the first caller does not become the main thread.
static void first_init_on_another_thread_fails(void) {
    pp_join_thread(pp_start_thread(init_elsewhere, NULL));
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"main_functions_need_the_main_thread", main_functions_need_the_main_thread},
        {"first_init_on_another_thread_fails", first_init_on_another_thread_fails},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
