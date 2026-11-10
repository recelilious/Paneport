// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// S1.2.2: the bounded queue with many producers and one consumer.

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdint.h>

#include "event_queue.h"
#include "pp_check.h"
#include "test_support.h"

typedef struct test_entry_t {
    uint32_t producer;
    uint32_t sequence;
    uint64_t check;
} test_entry_t;

// A value that depends on every bit of the entry, so a torn copy shows.
static uint64_t check_of(uint32_t producer, uint32_t sequence) {
    return ((((uint64_t)producer << 32U) | sequence) * UINT64_C(0x9E3779B97F4A7C15)) ^
           UINT64_C(0xA5A5A5A5A5A5A5A5);
}

static test_entry_t entry_of(uint32_t producer, uint32_t sequence) {
    const test_entry_t entry = {
        .producer = producer,
        .sequence = sequence,
        .check = check_of(producer, sequence),
    };
    return entry;
}

static void full_queue_refuses(void) {
    pp_queue_t queue;
    PP_CHECK_EQUAL(pp_queue_init(&queue, 8U, (uint32_t)sizeof(test_entry_t)), PP_OK);
    PP_CHECK(pp_queue_is_empty(&queue));

    for (uint32_t sequence = 0; sequence < 8U; sequence++) {
        const test_entry_t entry = entry_of(0U, sequence);
        PP_CHECK(pp_queue_push(&queue, &entry));
    }
    uint64_t refused = 0U;
    for (uint32_t attempt = 0; attempt < 5U; attempt++) {
        const test_entry_t entry = entry_of(1U, attempt);
        PP_CHECK(!pp_queue_push(&queue, &entry));
        refused++;
    }
    PP_CHECK_EQUAL(pp_queue_overflow_count(&queue), refused);

    test_entry_t entry;
    for (uint32_t sequence = 0; sequence < 8U; sequence++) {
        PP_CHECK(pp_queue_pop(&queue, &entry));
        PP_CHECK_EQUAL(entry.producer, 0U);
        PP_CHECK_EQUAL(entry.sequence, sequence);
        PP_CHECK_EQUAL(entry.check, check_of(0U, sequence));
    }
    PP_CHECK(!pp_queue_pop(&queue, &entry));
    PP_CHECK(pp_queue_is_empty(&queue));

    // Room again, and the count only grows with refusals.
    entry = entry_of(2U, 0U);
    PP_CHECK(pp_queue_push(&queue, &entry));
    PP_CHECK(!pp_queue_is_empty(&queue));
    PP_CHECK_EQUAL(pp_queue_overflow_count(&queue), refused);
    pp_queue_destroy(&queue);

    PP_CHECK_EQUAL(pp_queue_init(&queue, 3U, 8U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_queue_init(&queue, 1U, 8U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_queue_init(&queue, 8U, 0U), PP_E_INVALID_ARGUMENT);
    PP_CHECK_EQUAL(pp_queue_init(NULL, 8U, 8U), PP_E_INVALID_ARGUMENT);
}

enum { PRODUCERS = 8, ENTRIES_PER_PRODUCER = 125000, CAPACITY = 1024 };

static pp_queue_t s_queue;
static _Atomic(uint64_t) s_refused;

static void *produce(void *argument) {
    const uint32_t producer = pp_thread_number(argument);
    uint64_t refused = 0U;
    for (uint32_t sequence = 0; sequence < (uint32_t)ENTRIES_PER_PRODUCER; sequence++) {
        const test_entry_t entry = entry_of(producer, sequence);
        while (!pp_queue_push(&s_queue, &entry)) {
            refused++;
            (void)sched_yield();
        }
    }
    (void)atomic_fetch_add(&s_refused, refused);
    return NULL;
}

// Eight producers and one consumer move 1,000,000 entries: each arrives once, intact and in
// order per producer, and the overflow count equals the pushes the full queue refused.
static void many_producers(void) {
    PP_CHECK_EQUAL(pp_queue_init(&s_queue, (uint32_t)CAPACITY, (uint32_t)sizeof(test_entry_t)),
                   PP_OK);

    pthread_t producers[PRODUCERS];
    for (uint32_t producer = 0; producer < (uint32_t)PRODUCERS; producer++) {
        producers[producer] = pp_start_numbered_thread(produce, producer);
    }

    uint32_t next[PRODUCERS] = {0};
    uint64_t received = 0U;
    const uint64_t expected = (uint64_t)PRODUCERS * (uint64_t)ENTRIES_PER_PRODUCER;
    test_entry_t entry;
    while (received < expected) {
        if (!pp_queue_pop(&s_queue, &entry)) {
            (void)sched_yield();
            continue;
        }
        PP_CHECK(entry.producer < (uint32_t)PRODUCERS);
        PP_CHECK_EQUAL(entry.sequence, next[entry.producer]);
        PP_CHECK_EQUAL(entry.check, check_of(entry.producer, entry.sequence));
        next[entry.producer]++;
        received++;
    }

    for (size_t producer = 0; producer < (size_t)PRODUCERS; producer++) {
        pp_join_thread(producers[producer]);
        PP_CHECK_EQUAL(next[producer], ENTRIES_PER_PRODUCER);
    }
    PP_CHECK(!pp_queue_pop(&s_queue, &entry));
    PP_CHECK_EQUAL(pp_queue_overflow_count(&s_queue), atomic_load(&s_refused));
    pp_queue_destroy(&s_queue);
}

int main(int argc, char **argv) {
    static const pp_test_case_t cases[] = {
        {"full_queue_refuses", full_queue_refuses},
        {"many_producers", many_producers},
    };
    return pp_run_case(argc, argv, cases, PP_CASE_COUNT(cases));
}
