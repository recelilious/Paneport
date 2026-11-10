// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.4: the log pipeline.

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "event_thread.h"
#include "log.h"
#include "paneport/host.h"
#include "paneport/testing.h"
#include "pp_check.h"
#include "test_support.h"

enum { THREADS = 8, LINES_PER_THREAD = 400, LINE_SIZE = 160 };

// The text of one line: its thread, its number, and a filler that depends on both, so that a
// line mixed with another one shows.
static void make_line(char *text, size_t size, unsigned thread, unsigned line) {
    int length = snprintf(text, size, "thread %u line %u ", thread, line);
    PP_CHECK(length > 0);
    for (size_t at = (size_t)length; at + 1U < size && at < 120U; at++) {
        text[at] = (char)('a' + (char)((thread + line + at) % 26U));
        text[at + 1U] = '\0';
    }
}

static unsigned s_next[THREADS];       // Touched by the event thread only.
static uint64_t s_thread_ids[THREADS]; // Likewise.
static _Atomic(uint64_t) s_received;

static void check_line(const pp_log_line_t *line, void *user_data) {
    (void)user_data;
    PP_CHECK(pp_event_thread_is_current());
    PP_CHECK_EQUAL(line->struct_size, sizeof(pp_log_line_t));
    unsigned long long thread = 0U;
    unsigned long long number = 0U;
    const char *rest = NULL;
    if (!pp_read_number(line->text, "thread ", &thread, &rest) ||
        !pp_read_number(rest, " line ", &number, NULL)) {
        return; // The host's own lines, such as its start.
    }
    PP_CHECK(thread < (unsigned long long)THREADS);
    PP_CHECK_EQUAL(line->level, PP_LOG_INFO);
    PP_CHECK_EQUAL(number, s_next[thread]);
    char expected[LINE_SIZE];
    make_line(expected, sizeof expected, (unsigned)thread, (unsigned)number);
    PP_CHECK_TEXT(line->text, expected);
    if (number == 0U) {
        s_thread_ids[thread] = line->thread_id;
    }
    PP_CHECK_EQUAL(line->thread_id, s_thread_ids[thread]);
    s_next[thread]++;
    (void)atomic_fetch_add(&s_received, 1U);
}

static void *log_lines(void *argument) {
    const unsigned thread = pp_thread_number(argument);
    char text[LINE_SIZE];
    for (unsigned line = 0U; line < (unsigned)LINES_PER_THREAD; line++) {
        make_line(text, sizeof text, thread, line);
        pp_status_t status = pp_test_log(PP_LOG_INFO, text);
        while (status == PP_E_BUSY) {
            (void)sched_yield();
            status = pp_test_log(PP_LOG_INFO, text);
        }
        PP_CHECK_EQUAL(status, PP_OK);
    }
    return NULL;
}

// Lines from many threads arrive whole and in order per thread, with each thread's id.
static void lines_whole_and_ordered(void) {
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(check_line, NULL), PP_OK);
    pthread_t threads[THREADS];
    for (uint32_t thread = 0; thread < (uint32_t)THREADS; thread++) {
        threads[thread] = pp_start_numbered_thread(log_lines, thread);
    }
    for (size_t thread = 0; thread < (size_t)THREADS; thread++) {
        pp_join_thread(threads[thread]);
    }
    pp_wait_for_count(&s_received, (uint64_t)THREADS * (uint64_t)LINES_PER_THREAD);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

enum { EARLY_LINES = 100, PENDING_CAPACITY = 64 };

static char s_early[EARLY_LINES + 8][PP_LOG_TEXT_SIZE]; // Touched by the event thread only.
static _Atomic(uint64_t) s_early_received;
static uint64_t s_dropped; // Likewise.

static void keep_line(const pp_log_line_t *line, void *user_data) {
    (void)user_data;
    unsigned long long dropped = 0U;
    const char *rest = NULL;
    if (pp_read_number(line->text, "", &dropped, &rest) &&
        strncmp(rest, " host log lines that came before", 32U) == 0) {
        s_dropped = dropped;
    }
    const uint64_t index = atomic_load(&s_early_received);
    if (index < (uint64_t)EARLY_LINES + 8U) {
        (void)snprintf(s_early[index], sizeof s_early[index], "%s", line->text);
    }
    (void)atomic_fetch_add(&s_early_received, 1U);
}

// Lines logged before a callback is set arrive once it is, oldest first. The buffer keeps the
// first 64 of them; a notice counts the ones it could not keep.
static void early_lines_arrive(void) {
    pp_start_host(NULL);
    char text[64];
    for (unsigned line = 0U; line < (unsigned)EARLY_LINES; line++) {
        PP_CHECK(snprintf(text, sizeof text, "early line %u", line) > 0);
        PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, text), PP_OK);
    }
    pp_sleep_milliseconds(200);
    PP_CHECK_EQUAL(pp_set_log_callback(keep_line, NULL), PP_OK);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "last line"), PP_OK);

    // Wait for "last line", which comes after everything else.
    for (int round = 0; round < 60000; round++) {
        const uint64_t received = atomic_load(&s_early_received);
        if (received > 0U && strcmp(s_early[received - 1U], "last line") == 0) {
            break;
        }
        pp_sleep_milliseconds(1);
    }
    PP_CHECK(atomic_load(&s_early_received) >= (uint64_t)PENDING_CAPACITY + 1U);
    PP_CHECK(strstr(s_early[0], "native host started") != NULL);

    // Every early line arrives in order, or is counted as dropped; the host's start line
    // makes 101 lines before the callback. Lines that the event thread had not taken from
    // the queue yet arrive after the buffered ones, so the order may skip the dropped ones.
    uint64_t delivered = 1U;
    unsigned long long next_line = 0U;
    for (uint64_t index = 1U; index < atomic_load(&s_early_received); index++) {
        unsigned long long number = 0U;
        if (pp_read_number(s_early[index], "early line ", &number, NULL)) {
            PP_CHECK(number >= next_line);
            next_line = number + 1U;
            delivered++;
        }
    }
    PP_CHECK_EQUAL(delivered + s_dropped, (uint64_t)EARLY_LINES + 1U);
    PP_CHECK(delivered >= (uint64_t)PENDING_CAPACITY);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static char s_cut_line[PP_LOG_TEXT_SIZE + 1U]; // Touched by the event thread only.
static _Atomic(uint64_t) s_cut_received;

static void keep_cut_line(const pp_log_line_t *line, void *user_data) {
    (void)user_data;
    if (line->text[0] != 'a' && line->text[0] != 'z') {
        return;
    }
    PP_CHECK(strlen(line->text) < PP_LOG_TEXT_SIZE);
    (void)snprintf(s_cut_line, sizeof s_cut_line, "%s", line->text);
    (void)atomic_fetch_add(&s_cut_received, 1U);
}

// An overlong line is cut at a character boundary and ends with "...", never overflowed.
static void overlong_line_cut(void) {
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(keep_cut_line, NULL), PP_OK);

    static char long_line[2000];
    memset(long_line, 'z', sizeof long_line - 1U);
    long_line[sizeof long_line - 1U] = '\0';
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_WARNING, long_line), PP_OK);
    pp_wait_for_count(&s_cut_received, 1U);
    PP_CHECK_EQUAL(strlen(s_cut_line), PP_LOG_TEXT_SIZE - 1U);
    PP_CHECK(strcmp(s_cut_line + PP_LOG_TEXT_SIZE - 4U, "...") == 0);
    PP_CHECK(strspn(s_cut_line, "z") == PP_LOG_TEXT_SIZE - 4U);

    // A two-byte character that the cut would split goes as a whole: 507 letters, then
    // U+00E9, whose second byte would be the 509th.
    memset(long_line, 'a', 507U);
    long_line[507] = (char)0xC3;
    long_line[508] = (char)0xA9;
    memset(long_line + 509, 'b', sizeof long_line - 510U);
    long_line[sizeof long_line - 1U] = '\0';
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_WARNING, long_line), PP_OK);
    pp_wait_for_count(&s_cut_received, 2U);
    PP_CHECK_EQUAL(strlen(s_cut_line), 507U + 3U);
    PP_CHECK(strspn(s_cut_line, "a") == 507U);
    PP_CHECK_TEXT(s_cut_line + 507, "...");

    // The cut itself, on a three-byte character (U+20AC) at the end.
    const char euro[] = "ab\xE2\x82\xAC";
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 5U), 5U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 4U), 2U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 3U), 2U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 2U), 2U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 0U), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

static _Atomic(bool) s_blocked;
static _Atomic(bool) s_release;
static _Atomic(uint64_t) s_notices;
static _Atomic(uint64_t) s_floods;

static void block_on_marker(const pp_log_line_t *line, void *user_data) {
    (void)user_data;
    if (strstr(line->text, "log queue was full") != NULL) {
        (void)atomic_fetch_add(&s_notices, 1U);
    } else if (strcmp(line->text, "flood") == 0) {
        (void)atomic_fetch_add(&s_floods, 1U);
    } else if (strcmp(line->text, "block") == 0) {
        atomic_store(&s_blocked, true);
        while (!atomic_load(&s_release)) {
            pp_sleep_milliseconds(1);
        }
        atomic_store(&s_blocked, false);
    }
}

// Fills the log queue while the callback blocks, and returns how many lines it took.
static uint64_t flood_while_blocked(void) {
    atomic_store(&s_release, false);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "block"), PP_OK);
    for (int round = 0; round < 60000 && !atomic_load(&s_blocked); round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK(atomic_load(&s_blocked));

    uint64_t accepted = 0U;
    uint64_t refused = 0U;
    for (int line = 0; line < 2000; line++) {
        const pp_status_t status = pp_test_log(PP_LOG_INFO, "flood");
        if (status == PP_OK) {
            accepted++;
        } else {
            PP_CHECK_EQUAL(status, PP_E_BUSY);
            refused++;
        }
    }
    PP_CHECK(refused > 0U);
    atomic_store(&s_release, true);
    return accepted;
}

// A full queue refuses lines, and its first overflow is logged once, never again.
static void overflow_logged_once(void) {
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(block_on_marker, NULL), PP_OK);

    uint64_t accepted = flood_while_blocked();
    pp_wait_for_count(&s_floods, accepted);
    pp_wait_for_count(&s_notices, 1U);

    accepted += flood_while_blocked();
    pp_wait_for_count(&s_floods, accepted);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&s_notices), 1U);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"lines_whole_and_ordered", lines_whole_and_ordered},
        {"early_lines_arrive", early_lines_arrive},
        {"overlong_line_cut", overlong_line_cut},
        {"overflow_logged_once", overflow_logged_once},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
