// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The writers of the routing tables (docs/design/signals.md section 4): thread marks, JIT pool
// ranges, guest reservations, the probe mode, and the guest and probe handlers. Writers
// serialize on a mutex per table and publish with atomic stores in the order that the readers
// of signal_handler.c expect. Nothing here runs in a signal handler.

#include <pthread.h>
#include <sched.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "error.h"
#include "signal_state.h"
#include "signals.h"

// The tags that tell the handles of the two range tables apart.
#define PP_RANGE_TAG_POOL 1U
#define PP_RANGE_TAG_RESERVATION 2U

// The registry's locks and the key that frees a thread's mark when the thread ends: a
// process-wide singleton (C-0010 section 3), beside pp_signal_state.
static struct {
    pthread_mutex_t threads;  // Serializes the writers of the thread table.
    pthread_mutex_t ranges;   // Serializes the writers of both range tables.
    pthread_mutex_t handlers; // Serializes the writers of both handler slots.
    pthread_once_t key_once;
    pthread_key_t key;
    int key_result;
} s_registry = {
    .threads = PTHREAD_MUTEX_INITIALIZER,
    .ranges = PTHREAD_MUTEX_INITIALIZER,
    .handlers = PTHREAD_MUTEX_INITIALIZER,
    .key_once = PTHREAD_ONCE_INIT,
};

// Frees a thread's slot and the stack that its mark brought. Runs on the marked thread itself,
// from pp_signals_unmark_thread or from the key's destructor when the thread ends; a pthread_t
// may be reused by a later thread, so the slot must not outlive its thread.
static void release_thread_slot(pp_signal_thread_slot_t *slot) {
    pp_signal_stack_release(slot->stack, slot->stack_size);
    slot->stack = NULL;
    slot->stack_size = 0U;
    (void)pthread_mutex_lock(&s_registry.threads);
    atomic_store_explicit(&slot->thread, 0U, memory_order_release);
    atomic_store_explicit(&slot->mark, PP_THREAD_MARK_NONE, memory_order_relaxed);
    (void)pthread_mutex_unlock(&s_registry.threads);
}

static void release_at_thread_end(void *slot) {
    if (slot != NULL) {
        release_thread_slot(slot);
    }
}

static void create_key(void) {
    s_registry.key_result = pthread_key_create(&s_registry.key, release_at_thread_end);
}

// Finds the calling thread's slot, or claims a free one. Called with the thread table locked.
static pp_signal_thread_slot_t *find_thread_slot(uintptr_t self, bool *claimed) {
    pp_signal_thread_slot_t *free_slot = NULL;
    for (uint32_t index = 0U; index < PP_SIGNAL_MARKED_THREADS; index++) {
        pp_signal_thread_slot_t *slot = &pp_signal_state.threads[index];
        const uintptr_t thread = atomic_load_explicit(&slot->thread, memory_order_relaxed);
        if (thread == self) {
            *claimed = false;
            return slot;
        }
        if (thread == 0U && free_slot == NULL) {
            free_slot = slot;
        }
    }
    *claimed = free_slot != NULL;
    return free_slot;
}

pp_status_t pp_signals_mark_thread(uint32_t mark) {
    if (mark != PP_THREAD_MARK_GUEST && mark != PP_THREAD_MARK_TRANSLATOR) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "A thread is marked as a guest (1) or translator (2) thread, not %u.",
                            (unsigned)mark);
    }
    if (pthread_once(&s_registry.key_once, create_key) != 0 || s_registry.key_result != 0) {
        return pp_error_set(PP_E_SYSTEM, "The key of the marked threads could not be created.");
    }

    const uintptr_t self = (uintptr_t)pthread_self();
    bool claimed = false;
    (void)pthread_mutex_lock(&s_registry.threads);
    pp_signal_thread_slot_t *slot = find_thread_slot(self, &claimed);
    if (slot == NULL) {
        (void)pthread_mutex_unlock(&s_registry.threads);
        return pp_error_set(PP_E_OUT_OF_MEMORY,
                            "The table of marked threads is full: it holds %u threads.",
                            PP_SIGNAL_MARKED_THREADS);
    }
    atomic_store_explicit(&slot->mark, mark, memory_order_relaxed);
    if (claimed) {
        atomic_store_explicit(&slot->in_handler, PP_SIGNAL_IN_NONE, memory_order_relaxed);
        slot->stack = NULL;
        slot->stack_size = 0U;
        atomic_store_explicit(&slot->thread, self, memory_order_release);
        const uint32_t index = (uint32_t)(slot - pp_signal_state.threads);
        if (index >= atomic_load_explicit(&pp_signal_state.threads_used, memory_order_relaxed)) {
            atomic_store_explicit(&pp_signal_state.threads_used, index + 1U, memory_order_release);
        }
    }
    (void)pthread_mutex_unlock(&s_registry.threads);
    if (!claimed) {
        return PP_OK;
    }

    // The stack and the key belong to this thread alone, so they need no lock.
    pp_status_t status = pp_signal_stack_ensure(&slot->stack, &slot->stack_size);
    if (status == PP_OK && pthread_setspecific(s_registry.key, slot) != 0) {
        status = pp_error_set(PP_E_OUT_OF_MEMORY, "The thread's mark could not be kept.");
    }
    if (status != PP_OK) {
        release_thread_slot(slot);
    }
    return status;
}

pp_status_t pp_signals_unmark_thread(void) {
    if (pthread_once(&s_registry.key_once, create_key) != 0 || s_registry.key_result != 0) {
        return PP_OK;
    }
    pp_signal_thread_slot_t *slot = pthread_getspecific(s_registry.key);
    if (slot != NULL) {
        (void)pthread_setspecific(s_registry.key, NULL);
        release_thread_slot(slot);
    }
    return PP_OK;
}

// The handle of a range: the slot's index plus one in bits 0 to 15, the table's tag in bits 16
// to 23 and the slot's generation in bits 32 to 63, so that it is never 0 and a handle of a
// removed range finds no slot.
static uint64_t range_handle(uint32_t tag, uint32_t index, uint32_t generation) {
    return ((uint64_t)generation << 32U) | ((uint64_t)tag << 16U) | (uint64_t)(index + 1U);
}

static void publish_range(pp_signal_range_slot_t *slot, uint64_t start, uint64_t end) {
    const uint32_t sequence = atomic_load_explicit(&slot->sequence, memory_order_relaxed);
    atomic_store_explicit(&slot->sequence, sequence + 1U, memory_order_relaxed);
    atomic_thread_fence(memory_order_release);
    atomic_store_explicit(&slot->start, start, memory_order_relaxed);
    atomic_store_explicit(&slot->end, end, memory_order_relaxed);
    atomic_store_explicit(&slot->sequence, sequence + 2U, memory_order_release);
}

static pp_status_t add_range(pp_signal_range_slot_t *slots, uint32_t capacity,
                             _Atomic(uint32_t) *used, uint32_t tag, uint64_t start, uint64_t size,
                             uint64_t *handle) {
    if (handle == NULL || start == 0U || size == 0U || start > UINT64_MAX - size) {
        return pp_error_set(PP_E_INVALID_ARGUMENT,
                            "A range needs a handle, a start above 0 and a size above 0 that "
                            "ends inside the address space.");
    }
    (void)pthread_mutex_lock(&s_registry.ranges);
    for (uint32_t index = 0U; index < capacity; index++) {
        pp_signal_range_slot_t *slot = &slots[index];
        if (atomic_load_explicit(&slot->end, memory_order_relaxed) != 0U) {
            continue;
        }
        slot->generation = slot->generation == UINT32_MAX ? 1U : slot->generation + 1U;
        publish_range(slot, start, start + size);
        if (index >= atomic_load_explicit(used, memory_order_relaxed)) {
            atomic_store_explicit(used, index + 1U, memory_order_release);
        }
        *handle = range_handle(tag, index, slot->generation);
        (void)pthread_mutex_unlock(&s_registry.ranges);
        return PP_OK;
    }
    (void)pthread_mutex_unlock(&s_registry.ranges);
    return pp_error_set(PP_E_OUT_OF_MEMORY, "The table of %s is full: it holds %u ranges.",
                        tag == PP_RANGE_TAG_POOL ? "JIT pool ranges" : "guest reservations",
                        (unsigned)capacity);
}

static pp_status_t remove_range(pp_signal_range_slot_t *slots, uint32_t capacity, uint32_t tag,
                                uint64_t handle) {
    const uint32_t index = (uint32_t)(handle & 0xFFFFU) - 1U;
    const uint32_t generation = (uint32_t)(handle >> 32U);
    (void)pthread_mutex_lock(&s_registry.ranges);
    if (((handle >> 16U) & 0xFFU) == tag && index < capacity &&
        slots[index].generation == generation &&
        atomic_load_explicit(&slots[index].end, memory_order_relaxed) != 0U) {
        publish_range(&slots[index], 0U, 0U);
        (void)pthread_mutex_unlock(&s_registry.ranges);
        return PP_OK;
    }
    (void)pthread_mutex_unlock(&s_registry.ranges);
    return pp_error_set(PP_E_INVALID_ARGUMENT, "The handle names no registered %s.",
                        tag == PP_RANGE_TAG_POOL ? "JIT pool range" : "guest reservation");
}

pp_status_t pp_signals_add_pool_range(uint64_t start, uint64_t size, uint64_t *handle) {
    return add_range(pp_signal_state.pool, PP_SIGNAL_POOL_RANGES, &pp_signal_state.pool_used,
                     PP_RANGE_TAG_POOL, start, size, handle);
}

pp_status_t pp_signals_remove_pool_range(uint64_t handle) {
    return remove_range(pp_signal_state.pool, PP_SIGNAL_POOL_RANGES, PP_RANGE_TAG_POOL, handle);
}

pp_status_t pp_signals_add_reservation(uint64_t start, uint64_t size, uint64_t *handle) {
    return add_range(pp_signal_state.reservations, PP_SIGNAL_RESERVATIONS,
                     &pp_signal_state.reservations_used, PP_RANGE_TAG_RESERVATION, start, size,
                     handle);
}

pp_status_t pp_signals_remove_reservation(uint64_t handle) {
    return remove_range(pp_signal_state.reservations, PP_SIGNAL_RESERVATIONS,
                        PP_RANGE_TAG_RESERVATION, handle);
}

pp_status_t pp_signals_set_probe_mode(uint8_t on) {
    if (on > 1U) {
        return pp_error_set(PP_E_INVALID_ARGUMENT, "The probe mode is 0 or 1, not %u.",
                            (unsigned)on);
    }
    atomic_store_explicit(&pp_signal_state.probe_mode, on, memory_order_release);
    return PP_OK;
}

// Waits until no caller is left in a box. A handler must return, so this ends: a handler that
// faults on a marked thread ends the app, and elsewhere its fault is routed again.
static void wait_for_callers(pp_signal_handler_box_t *box) {
    while (atomic_load_explicit(&box->callers, memory_order_seq_cst) != 0U) {
        (void)sched_yield();
    }
}

// Replaces a slot's handler: the box that is not active takes the new handler once callers
// that came too late have left it, becomes active, and the old box empties before the caller
// returns.
static void replace_handler(pp_signal_handler_slot_t *slot, pp_guest_fault_handler_t handler,
                            void *user_data) {
    (void)pthread_mutex_lock(&s_registry.handlers);
    const uint32_t current = atomic_load_explicit(&slot->active, memory_order_relaxed) & 1U;
    const uint32_t next = current ^ 1U;
    wait_for_callers(&slot->boxes[next]);
    atomic_store_explicit(&slot->boxes[next].handler, handler, memory_order_relaxed);
    atomic_store_explicit(&slot->boxes[next].user_data, handler != NULL ? user_data : NULL,
                          memory_order_relaxed);
    atomic_store_explicit(&slot->active, next, memory_order_seq_cst);
    wait_for_callers(&slot->boxes[current]);
    (void)pthread_mutex_unlock(&s_registry.handlers);
}

pp_status_t pp_signals_set_guest_handler(pp_guest_fault_handler_t handler, void *user_data) {
    replace_handler(&pp_signal_state.guest, handler, user_data);
    return PP_OK;
}

pp_status_t pp_signals_set_probe_handler(pp_guest_fault_handler_t handler, void *user_data) {
    replace_handler(&pp_signal_state.probe, handler, user_data);
    return PP_OK;
}

bool pp_signals_has_guest_handler(void) {
    (void)pthread_mutex_lock(&s_registry.handlers);
    const uint32_t active =
        atomic_load_explicit(&pp_signal_state.guest.active, memory_order_relaxed) & 1U;
    const bool set = atomic_load_explicit(&pp_signal_state.guest.boxes[active].handler,
                                          memory_order_relaxed) != NULL;
    (void)pthread_mutex_unlock(&s_registry.handlers);
    return set;
}

void pp_signals_read_counters(pp_signal_counters_t *counters) {
    counters->struct_size = (uint32_t)sizeof(pp_signal_counters_t);
    counters->runtime = atomic_load_explicit(&pp_signal_state.runtime_count, memory_order_relaxed);
    counters->guest_handled =
        atomic_load_explicit(&pp_signal_state.guest_handled_count, memory_order_relaxed);
    counters->guest_declined =
        atomic_load_explicit(&pp_signal_state.guest_declined_count, memory_order_relaxed);
    counters->probe_handled =
        atomic_load_explicit(&pp_signal_state.probe_handled_count, memory_order_relaxed);
    counters->probe_declined =
        atomic_load_explicit(&pp_signal_state.probe_declined_count, memory_order_relaxed);
}
