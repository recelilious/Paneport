// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "platform.h"

#include <pthread.h>
#include <stddef.h>
#include <sys/sysctl.h>

uint64_t pp_platform_thread_id(void) {
    uint64_t thread_id = 0;
    (void)pthread_threadid_np(NULL, &thread_id);
    return thread_id;
}

bool pp_platform_is_main_thread(void) { return pthread_main_np() == 1; }

void pp_platform_name_thread(const char *name) { (void)pthread_setname_np(name); }

bool pp_platform_device_model(char *buffer, size_t size, size_t *needed) {
    size_t length = 0U;
    if (sysctlbyname("hw.machine", NULL, &length, NULL, 0U) != 0 || length == 0U) {
        return false;
    }
    *needed = length;
    if (size < length) {
        return true;
    }

    size_t written = size;
    if (sysctlbyname("hw.machine", buffer, &written, NULL, 0U) != 0 || written == 0U) {
        return false;
    }
    // The value carries its NUL; a value that grew between the two calls is cut, never overrun.
    buffer[(written < size ? written : size) - 1U] = '\0';
    *needed = written;
    return true;
}
