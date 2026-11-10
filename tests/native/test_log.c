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

// Logs a line, waiting while the queue is full.
static void log_line(const char *text) {
    pp_status_t status = pp_test_log(PP_LOG_INFO, text);
    while (status == PP_E_BUSY) {
        (void)sched_yield();
        status = pp_test_log(PP_LOG_INFO, text);
    }
    PP_CHECK_EQUAL(status, PP_OK);
}

// What lines_whole_and_ordered checks. next and thread_ids are touched by the event thread only.
typedef struct ordered_lines_t {
    unsigned long long next[THREADS];
    uint64_t thread_ids[THREADS];
    _Atomic(uint64_t) received;
} ordered_lines_t;

static void check_line(const pp_log_line_t *line, void *user_data) {
    ordered_lines_t *ordered = user_data;
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
    PP_CHECK_EQUAL(number, ordered->next[thread]);
    char expected[LINE_SIZE];
    make_line(expected, sizeof expected, (unsigned)thread, (unsigned)number);
    PP_CHECK_TEXT(line->text, expected);
    if (number == 0U) {
        ordered->thread_ids[thread] = line->thread_id;
    }
    PP_CHECK_EQUAL(line->thread_id, ordered->thread_ids[thread]);
    ordered->next[thread]++;
    (void)atomic_fetch_add(&ordered->received, 1U);
}

static void *log_lines(void *argument) {
    const pp_thread_argument_t *thread = argument;
    char text[LINE_SIZE];
    for (unsigned line = 0U; line < (unsigned)LINES_PER_THREAD; line++) {
        make_line(text, sizeof text, thread->number, line);
        log_line(text);
    }
    return NULL;
}

// Lines from many threads arrive whole and in order per thread, with each thread's id.
static void lines_whole_and_ordered(void) {
    ordered_lines_t ordered = {.next = {0}, .thread_ids = {0}};
    atomic_init(&ordered.received, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(check_line, &ordered), PP_OK);
    pthread_t threads[THREADS];
    pp_thread_argument_t arguments[THREADS];
    pp_start_threads(threads, arguments, (size_t)THREADS, log_lines, NULL);
    pp_join_threads(threads, (size_t)THREADS);
    pp_wait_for_count(&ordered.received, (uint64_t)THREADS * (uint64_t)LINES_PER_THREAD);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

enum { EARLY_LINES = 100, KEPT_LINES = EARLY_LINES + 8, PENDING_CAPACITY = 64 };

// The lines that a log callback keeps, as early_lines_arrive and the overflow cases read them.
// The texts and dropped are touched by the event thread only, until received says they are
// there.
typedef struct kept_lines_t {
    char texts[KEPT_LINES][PP_LOG_TEXT_SIZE];
    _Atomic(uint64_t) received;
    uint64_t dropped;
} kept_lines_t;

static void keep_line(const pp_log_line_t *line, void *user_data) {
    kept_lines_t *kept = user_data;
    unsigned long long dropped = 0U;
    const char *rest = NULL;
    if (pp_read_number(line->text, "", &dropped, &rest) &&
        strncmp(rest, " host log lines that came before", 32U) == 0) {
        kept->dropped = dropped;
    }
    const uint64_t index = atomic_load(&kept->received);
    if (index < (uint64_t)KEPT_LINES) {
        (void)snprintf(kept->texts[index], sizeof kept->texts[index], "%s", line->text);
    }
    (void)atomic_fetch_add(&kept->received, 1U);
}

// Counts the kept lines that contain text.
static uint64_t count_kept(kept_lines_t *kept, const char *text) {
    uint64_t found = 0U;
    const uint64_t received = atomic_load(&kept->received);
    for (uint64_t index = 0U; index < received && index < (uint64_t)KEPT_LINES; index++) {
        if (strstr(kept->texts[index], text) != NULL) {
            found++;
        }
    }
    return found;
}

// Waits until the callback has kept a line that contains text.
static void wait_for_kept(kept_lines_t *kept, const char *text) {
    for (int round = 0; round < 60000 && count_kept(kept, text) == 0U; round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK(count_kept(kept, text) > 0U);
}

// Waits until the callback has kept a line with this text as its last one.
static void wait_for_last_line(kept_lines_t *kept, const char *text) {
    for (int round = 0; round < 60000; round++) {
        const uint64_t received = atomic_load(&kept->received);
        if (received > 0U && received <= (uint64_t)KEPT_LINES &&
            strcmp(kept->texts[received - 1U], text) == 0) {
            return;
        }
        pp_sleep_milliseconds(1);
    }
    pp_check_failed(__FILE__, __LINE__, "the last line never arrived");
}

// Lines logged before a callback is set arrive once it is, oldest first. The buffer keeps the
// first 64 of them; a notice counts the ones it could not keep.
static void early_lines_arrive(void) {
    kept_lines_t kept = {.dropped = 0U};
    atomic_init(&kept.received, 0U);
    pp_start_host(NULL);
    char text[64];
    for (unsigned line = 0U; line < (unsigned)EARLY_LINES; line++) {
        PP_CHECK(snprintf(text, sizeof text, "early line %u", line) > 0);
        PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, text), PP_OK);
    }
    pp_sleep_milliseconds(200);
    PP_CHECK_EQUAL(pp_set_log_callback(keep_line, &kept), PP_OK);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "last line"), PP_OK);
    wait_for_last_line(&kept, "last line");
    PP_CHECK(atomic_load(&kept.received) >= (uint64_t)PENDING_CAPACITY + 1U);
    PP_CHECK(strstr(kept.texts[0], "native host started") != NULL);

    // Every early line arrives in order, or is counted as dropped; the host's start line
    // makes 101 lines before the callback. Lines that the event thread had not taken from
    // the queue yet arrive after the buffered ones, so the order may skip the dropped ones.
    uint64_t delivered = 1U;
    unsigned long long next_line = 0U;
    for (uint64_t index = 1U; index < atomic_load(&kept.received); index++) {
        unsigned long long number = 0U;
        if (pp_read_number(kept.texts[index], "early line ", &number, NULL)) {
            PP_CHECK(number >= next_line);
            next_line = number + 1U;
            delivered++;
        }
    }
    PP_CHECK_EQUAL(delivered + kept.dropped, (uint64_t)EARLY_LINES + 1U);
    PP_CHECK(delivered >= (uint64_t)PENDING_CAPACITY);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// The line that keep_cut_line kept. line is touched by the event thread until received grows.
typedef struct cut_line_t {
    char line[PP_LOG_TEXT_SIZE + 1U];
    _Atomic(uint64_t) received;
} cut_line_t;

static void keep_cut_line(const pp_log_line_t *line, void *user_data) {
    cut_line_t *cut = user_data;
    if (line->text[0] != 'a' && line->text[0] != 'z') {
        return;
    }
    PP_CHECK(strlen(line->text) < PP_LOG_TEXT_SIZE);
    (void)snprintf(cut->line, sizeof cut->line, "%s", line->text);
    (void)atomic_fetch_add(&cut->received, 1U);
}

// An overlong line is cut at a character boundary and ends with "...", never overflowed.
static void overlong_line_cut(void) {
    cut_line_t cut = {.line = {0}};
    atomic_init(&cut.received, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(keep_cut_line, &cut), PP_OK);

    char long_line[2000];
    memset(long_line, 'z', sizeof long_line - 1U);
    long_line[sizeof long_line - 1U] = '\0';
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_WARNING, long_line), PP_OK);
    pp_wait_for_count(&cut.received, 1U);
    PP_CHECK_EQUAL(strlen(cut.line), PP_LOG_TEXT_SIZE - 1U);
    PP_CHECK(strcmp(cut.line + PP_LOG_TEXT_SIZE - 4U, "...") == 0);
    PP_CHECK(strspn(cut.line, "z") == PP_LOG_TEXT_SIZE - 4U);

    // A two-byte character that the cut would split goes as a whole: 507 letters, then
    // U+00E9, whose second byte would be the 509th.
    memset(long_line, 'a', 507U);
    long_line[507] = (char)0xC3;
    long_line[508] = (char)0xA9;
    memset(long_line + 509, 'b', sizeof long_line - 510U);
    long_line[sizeof long_line - 1U] = '\0';
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_WARNING, long_line), PP_OK);
    pp_wait_for_count(&cut.received, 2U);
    PP_CHECK_EQUAL(strlen(cut.line), 507U + 3U);
    PP_CHECK(strspn(cut.line, "a") == 507U);
    PP_CHECK_TEXT(cut.line + 507, "...");

    // The cut itself, on a three-byte character (U+20AC) at the end.
    const char euro[] = "ab\xE2\x82\xAC";
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 5U), 5U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 4U), 2U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 3U), 2U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 2U), 2U);
    PP_CHECK_EQUAL(pp_utf8_cut(euro, 0U), 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// What overflow_logged_once counts, and whether its callback blocks on the line "block".
typedef struct log_flood_t {
    _Atomic(bool) blocked;
    _Atomic(bool) release;
    _Atomic(uint64_t) notices;
    _Atomic(uint64_t) floods;
} log_flood_t;

static void block_on_marker(const pp_log_line_t *line, void *user_data) {
    log_flood_t *flood = user_data;
    if (strstr(line->text, "log queue was full") != NULL) {
        (void)atomic_fetch_add(&flood->notices, 1U);
    } else if (strcmp(line->text, "flood") == 0) {
        (void)atomic_fetch_add(&flood->floods, 1U);
    } else if (strcmp(line->text, "block") == 0) {
        atomic_store(&flood->blocked, true);
        pp_wait_for_flag(&flood->release);
        atomic_store(&flood->blocked, false);
    }
}

// Fills the log queue while the callback blocks, and returns how many lines it took.
static uint64_t flood_while_blocked(log_flood_t *flood) {
    atomic_store(&flood->release, false);
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "block"), PP_OK);
    pp_wait_for_flag(&flood->blocked);

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
    atomic_store(&flood->release, true);
    return accepted;
}

// A full queue refuses lines, and its first overflow is logged once, never again.
static void overflow_logged_once(void) {
    log_flood_t flood;
    atomic_init(&flood.blocked, false);
    atomic_init(&flood.release, false);
    atomic_init(&flood.notices, 0U);
    atomic_init(&flood.floods, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(block_on_marker, &flood), PP_OK);

    uint64_t accepted = flood_while_blocked(&flood);
    pp_wait_for_count(&flood.floods, accepted);
    pp_wait_for_count(&flood.notices, 1U);

    accepted += flood_while_blocked(&flood);
    pp_wait_for_count(&flood.floods, accepted);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&flood.notices), 1U);
}

// Blocks the event thread inside the event callback until release, so that log lines pile up.
typedef struct blocked_thread_t {
    _Atomic(bool) blocked;
    _Atomic(bool) release;
} blocked_thread_t;

static void block_event_thread(const pp_event_t *event, void *user_data) {
    blocked_thread_t *blocked = user_data;
    (void)event;
    atomic_store(&blocked->blocked, true);
    pp_wait_for_flag(&blocked->release);
}

// The log queue overflows while no log callback is set and the buffer for early lines is full.
// The notice of that overflow waits for a callback instead of being dropped with the lines.
static void overflow_notice_waits_for_a_callback(void) {
    blocked_thread_t blocked;
    atomic_init(&blocked.blocked, false);
    atomic_init(&blocked.release, false);
    kept_lines_t kept = {.dropped = 0U};
    atomic_init(&kept.received, 0U);
    pp_start_host(NULL);

    // First the buffer fills, so that it is full when the event thread sees the overflow.
    for (int line = 0; line < PENDING_CAPACITY + 10; line++) {
        PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "a line that fills the buffer"), PP_OK);
    }
    pp_sleep_milliseconds(200);

    PP_CHECK_EQUAL(pp_set_event_callback(block_event_thread, &blocked), PP_OK);
    PP_CHECK_EQUAL(pp_test_post_event(1U, 0), PP_OK);
    pp_wait_for_flag(&blocked.blocked);

    uint64_t refused = 0U;
    for (int line = 0; line < 1000; line++) {
        if (pp_test_log(PP_LOG_INFO, "a line before any log callback") == PP_E_BUSY) {
            refused++;
        }
    }
    PP_CHECK(refused > 0U);
    atomic_store(&blocked.release, true);
    pp_sleep_milliseconds(200);

    // The notice comes after the buffered lines, once the callback is set; a line logged after
    // it shows that no second notice follows.
    PP_CHECK_EQUAL(pp_set_log_callback(keep_line, &kept), PP_OK);
    wait_for_kept(&kept, "log queue was full");
    PP_CHECK_EQUAL(pp_test_log(PP_LOG_INFO, "last line"), PP_OK);
    wait_for_last_line(&kept, "last line");
    PP_CHECK_EQUAL(count_kept(&kept, "log queue was full"), 1U);
    PP_CHECK(kept.dropped > 0U);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"lines_whole_and_ordered", lines_whole_and_ordered},
        {"early_lines_arrive", early_lines_arrive},
        {"overlong_line_cut", overlong_line_cut},
        {"overflow_logged_once", overflow_logged_once},
        {"overflow_notice_waits_for_a_callback", overflow_notice_waits_for_a_callback},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
