// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The memory budget of C-0005 section 2. On iOS the host reads the system; elsewhere, which
// means the macOS test builds, a stub reports values that tests set.

#ifndef PANEPORT_HOST_MEMORY_BUDGET_H
#define PANEPORT_HOST_MEMORY_BUDGET_H

#include <stdint.h>

#include "paneport/memory.h"

// The limit from which the budget reports the Game Mode level: 7,936 MiB, 256 MiB below the
// 8,192 MiB that Game Mode gives a 12 GB iPhone. The margin absorbs the moment between
// reading the available memory and the footprint, whose sum was 8,191 MiB in one published
// measurement. Without Game Mode the limit is about 6,144 MiB. docs/design/native-host.md
// section 7 explains the choice.
#define PP_MEMORY_GAME_MODE_LEVEL_BYTES (UINT64_C(7936) * 1024U * 1024U)

// Fills every member of budget from the two readings. Pure; any thread.
void pp_memory_budget_compute(uint64_t available_bytes, uint64_t footprint_bytes,
                              pp_memory_budget_t *budget);

// Sets the stub's readings. Returns PP_OK, or PP_E_UNSUPPORTED on iOS, which has no stub.
pp_status_t pp_memory_set_stub(uint64_t available_bytes, uint64_t footprint_bytes);

#endif // PANEPORT_HOST_MEMORY_BUDGET_H
