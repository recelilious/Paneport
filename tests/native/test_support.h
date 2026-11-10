// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Helpers that several native tests share: starting the host, waiting, threads and
// temporary folders.

#ifndef PANEPORT_TESTS_TEST_SUPPORT_H
#define PANEPORT_TESTS_TEST_SUPPORT_H

#include <errno.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "paneport/host.h"
#include "pp_check.h"

// Starts the host, with a crash record folder or none.
static inline void pp_start_host(const char *crash_record_folder) {
    const pp_init_config_t config = {
        .struct_size = (uint32_t)sizeof(pp_init_config_t),
        .crash_record_folder = crash_record_folder,
    };
    PP_CHECK_EQUAL(pp_init(&config), PP_OK);
}

static inline void pp_sleep_milliseconds(long milliseconds) {
    const struct timespec duration = {
        .tv_sec = milliseconds / 1000L,
        .tv_nsec = (milliseconds % 1000L) * 1000000L,
    };
    (void)nanosleep(&duration, NULL);
}

// Waits up to a minute for a counter to reach a value, which leaves room for slow sanitizer
// runs, and fails the test when it does not.
static inline void pp_wait_for_count(_Atomic(uint64_t) *counter, uint64_t expected) {
    for (int round = 0; round < 60000 && atomic_load(counter) < expected; round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK_EQUAL(atomic_load(counter), expected);
}

static inline pthread_t pp_start_thread(void *(*function)(void *), void *argument) {
    pthread_t thread;
    PP_CHECK_EQUAL(pthread_create(&thread, NULL, function, argument), 0);
    return thread;
}

static inline void pp_join_thread(pthread_t thread) {
    PP_CHECK_EQUAL(pthread_join(thread, NULL), 0);
}

// Creates an empty folder under /tmp, whose path is written to path.
static inline void pp_make_folder(char *path, size_t size) {
    PP_CHECK(snprintf(path, size, "/tmp/paneport-native-XXXXXX") > 0);
    PP_CHECK(mkdtemp(path) != NULL);
}

// Reads the decimal number that follows prefix at the start of text. Returns whether text
// starts so; rest, unless NULL, receives the text after the number.
static inline bool pp_read_number(const char *text, const char *prefix, unsigned long long *number,
                                  const char **rest) {
    const size_t length = strlen(prefix);
    if (strncmp(text, prefix, length) != 0) {
        return false;
    }
    const char *digits = text + length;
    if (*digits < '0' || *digits > '9') {
        return false;
    }
    char *end = NULL;
    errno = 0;
    const unsigned long long value = strtoull(digits, &end, 10);
    if (errno != 0) {
        return false;
    }
    *number = value;
    if (rest != NULL) {
        *rest = end;
    }
    return true;
}

// The numbers 0 to 15, whose addresses tell test threads apart.
static const uint32_t k_thread_numbers[16] = {0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15};

// Starts a thread that receives the address of its number in k_thread_numbers.
static inline pthread_t pp_start_numbered_thread(void *(*function)(void *), uint32_t number) {
    PP_CHECK(number < 16U);
    return pp_start_thread(function, (void *)&k_thread_numbers[number]);
}

static inline uint32_t pp_thread_number(const void *argument) {
    return *(const uint32_t *)argument;
}

// Removes a file of a folder, if it exists.
static inline void pp_remove_file(const char *folder, const char *name) {
    char path[1024];
    PP_CHECK(snprintf(path, sizeof path, "%s/%s", folder, name) > 0);
    (void)remove(path);
}

#endif // PANEPORT_TESTS_TEST_SUPPORT_H
