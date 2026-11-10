// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "crash_record.h"

#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include <time.h>
#include <unistd.h>

#include "error.h"
#include "log.h"

#define PP_CRASH_PATH_SIZE PATH_MAX

// The two paths that pp_init prepares, and the writer's lock: a process-wide singleton
// (C-0010 section 3). The paths are written once, before ready turns true.
static struct {
    char record_path[PP_CRASH_PATH_SIZE];
    char temporary_path[PP_CRASH_PATH_SIZE];
    _Atomic(bool) ready;
    atomic_flag busy;
} s_crash = {.busy = ATOMIC_FLAG_INIT};

// The record is little-endian whatever the machine, so each value is stored byte by byte.
static void put_u16(unsigned char *at, uint16_t value) {
    at[0] = (unsigned char)(value & 0xFFU);
    at[1] = (unsigned char)(value >> 8U);
}

static void put_u32(unsigned char *at, uint32_t value) {
    for (unsigned shift = 0U; shift < 32U; shift += 8U) {
        at[shift / 8U] = (unsigned char)((value >> shift) & 0xFFU);
    }
}

static void put_u64(unsigned char *at, uint64_t value) {
    for (unsigned shift = 0U; shift < 64U; shift += 8U) {
        at[shift / 8U] = (unsigned char)((value >> shift) & 0xFFU);
    }
}

void pp_crash_record_encode(const pp_crash_info_t *info, int64_t seconds, int32_t nanoseconds,
                            unsigned char *record) {
    for (size_t index = 0U; index < PP_CRASH_RECORD_SIZE; index++) {
        record[index] = 0U;
    }

    put_u32(record + 0U, PP_CRASH_RECORD_MAGIC);
    put_u16(record + 4U, (uint16_t)PP_CRASH_RECORD_VERSION);
    put_u16(record + 6U, (uint16_t)PP_CRASH_RECORD_SIZE);
    put_u64(record + 8U, (uint64_t)seconds);
    put_u32(record + 16U, (uint32_t)nanoseconds);
    put_u32(record + 20U, (uint32_t)info->signal);
    put_u32(record + 24U, (uint32_t)info->code);
    put_u32(record + 28U, info->thread_role);
    put_u64(record + 32U, info->fault_address);
    put_u64(record + 40U, info->program_counter);
    record[48] = info->is_test != 0U ? (unsigned char)PP_CRASH_RECORD_FLAG_TEST : 0U;
    // Bytes 49 to 63 are reserved and stay zero.

    if (info->message == NULL) {
        return;
    }
    size_t length = 0U;
    while (length < PP_CRASH_RECORD_MESSAGE_SIZE - 1U && info->message[length] != '\0') {
        length++;
    }
    length = pp_utf8_cut(info->message, length);
    for (size_t index = 0U; index < length; index++) {
        record[PP_CRASH_RECORD_MESSAGE_OFFSET + index] = (unsigned char)info->message[index];
    }
}

pp_status_t pp_crash_record_prepare(const char *folder) {
    size_t length = strlen(folder);
    while (length > 1U && folder[length - 1U] == '/') {
        length--;
    }
    if (length == 0U) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "The crash record folder is empty.");
    }
    if (length + 1U + sizeof(PP_CRASH_RECORD_TEMPORARY_NAME) > sizeof s_crash.record_path) {
        return pp_error_set(
            PP_E_INVALID_ARGUMENT, "The crash record folder's path is longer than %zu bytes.",
            sizeof s_crash.record_path - 1U - sizeof(PP_CRASH_RECORD_TEMPORARY_NAME));
    }

    char path[PP_CRASH_PATH_SIZE];
    memcpy(path, folder, length);
    path[length] = '\0';

    struct stat information;
    if (stat(path, &information) != 0 || !S_ISDIR(information.st_mode)) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "The crash record folder does not exist or is not a folder.");
    }
    if (access(path, W_OK) != 0) {
        return pp_error_set(PP_E_IO, "The crash record folder cannot be written.");
    }

    (void)snprintf(s_crash.record_path, sizeof s_crash.record_path, "%s/%s", path,
                   PP_CRASH_RECORD_FILE_NAME);
    (void)snprintf(s_crash.temporary_path, sizeof s_crash.temporary_path, "%s/%s", path,
                   PP_CRASH_RECORD_TEMPORARY_NAME);
    atomic_store_explicit(&s_crash.ready, true, memory_order_release);
    return PP_OK;
}

// Only async-signal-safe calls from here on: open, write, fsync, close, rename, unlink.
static pp_status_t write_file(const unsigned char *record) {
    const int file =
        open(s_crash.temporary_path, O_WRONLY | O_CREAT | O_TRUNC | O_CLOEXEC, S_IRUSR | S_IWUSR);
    if (file < 0) {
        return PP_E_IO;
    }

    size_t written = 0U;
    bool failed = false;
    while (written < PP_CRASH_RECORD_SIZE && !failed) {
        const ssize_t result = write(file, record + written, PP_CRASH_RECORD_SIZE - written);
        if (result > 0) {
            written += (size_t)result;
        } else if (result == 0 || errno != EINTR) {
            failed = true;
        }
    }
    if (!failed && fsync(file) != 0) {
        failed = true;
    }
    if (close(file) != 0) {
        failed = true;
    }
    if (!failed && rename(s_crash.temporary_path, s_crash.record_path) != 0) {
        failed = true;
    }
    if (failed) {
        (void)unlink(s_crash.temporary_path);
        return PP_E_IO;
    }
    return PP_OK;
}

pp_status_t pp_crash_record_write(const pp_crash_info_t *info) {
    if (!atomic_load_explicit(&s_crash.ready, memory_order_acquire)) {
        return PP_E_NOT_INITIALIZED;
    }
    if (atomic_flag_test_and_set_explicit(&s_crash.busy, memory_order_acquire)) {
        return PP_E_BUSY;
    }

    const int saved_errno = errno;
    struct timespec now = {0};
    (void)clock_gettime(CLOCK_REALTIME, &now);

    unsigned char record[PP_CRASH_RECORD_SIZE];
    pp_crash_record_encode(info, (int64_t)now.tv_sec, (int32_t)now.tv_nsec, record);
    const pp_status_t status = write_file(record);

    errno = saved_errno;
    atomic_flag_clear_explicit(&s_crash.busy, memory_order_release);
    return status;
}
