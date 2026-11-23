// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S4.3.8: the splitter of a Windows program's output (guest_output.h) gives the same lines
// however the bytes arrive: whole, in chunks of every size, or cut at random.

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include "fake_wine_fork.h"
#include "guest_output.h"
#include "pp_check.h"

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
        state = state * 1103515245U + 12345U;
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
    memcpy(text + 509U, "\xf0\x9f\x98\x80\n", 5U);
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
    memcpy(full + PP_GUEST_LINE_MAX, "\r\n", 2U);
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

int main(int argc, char **argv) {
    static const pp_test_case_t k_cases[] = {
        {"every_chunking_gives_the_same_lines", every_chunking_gives_the_same_lines},
        {"piece_ends_at_a_character", piece_ends_at_a_character},
        {"carriage_returns", carriage_returns},
        {"empty_lines_and_the_end", empty_lines_and_the_end},
    };
    return pp_run_case(argc, argv, k_cases, PP_CASE_COUNT(k_cases));
}
