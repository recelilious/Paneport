// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.6: the crash record. The checks read the bytes at the offsets that
// docs/design/native-host.md section 8 documents, not through the writer's own code.

#include <pthread.h>
#include <signal.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <time.h>
#include <unistd.h>

#include "crash_record.h"
#include "host_state.h"
#include "paneport/host.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "test_support.h"

static uint64_t read_le(const unsigned char *record, size_t offset, size_t size) {
    uint64_t value = 0U;
    for (size_t index = 0; index < size; index++) {
        value |= (uint64_t)record[offset + index] << (8U * index);
    }
    return value;
}

// Reads the record of a folder; fails unless it has exactly 128 bytes.
static void read_record(const char *folder, unsigned char *record) {
    char path[1024];
    PP_CHECK(snprintf(path, sizeof path, "%s/crash-record.bin", folder) > 0);
    FILE *file = fopen(path, "rb");
    PP_CHECK(file != NULL);
    unsigned char extra = 0U;
    PP_CHECK_EQUAL(fread(record, 1U, 128U, file), 128U);
    PP_CHECK_EQUAL(fread(&extra, 1U, 1U, file), 0U);
    PP_CHECK_EQUAL(fclose(file), 0);
}

static void check_header(const unsigned char *record) {
    PP_CHECK(memcmp(record, "PPCR", 4U) == 0);
    PP_CHECK_EQUAL(read_le(record, 4U, 2U), 1U);   // Version.
    PP_CHECK_EQUAL(read_le(record, 6U, 2U), 128U); // Size.
    for (size_t index = 49U; index < 64U; index++) {
        PP_CHECK_EQUAL(record[index], 0U); // Reserved.
    }
}

static void remove_folder(const char *folder) {
    pp_remove_file(folder, "crash-record.bin");
    pp_remove_file(folder, "crash-record.tmp");
    PP_CHECK_EQUAL(rmdir(folder), 0);
}

// The layout, byte by byte, for known values.
static void encode_layout(void) {
    const pp_crash_info_t info = {
        .signal = 11,
        .code = 2,
        .fault_address = UINT64_C(0x1122334455667788),
        .program_counter = UINT64_C(0x0102030405060708),
        .thread_role = 2U,
        .is_test = 0U,
        .message = "fault in h\xC3\xA9llo",
    };
    unsigned char record[128];
    memset(record, 0xEE, sizeof record);
    pp_crash_record_encode(&info, INT64_C(0x0000000065432100), 123456789, record);

    check_header(record);
    static const unsigned char magic[] = {0x50, 0x50, 0x43, 0x52};
    PP_CHECK(memcmp(record, magic, sizeof magic) == 0);
    PP_CHECK_EQUAL(read_le(record, 8U, 8U), UINT64_C(0x0000000065432100));
    PP_CHECK_EQUAL(read_le(record, 16U, 4U), 123456789U);
    PP_CHECK_EQUAL(read_le(record, 20U, 4U), 11U);
    PP_CHECK_EQUAL(read_le(record, 24U, 4U), 2U);
    PP_CHECK_EQUAL(read_le(record, 28U, 4U), 2U);
    PP_CHECK_EQUAL(read_le(record, 32U, 8U), UINT64_C(0x1122334455667788));
    PP_CHECK_EQUAL(read_le(record, 40U, 8U), UINT64_C(0x0102030405060708));
    PP_CHECK_EQUAL(record[48], 0U);
    PP_CHECK_TEXT((const char *)record + 64, "fault in h\xC3\xA9llo");
    for (size_t index = 64U + strlen("fault in h\xC3\xA9llo"); index < 128U; index++) {
        PP_CHECK_EQUAL(record[index], 0U);
    }

    // A negative signal code keeps its bits.
    const pp_crash_info_t negative = {.signal = 6, .code = -1, .message = NULL};
    pp_crash_record_encode(&negative, 0, 0, record);
    PP_CHECK_EQUAL(read_le(record, 24U, 4U), 0xFFFFFFFFU);
    PP_CHECK_EQUAL(record[64], 0U);
}

// A message longer than 63 bytes is cut at a character boundary, and the record keeps its
// NUL.
static void message_cut(void) {
    char message[80];
    memset(message, 'm', 62U);
    message[62] = (char)0xC3; // U+00E9 would take bytes 62 and 63, and 63 is the NUL's.
    message[63] = (char)0xA9;
    memset(message + 64, 'n', 10U);
    message[74] = '\0';
    const pp_crash_info_t info = {.signal = 0, .is_test = 1U, .message = message};
    unsigned char record[128];
    pp_crash_record_encode(&info, 1, 2, record);
    PP_CHECK_EQUAL(strlen((const char *)record + 64), 62U);
    PP_CHECK_EQUAL(record[127], 0U);
    PP_CHECK_EQUAL(record[48], 1U);
}

enum { TEST_PROGRAM_COUNTER = 0x1DEADBEE };

// What the signal handler hands back to the test. A handler receives no argument of the test,
// so these four are the documented singletons of this test (C-0010 section 3).
static volatile sig_atomic_t s_handled;
static atomic_int s_write_status;
static atomic_int s_seen_code;
static _Atomic(uint64_t) s_seen_address;

// Writes a record from inside the signal handler, as the dispatcher of M2.2 will.
static void handle_signal(int signal_number, siginfo_t *information, void *context) {
    (void)context;
    const pp_crash_info_t info = {
        .signal = signal_number,
        .code = information->si_code,
        .fault_address = (uint64_t)(uintptr_t)information->si_addr,
        .program_counter = (uint64_t)TEST_PROGRAM_COUNTER,
        .thread_role = pp_host_thread_role(),
        .is_test = 0U,
        .message = "fault in a test thread",
    };
    atomic_store(&s_seen_code, information->si_code);
    atomic_store(&s_seen_address, info.fault_address);
    atomic_store(&s_write_status, pp_crash_record_write(&info));
    s_handled = 1;
}

static void *raise_on_thread(void *argument) {
    (void)argument;
    PP_CHECK_EQUAL(pthread_kill(pthread_self(), SIGUSR2), 0);
    for (int round = 0; round < 60000 && s_handled == 0; round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK(s_handled != 0);
    return NULL;
}

// The writer, called in a signal handler on a test thread, leaves a whole record.
static void written_in_signal_handler(void) {
    char folder[256];
    pp_make_folder(folder, sizeof folder);
    pp_start_host(folder);

    struct sigaction action;
    memset(&action, 0, sizeof action);
    action.sa_sigaction = handle_signal;
    action.sa_flags = SA_SIGINFO;
    PP_CHECK_EQUAL(sigemptyset(&action.sa_mask), 0);
    PP_CHECK_EQUAL(sigaction(SIGUSR2, &action, NULL), 0);

    const time_t before = time(NULL);
    pp_join_thread(pp_start_thread(raise_on_thread, NULL));
    const time_t after = time(NULL);
    PP_CHECK_EQUAL(atomic_load(&s_write_status), PP_OK);

    unsigned char record[128];
    read_record(folder, record);
    check_header(record);
    const int64_t seconds = (int64_t)read_le(record, 8U, 8U);
    PP_CHECK(seconds >= (int64_t)before && seconds <= (int64_t)after);
    PP_CHECK(read_le(record, 16U, 4U) < 1000000000U);
    PP_CHECK_EQUAL(read_le(record, 20U, 4U), SIGUSR2);
    PP_CHECK_EQUAL(read_le(record, 24U, 4U), (uint32_t)atomic_load(&s_seen_code));
    PP_CHECK_EQUAL(read_le(record, 28U, 4U), PP_THREAD_ROLE_OTHER);
    PP_CHECK_EQUAL(read_le(record, 32U, 8U), atomic_load(&s_seen_address));
    PP_CHECK_EQUAL(read_le(record, 40U, 8U), TEST_PROGRAM_COUNTER);
    PP_CHECK_EQUAL(record[48], 0U);
    PP_CHECK_TEXT((const char *)record + 64, "fault in a test thread");

    // The temporary file is renamed, never left behind.
    char temporary[512];
    PP_CHECK(snprintf(temporary, sizeof temporary, "%s/crash-record.tmp", folder) > 0);
    PP_CHECK(access(temporary, F_OK) != 0);

    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    remove_folder(folder);
}

// A record that a test writes carries the test flag, and a newer record replaces an older one.
static void test_record_flag(void) {
    char folder[256];
    pp_make_folder(folder, sizeof folder);
    pp_start_host(folder);

    PP_CHECK_EQUAL(pp_test_write_crash_record("first record"), PP_OK);
    PP_CHECK_EQUAL(pp_test_write_crash_record("a test record"), PP_OK);
    unsigned char record[128];
    read_record(folder, record);
    check_header(record);
    PP_CHECK_EQUAL(record[48], 1U);
    PP_CHECK_EQUAL(read_le(record, 20U, 4U), 0U);
    PP_CHECK_EQUAL(read_le(record, 28U, 4U), PP_THREAD_ROLE_MAIN);
    PP_CHECK_TEXT((const char *)record + 64, "a test record");
    PP_CHECK_EQUAL(pp_test_write_crash_record(NULL), PP_E_INVALID_ARGUMENT);

    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    remove_folder(folder);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"encode_layout", encode_layout},
        {"message_cut", message_cut},
        {"written_in_signal_handler", written_in_signal_handler},
        {"test_record_flag", test_record_flag},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
