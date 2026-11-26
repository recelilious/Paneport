// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.3.8: the splitter of a Windows program's output (guest_output.h) gives the same lines
// however the bytes arrive: whole, in chunks of every size, or cut at random; and the reader of
// a program's pipes, which ends when it is woken although a write end stays open.

#include <fcntl.h>
#include <pthread.h>
#include <stdatomic.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include "event_thread.h"
#include "fake_wine_fork.h"
#include "guest_output.h"
#include "paneport/host.h"
#include "pp_check.h"
#include "test_support.h"

enum { MAX_LINES = 64, MAX_LINE = 600 };

typedef struct lines_t {
    char text[MAX_LINES][MAX_LINE + 1];
    size_t length[MAX_LINES];
    size_t count;
} lines_t;

static void collect(const char *line, size_t length, void *context) {
    lines_t *lines = context;
    PP_CHECK(lines->count < (size_t)MAX_LINES);
    PP_CHECK(length <= PP_GUEST_LINE_MAX);
    memcpy(lines->text[lines->count], line, length);
    lines->text[lines->count][length] = '\0';
    lines->length[lines->count] = length;
    lines->count++;
}

// Splits text into lines, fed in chunks of the given sizes, used in turn; one size of 0 feeds it
// all at once.
static lines_t *split(const char *text, size_t length, const size_t *sizes, size_t size_count) {
    lines_t *lines = calloc(1U, sizeof *lines);
    PP_CHECK(lines != NULL);
    pp_line_splitter_t splitter;
    pp_line_splitter_init(&splitter);
    size_t offset = 0U;
    size_t round = 0U;
    while (offset < length) {
        size_t size = sizes[round % size_count];
        if (size == 0U || size > length - offset) {
            size = length - offset;
        }
        pp_line_splitter_feed(&splitter, text + offset, size, collect, lines);
        offset += size;
        round++;
    }
    pp_line_splitter_finish(&splitter, collect, lines);
    return lines;
}

static lines_t *split_whole(const char *text) {
    static const size_t k_whole[] = {0U};
    return split(text, strlen(text), k_whole, 1U);
}

static void check_same(const lines_t *actual, const lines_t *expected) {
    PP_CHECK_EQUAL(actual->count, expected->count);
    for (size_t index = 0U; index < expected->count; index++) {
        PP_CHECK_EQUAL(actual->length[index], expected->length[index]);
        PP_CHECK(memcmp(actual->text[index], expected->text[index], expected->length[index]) == 0);
    }
}

static void check_line(const lines_t *lines, size_t index, const char *expected) {
    PP_CHECK(index < lines->count);
    PP_CHECK_TEXT(lines->text[index], expected);
}

// The fake fork's output gives its six lines, the long one in two pieces, whatever the chunks.
static void every_chunking_gives_the_same_lines(void) {
    static const char k_text[] = FAKE_FORK_OUTPUT;
    lines_t *reference = split_whole(k_text);
    PP_CHECK_EQUAL(reference->count, 6U);
    check_line(reference, 0U, "first line");
    check_line(reference, 1U, "second line");
    PP_CHECK_EQUAL(reference->length[2], PP_GUEST_LINE_MAX);
    PP_CHECK_EQUAL(reference->length[3], 600U - PP_GUEST_LINE_MAX);
    for (size_t index = 0U; index < reference->length[2]; index++) {
        PP_CHECK_EQUAL(reference->text[2][index], 'x');
    }
    check_line(reference, 4U, FAKE_FORK_BYTES_LINE);
    check_line(reference, 5U, "last line");

    for (size_t chunk = 1U; chunk <= 64U; chunk++) {
        lines_t *lines = split(k_text, sizeof k_text - 1U, &chunk, 1U);
        check_same(lines, reference);
        free(lines);
    }
    static const size_t k_large[] = {511U, 512U, 1000U};
    for (size_t index = 0U; index < sizeof k_large / sizeof k_large[0]; index++) {
        lines_t *lines = split(k_text, sizeof k_text - 1U, &k_large[index], 1U);
        check_same(lines, reference);
        free(lines);
    }
    // Chunks of random sizes from 1 to 40, from a fixed linear congruential generator.
    size_t random_sizes[257];
    uint32_t state = 12345U;
    for (size_t index = 0U; index < sizeof random_sizes / sizeof random_sizes[0]; index++) {
        state = (state * 1103515245U) + 12345U;
        random_sizes[index] = 1U + (size_t)((state >> 16U) % 40U);
    }
    lines_t *lines = split(k_text, sizeof k_text - 1U, random_sizes,
                           sizeof random_sizes / sizeof random_sizes[0]);
    check_same(lines, reference);
    free(lines);
    free(reference);
}

// A piece of a long line ends at a character boundary, never inside a character.
static void piece_ends_at_a_character(void) {
    char text[600];
    memset(text, 'a', 510U);
    memcpy(text + 510U,
           "\xc3\xa9"
           "b\n",
           5U);
    lines_t *lines = split(text, 514U, (const size_t[]){1U}, 1U);
    PP_CHECK_EQUAL(lines->count, 2U);
    PP_CHECK_EQUAL(lines->length[0], 510U);
    check_line(lines, 1U,
               "\xc3\xa9"
               "b");
    free(lines);

    memset(text, 'a', 509U);
    // The terminator lands past the 514 bytes that the split reads.
    static const char k_four_bytes[] = "\xf0\x9f\x98\x80\n";
    memcpy(text + 509U, k_four_bytes, sizeof k_four_bytes);
    lines = split(text, 514U, (const size_t[]){3U}, 1U);
    PP_CHECK_EQUAL(lines->count, 2U);
    PP_CHECK_EQUAL(lines->length[0], 509U);
    check_line(lines, 1U, "\xf0\x9f\x98\x80");
    free(lines);
}

// Only a carriage return right before a line feed, or at the very end, goes with the line break.
static void carriage_returns(void) {
    static const char k_text[] = "a\r\nb\r\nc\rd\n\r\re\r\nlast\r";
    static const size_t k_sizes[] = {1U, 2U, 3U};
    for (size_t index = 0U; index < sizeof k_sizes / sizeof k_sizes[0]; index++) {
        lines_t *lines = split(k_text, sizeof k_text - 1U, &k_sizes[index], 1U);
        PP_CHECK_EQUAL(lines->count, 5U);
        check_line(lines, 0U, "a");
        check_line(lines, 1U, "b");
        check_line(lines, 2U, "c\rd");
        check_line(lines, 3U, "\r\re");
        check_line(lines, 4U, "last");
        free(lines);
    }

    // A line that fills a piece exactly and ends with CR LF is one line, with no empty one after.
    char full[PP_GUEST_LINE_MAX + 2U];
    memset(full, 'f', PP_GUEST_LINE_MAX);
    full[PP_GUEST_LINE_MAX] = '\r';
    full[PP_GUEST_LINE_MAX + 1U] = '\n';
    lines_t *lines = split(full, sizeof full, (const size_t[]){7U}, 1U);
    PP_CHECK_EQUAL(lines->count, 1U);
    PP_CHECK_EQUAL(lines->length[0], PP_GUEST_LINE_MAX);
    free(lines);
}

// Empty lines stay, nothing gives no line, and a last line without a break still arrives.
static void empty_lines_and_the_end(void) {
    lines_t *lines = split_whole("\n\n");
    PP_CHECK_EQUAL(lines->count, 2U);
    PP_CHECK_EQUAL(lines->length[0], 0U);
    PP_CHECK_EQUAL(lines->length[1], 0U);
    free(lines);

    lines = split_whole("");
    PP_CHECK_EQUAL(lines->count, 0U);
    free(lines);

    lines = split_whole("no break");
    PP_CHECK_EQUAL(lines->count, 1U);
    check_line(lines, 0U, "no break");
    free(lines);
}

// What the log callback saw of a reader's lines, in a context of the case.
typedef struct guest_lines_t {
    pthread_mutex_t mutex;
    char text[MAX_LINES][MAX_LINE + 1];
    uint32_t source[MAX_LINES];
    _Atomic(uint64_t) count;
} guest_lines_t;

static void record_guest_line(const pp_log_line_t *line, void *user_data) {
    guest_lines_t *lines = user_data;
    if (line->source != PP_LOG_SOURCE_GUEST_OUTPUT && line->source != PP_LOG_SOURCE_GUEST_ERRORS) {
        return;
    }
    PP_CHECK_EQUAL(line->process, 5U);
    (void)pthread_mutex_lock(&lines->mutex);
    const uint64_t index = atomic_load(&lines->count);
    PP_CHECK(index < (uint64_t)MAX_LINES);
    (void)snprintf(lines->text[index], sizeof lines->text[index], "%s", line->text);
    lines->source[index] = line->source;
    atomic_store(&lines->count, index + 1U);
    (void)pthread_mutex_unlock(&lines->mutex);
}

// The handle that the reader's end named: on_end takes no context, so this is the program's one
// record of it.
static _Atomic(uint64_t) s_ended_process;

static void note_end(uint64_t process) { atomic_store(&s_ended_process, process); }

// Starts the host with the callback and a reader of two pipes for process 5, whose write ends go
// to writers.
static pp_guest_output_t *start_reader(guest_lines_t *lines, int writers[2]) {
    (void)pthread_mutex_init(&lines->mutex, NULL);
    atomic_init(&lines->count, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(record_guest_line, lines), PP_OK);
    int output[2];
    int errors[2];
    PP_CHECK_EQUAL(pipe(output), 0);
    PP_CHECK_EQUAL(pipe(errors), 0);
    pp_guest_output_t *reader = NULL;
    PP_CHECK_EQUAL(pp_guest_output_start(output[0], errors[0], 5U, note_end, &reader), PP_OK);
    writers[0] = output[1];
    writers[1] = errors[1];
    return reader;
}

static void write_all(int fd, const char *text, size_t length) {
    PP_CHECK_EQUAL(write(fd, text, length), (ssize_t)length);
}

// A reader whose pipes keep a write end open, as a runtime's copy would, ends once it is woken:
// what the pipes hold still arrives, the last piece without a line feed too, and its end names
// the process.
static void wake_ends_a_reader_whose_pipes_stay_open(void) {
    guest_lines_t lines;
    int writers[2];
    pp_guest_output_t *reader = start_reader(&lines, writers);
    write_all(writers[0], "first\n", 6U);
    pp_wait_for_count(&lines.count, 1U);
    write_all(writers[1], "an error\nlast piece", 19U);
    write_all(writers[0], "output piece", 12U);
    pp_guest_output_wake(reader);
    pp_guest_output_join(reader);
    PP_CHECK_EQUAL(atomic_load(&s_ended_process), 5U);
    pp_wait_for_count(&lines.count, 4U);
    PP_CHECK_TEXT(lines.text[0], "first");
    // The two streams' lines may interleave; each stream keeps its own order.
    static const char *const k_texts[] = {"output piece", "an error", "last piece"};
    static const uint32_t k_sources[] = {
        PP_LOG_SOURCE_GUEST_OUTPUT,
        PP_LOG_SOURCE_GUEST_ERRORS,
        PP_LOG_SOURCE_GUEST_ERRORS,
    };
    size_t found[3] = {0U, 0U, 0U};
    for (size_t text = 0U; text < 3U; text++) {
        for (size_t index = 1U; index < 4U; index++) {
            if (strcmp(lines.text[index], k_texts[text]) == 0) {
                found[text] = index;
            }
        }
        PP_CHECK(found[text] != 0U);
        PP_CHECK_EQUAL(lines.source[found[text]], k_sources[text]);
    }
    PP_CHECK(found[1] < found[2]);
    (void)close(writers[0]);
    (void)close(writers[1]);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

// How much the writer of woken_reader_takes_at_most_64_kib adds to the pipe each time it is asked.
enum { FILL_SIZE = 600 };

// What woken_reader_takes_at_most_64_kib watches. A writer thread keeps the output pipe from
// running dry while the woken reader takes from it, as a program that keeps writing would: the
// post hook asks it for FILL_SIZE more bytes before each post of the reader and waits for them,
// so that every read of the reader finds bytes, which are x's. The hook holds the reader's first
// post, of the one line written before the wake, until the test woke the reader, and the log
// callback adds up the length of the pieces of x's that arrive.
typedef struct filled_pipe_t {
    pthread_mutex_t mutex;
    pthread_cond_t changed;
    pthread_t test_thread;
    int fd;
    uint64_t requested;
    uint64_t filled;
    bool stop;
    _Atomic(bool) in_hook;
    _Atomic(bool) release;
    _Atomic(uint64_t) taken;
    _Atomic(uint64_t) other_lines;
} filled_pipe_t;

static void fill_before_a_post(void *argument) {
    filled_pipe_t *filled = argument;
    if (pthread_equal(pthread_self(), filled->test_thread) != 0) {
        return;
    }
    if (!atomic_exchange(&filled->in_hook, true)) {
        pp_wait_for_flag(&filled->release);
    }
    (void)pthread_mutex_lock(&filled->mutex);
    filled->requested++;
    const uint64_t request = filled->requested;
    (void)pthread_cond_broadcast(&filled->changed);
    while (filled->filled < request) {
        (void)pthread_cond_wait(&filled->changed, &filled->mutex);
    }
    (void)pthread_mutex_unlock(&filled->mutex);
}

static void *fill_on_request(void *argument) {
    filled_pipe_t *filled = argument;
    char chunk[FILL_SIZE];
    memset(chunk, 'x', sizeof chunk);
    (void)pthread_mutex_lock(&filled->mutex);
    for (;;) {
        while (!filled->stop && filled->filled == filled->requested) {
            (void)pthread_cond_wait(&filled->changed, &filled->mutex);
        }
        if (filled->stop) {
            break;
        }
        (void)pthread_mutex_unlock(&filled->mutex);
        // The write end does not block: a full pipe takes what fits, or nothing, and holds bytes.
        (void)write(filled->fd, chunk, sizeof chunk);
        (void)pthread_mutex_lock(&filled->mutex);
        filled->filled++;
        (void)pthread_cond_broadcast(&filled->changed);
    }
    (void)pthread_mutex_unlock(&filled->mutex);
    return NULL;
}

static void count_taken(const pp_log_line_t *line, void *user_data) {
    filled_pipe_t *filled = user_data;
    if (line->source != PP_LOG_SOURCE_GUEST_OUTPUT) {
        return;
    }
    const size_t length = strlen(line->text);
    if (length > 0U && strspn(line->text, "x") == length) {
        (void)atomic_fetch_add(&filled->taken, (uint64_t)length);
    } else {
        (void)atomic_fetch_add(&filled->other_lines, 1U);
    }
}

// A reader that is woken while a writer keeps its pipe from running dry, as the runtime's
// process may after the stop's wait, ends within a bound, after it took exactly 64 KiB of that
// stream: the limit bounds the join of pp_session_stop.
static void woken_reader_takes_at_most_64_kib(void) {
    filled_pipe_t filled = {.test_thread = pthread_self(), .requested = 0U, .filled = 0U};
    PP_CHECK_EQUAL(pthread_mutex_init(&filled.mutex, NULL), 0);
    PP_CHECK_EQUAL(pthread_cond_init(&filled.changed, NULL), 0);
    atomic_init(&filled.in_hook, false);
    atomic_init(&filled.release, false);
    atomic_init(&filled.taken, 0U);
    atomic_init(&filled.other_lines, 0U);
    pp_start_host(NULL);
    PP_CHECK_EQUAL(pp_set_log_callback(count_taken, &filled), PP_OK);
    int output[2];
    int errors[2];
    PP_CHECK_EQUAL(pipe(output), 0);
    PP_CHECK_EQUAL(pipe(errors), 0);
    const int flags = fcntl(output[1], F_GETFL);
    PP_CHECK(flags >= 0);
    PP_CHECK_EQUAL(fcntl(output[1], F_SETFL, (int)((unsigned)flags | (unsigned)O_NONBLOCK)), 0);
    filled.fd = output[1];
    pp_event_thread_set_post_hook(fill_before_a_post, &filled);
    const pthread_t writer = pp_start_thread(fill_on_request, &filled);
    pp_guest_output_t *reader = NULL;
    PP_CHECK_EQUAL(pp_guest_output_start(output[0], errors[0], 5U, note_end, &reader), PP_OK);

    write_all(output[1], "first\n", 6U);
    pp_wait_for_flag(&filled.in_hook);
    pp_guest_output_wake(reader);
    atomic_store(&filled.release, true);
    for (int round = 0; round < 30000 && atomic_load(&s_ended_process) == 0U; round++) {
        pp_sleep_milliseconds(1);
    }
    PP_CHECK_EQUAL(atomic_load(&s_ended_process), 5U);
    pp_guest_output_join(reader);

    (void)pthread_mutex_lock(&filled.mutex);
    filled.stop = true;
    (void)pthread_cond_broadcast(&filled.changed);
    (void)pthread_mutex_unlock(&filled.mutex);
    pp_join_thread(writer);
    pp_event_thread_set_post_hook(NULL, NULL);
    (void)close(output[1]);
    (void)close(errors[1]);
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
    PP_CHECK_EQUAL(atomic_load(&filled.taken), 64U * 1024U);
    PP_CHECK_EQUAL(atomic_load(&filled.other_lines), 1U);
    (void)pthread_cond_destroy(&filled.changed);
    (void)pthread_mutex_destroy(&filled.mutex);
}

// A NUL byte of a program's output becomes U+FFFD, so the rest of its line still arrives, and a
// line that the replacements make longer than a log line holds arrives in pieces, cut between
// two replacement characters.
static void nul_bytes_become_replacements(void) {
    guest_lines_t lines;
    int writers[2];
    pp_guest_output_t *reader = start_reader(&lines, writers);
    write_all(writers[0], "abc\0def\n", 8U);
    char nuls[201];
    memset(nuls, 0, sizeof nuls - 1U);
    nuls[200] = '\n';
    write_all(writers[0], nuls, sizeof nuls);
    (void)close(writers[0]);
    (void)close(writers[1]);
    pp_guest_output_join(reader);
    pp_wait_for_count(&lines.count, 3U);
    PP_CHECK_TEXT(lines.text[0], "abc"
                                 "\xef\xbf\xbd"
                                 "def");
    // 200 replacements of 3 bytes: 170 of them fill the 511 bytes of a piece as far as whole
    // characters go, and 30 follow.
    PP_CHECK_EQUAL(strlen(lines.text[1]), 510U);
    PP_CHECK_EQUAL(strlen(lines.text[2]), 90U);
    for (size_t piece = 1U; piece < 3U; piece++) {
        for (size_t offset = 0U; lines.text[piece][offset] != '\0'; offset += 3U) {
            PP_CHECK(memcmp(lines.text[piece] + offset, "\xef\xbf\xbd", 3U) == 0);
        }
    }
    PP_CHECK_EQUAL(pp_shutdown(), PP_OK);
}

int main(int argc, char **argv) {
    static const pp_test_case_t k_cases[] = {
        {"every_chunking_gives_the_same_lines", every_chunking_gives_the_same_lines},
        {"piece_ends_at_a_character", piece_ends_at_a_character},
        {"carriage_returns", carriage_returns},
        {"empty_lines_and_the_end", empty_lines_and_the_end},
        {"wake_ends_a_reader_whose_pipes_stay_open", wake_ends_a_reader_whose_pipes_stay_open},
        {"woken_reader_takes_at_most_64_kib", woken_reader_takes_at_most_64_kib},
        {"nul_bytes_become_replacements", nul_bytes_become_replacements},
    };
    return pp_run_case(argc, argv, k_cases, PP_CASE_COUNT(k_cases));
}
