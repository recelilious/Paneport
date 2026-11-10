// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// A bounded queue of fixed-size entries with many producers and one consumer. A push never
// blocks, never allocates and never takes a lock: it claims a slot with one compare-and-swap
// on the queue's tail, copies the entry, and publishes it by advancing the slot's sequence
// number. A full queue refuses the push and counts it. docs/design/native-host.md section 4
// describes the algorithm.

#ifndef PANEPORT_HOST_EVENT_QUEUE_H
#define PANEPORT_HOST_EVENT_QUEUE_H

#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "paneport/common.h"

// The cache-line size of Apple silicon, which keeps the producers' tail and the consumer's
// head apart.
#define PP_QUEUE_CACHE_LINE 128

typedef struct pp_queue_t {
    // The next position that a producer claims.
    _Alignas(PP_QUEUE_CACHE_LINE) _Atomic(uint64_t) tail;
    // The next position that the consumer reads. Only the consumer touches it.
    _Alignas(PP_QUEUE_CACHE_LINE) uint64_t head;
    // How many pushes the queue refused because it was full.
    _Alignas(PP_QUEUE_CACHE_LINE) _Atomic(uint64_t) overflow;
    // Per slot, the position whose entry it may take next (position) or holds (position + 1).
    _Atomic(uint64_t) *sequences;
    // capacity * entry_size bytes.
    unsigned char *entries;
    uint32_t capacity;
    uint32_t entry_size;
} pp_queue_t;

// Allocates the slots. capacity must be a power of two from 2 to 2^20, and entry_size from 1
// to 4096 bytes. Returns PP_OK, PP_E_INVALID_ARGUMENT or PP_E_OUT_OF_MEMORY.
pp_status_t pp_queue_init(pp_queue_t *queue, uint32_t capacity, uint32_t entry_size);

// Frees the slots. No push or pop may run while it does.
void pp_queue_destroy(pp_queue_t *queue);

// Copies entry_size bytes into the queue. Any thread. Returns false, and counts the refusal,
// when the queue is full.
bool pp_queue_push(pp_queue_t *queue, const void *entry);

// Copies the oldest entry out of the queue. The consumer thread only. Returns false when the
// queue holds no published entry.
bool pp_queue_pop(pp_queue_t *queue, void *entry);

// Returns whether the next entry for the consumer is still missing. The consumer thread only.
bool pp_queue_is_empty(const pp_queue_t *queue);

// Returns how many pushes the queue refused. Any thread.
uint64_t pp_queue_overflow_count(const pp_queue_t *queue);

#endif // PANEPORT_HOST_EVENT_QUEUE_H
