// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "event_queue.h"

#include <stdlib.h>
#include <string.h>

#define PP_QUEUE_MAX_CAPACITY (UINT32_C(1) << 20U)
#define PP_QUEUE_MAX_ENTRY_SIZE UINT32_C(4096)

static unsigned char *entry_at(const pp_queue_t *queue, uint64_t position) {
    const uint64_t index = position & (uint64_t)(queue->capacity - 1U);
    return queue->entries + ((size_t)index * queue->entry_size);
}

static _Atomic(uint64_t) *sequence_at(const pp_queue_t *queue, uint64_t position) {
    return &queue->sequences[position & (uint64_t)(queue->capacity - 1U)];
}

pp_status_t pp_queue_init(pp_queue_t *queue, uint32_t capacity, uint32_t entry_size) {
    if (queue == NULL || capacity < 2U || capacity > PP_QUEUE_MAX_CAPACITY ||
        (capacity & (capacity - 1U)) != 0U || entry_size == 0U ||
        entry_size > PP_QUEUE_MAX_ENTRY_SIZE) {
        return PP_E_INVALID_ARGUMENT;
    }

    _Atomic(uint64_t) *sequences = calloc(capacity, sizeof *sequences);
    unsigned char *entries = calloc(capacity, entry_size);
    if (sequences == NULL || entries == NULL) {
        free(sequences);
        free(entries);
        return PP_E_OUT_OF_MEMORY;
    }

    // Slot i first takes the entry of position i.
    for (uint32_t index = 0; index < capacity; index++) {
        atomic_init(&sequences[index], (uint64_t)index);
    }

    atomic_init(&queue->tail, 0U);
    queue->head = 0U;
    atomic_init(&queue->overflow, 0U);
    queue->sequences = sequences;
    queue->entries = entries;
    queue->capacity = capacity;
    queue->entry_size = entry_size;
    return PP_OK;
}

void pp_queue_destroy(pp_queue_t *queue) {
    if (queue == NULL) {
        return;
    }
    free(queue->sequences);
    free(queue->entries);
    queue->sequences = NULL;
    queue->entries = NULL;
    queue->capacity = 0U;
    queue->entry_size = 0U;
}

bool pp_queue_push(pp_queue_t *queue, const void *entry) {
    uint64_t position = atomic_load_explicit(&queue->tail, memory_order_relaxed);
    for (;;) {
        const uint64_t sequence =
            atomic_load_explicit(sequence_at(queue, position), memory_order_acquire);
        // Zero: the slot is free for this position. Negative: it still holds the entry of
        // the previous lap, so the queue is full. Positive: another producer took this
        // position, so read the tail again.
        const int64_t difference = (int64_t)(sequence - position);
        if (difference == 0) {
            if (atomic_compare_exchange_weak_explicit(&queue->tail, &position, position + 1U,
                                                      memory_order_relaxed, memory_order_relaxed)) {
                break;
            }
        } else if (difference < 0) {
            (void)atomic_fetch_add_explicit(&queue->overflow, 1U, memory_order_relaxed);
            return false;
        } else {
            position = atomic_load_explicit(&queue->tail, memory_order_relaxed);
        }
    }

    memcpy(entry_at(queue, position), entry, queue->entry_size);
    atomic_store_explicit(sequence_at(queue, position), position + 1U, memory_order_release);
    return true;
}

bool pp_queue_pop(pp_queue_t *queue, void *entry) {
    const uint64_t position = queue->head;
    _Atomic(uint64_t) *sequence = sequence_at(queue, position);
    if (atomic_load_explicit(sequence, memory_order_acquire) != position + 1U) {
        return false;
    }

    memcpy(entry, entry_at(queue, position), queue->entry_size);
    // The slot takes the entry of the same index in the next lap.
    atomic_store_explicit(sequence, position + queue->capacity, memory_order_release);
    queue->head = position + 1U;
    return true;
}

bool pp_queue_is_empty(const pp_queue_t *queue) {
    const uint64_t position = queue->head;
    return atomic_load_explicit(sequence_at(queue, position), memory_order_acquire) !=
           position + 1U;
}

uint64_t pp_queue_overflow_count(const pp_queue_t *queue) {
    return atomic_load_explicit(&queue->overflow, memory_order_relaxed);
}
