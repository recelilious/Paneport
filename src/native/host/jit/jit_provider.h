// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The JIT provider: the exports of paneport/jit.h, and what the rest of the host needs from the
// pool. It drives the state machine (jit_machine.h) with the readings and the commands of a
// backend (jit_platform.h) on two threads of its own: the supervisor, "paneport.jit", which
// reads the flags while the host waits for the broker and its detach, applies the timeouts and
// runs the probe; and the request, "paneport.jit-pool", which sends the commands, maps each
// region and registers it with the signal dispatcher. docs/design/jit.md describes it.

#ifndef PANEPORT_HOST_JIT_PROVIDER_H
#define PANEPORT_HOST_JIT_PROVIDER_H

#include <stdbool.h>
#include <stdint.h>

#include "jit/jit_map.h"
#include "jit/jit_platform.h"
#include "paneport/jit.h"

// Stops the supervisor and waits for it. pp_shutdown calls it before the event thread stops,
// so that the provider's last events still arrive. The request thread may be inside a command
// that never returns, so nobody waits for it. From this call on it sends no command and keeps
// no region that a command returns; it gives back the region's alias and ends.
void pp_jit_provider_stop(void);

// Returns whether the state is ready: the precondition of pp_session_start (C-0005 section 2).
// Any thread.
bool pp_jit_is_ready(void);

// Copies the pool's regions, the registry that the Wine bridge hands to the translator
// (C-0005 section 3): up to capacity of them into regions, and their number into *count. Any
// thread, in any state; in the ready state the list no longer changes.
void pp_jit_pool_copy(pp_jit_pool_region_t *regions, uint32_t capacity, uint32_t *count);

#ifdef PANEPORT_TEST_BUILD
// Test builds only: makes the provider use replacement, which must outlive the process, instead
// of the backend of the build. Call it before the first call of the JIT group.
void pp_jit_provider_use_platform(const pp_jit_platform_t *replacement);

// Test builds only: wakes the supervisor at once, as a test that moved its clock needs.
void pp_jit_provider_wake(void);

// Test builds only: makes the request thread call callback, with the region's index, each time
// it has kept a region and before it asks whether the next command may go out, so that a test
// can stop it between two commands.
void pp_jit_provider_on_region_kept(void (*callback)(uint32_t index));

// Test builds only: makes pp_jit_get_state report a copy of status, whose struct_size must be
// sizeof(pp_jit_status_t), instead of the provider's own status; NULL ends it. The state of
// the provider does not change. pp_test_jit_plant_status uses it.
void pp_jit_provider_plant_status(const pp_jit_status_t *status);
#endif

#endif // PANEPORT_HOST_JIT_PROVIDER_H
