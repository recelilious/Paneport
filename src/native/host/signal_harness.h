// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The part of the signal gate's stress harness that runs in signal context: its guest fault
// handler, in signal_harness_handler.c, and the state that the handler shares with
// signal_harness.c (docs/design/signals.md section 9). Test builds only.

#ifndef PANEPORT_HOST_SIGNAL_HARNESS_H
#define PANEPORT_HOST_SIGNAL_HARNESS_H

#include <stdatomic.h>
#include <stddef.h>
#include <stdint.h>

#include "signals/signals.h"

// The harness's pages, in this order: the guard page and the read-only page of the marked
// threads, then the two pages of the reservation.
#define PP_HARNESS_PAGE_COUNT 4U
#define PP_HARNESS_GUARD_PAGE 0U
#define PP_HARNESS_READ_ONLY_PAGE 1U
#define PP_HARNESS_RESERVATION_PAGE 2U
#define PP_HARNESS_RESERVATION_PAGES 2U

// What the handler shares with the rest of the harness: the pages, which the harness maps
// before its threads start and unmaps after they end, and the counts that the handler adds
// to. Part of the harness's process-wide singleton (C-0010 section 3), defined in
// signal_harness_handler.c.
typedef struct pp_harness_faults_t {
    unsigned char *pages;
    size_t page_size;
    _Atomic(uint64_t) handled_by_mark;
    _Atomic(uint64_t) handled_by_address;
    _Atomic(uint64_t) unexpected;
} pp_harness_faults_t;

extern pp_harness_faults_t pp_harness_faults;

// The harness's guest fault handler, in signal context: it counts a fault on its pages by its
// route and skips the faulting instruction, counts a fault that took another route as
// unexpected, and declines a fault outside its pages, which ends the app.
uint32_t pp_harness_handle_fault(pp_guest_fault_t *fault, void *user_data);

#endif // PANEPORT_HOST_SIGNAL_HARNESS_H
