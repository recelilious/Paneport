// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "platform.h"

#include <pthread.h>

uint64_t pp_platform_thread_id(void) {
    uint64_t thread_id = 0;
    (void)pthread_threadid_np(NULL, &thread_id);
    return thread_id;
}

bool pp_platform_is_main_thread(void) { return pthread_main_np() == 1; }

void pp_platform_name_thread(const char *name) { (void)pthread_setname_np(name); }
