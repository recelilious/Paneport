// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The state machine of the JIT request protocol (C-0006 sections 3 to 5), with no thread, no
// lock and no system call: every input carries the time at which it happened, so the tests
// drive every transition and both timeouts with a clock of their own. Each input makes at most
// one transition, so the provider (jit_provider.c), which compares the state before and after
// each input, reports every change with an event. docs/design/jit.md section 3 draws it.

#ifndef PANEPORT_HOST_JIT_MACHINE_H
#define PANEPORT_HOST_JIT_MACHINE_H

#include <stdbool.h>
#include <stdint.h>

#include "paneport/jit.h"

// The number of states of pp_jit_state_t.
#define PP_JIT_STATE_COUNT 7U

// The size of the regions that the pool is requested in, in MiB: the reach of an arm64 B or BL
// instruction, so that code inside one region can always branch directly to any other code in
// it. The last region takes the rest of the pool. jit.md section 4 gives the reasons.
#define PP_JIT_REGION_MIB 128U

// The most regions a pool takes: the largest pool in regions of PP_JIT_REGION_MIB.
#define PP_JIT_MAX_REGIONS ((PP_JIT_POOL_MAX_MIB + PP_JIT_REGION_MIB - 1U) / PP_JIT_REGION_MIB)

// One MiB, in bytes.
#define PP_JIT_MIB (UINT64_C(1024) * 1024U)

// The two debugger flags of C-0006 section 3 step 1.
typedef struct pp_jit_flags_t {
    bool debugged_since_launch; // CS_DEBUGGED, which XNU never clears once set.
    bool attached_now;          // P_TRACED.
} pp_jit_flags_t;

typedef struct pp_jit_machine_t {
    pp_jit_state_t state;
    pp_jit_failure_t failure;
    pp_jit_flags_t flags; // The last reading.
    // The app said that this launch opens the enabler, and has not taken it back.
    bool enabler_opened;
    // A reading since the enabler opened found P_TRACED clear, so a debugger attached after it
    // attached "afterwards" (C-0006 section 3 step 2).
    bool untraced_since_enabler;
    uint32_t requested_mib;
    uint32_t pool_mib;
    uint32_t regions_planned;
    uint32_t regions_mapped;
    bool detach_sent; // Command 0 was sent.
    uint64_t state_entered_ms;
    uint64_t requesting_entered_ms;
    uint64_t detach_sent_ms;
    uint64_t durations_ms[PP_JIT_STATE_COUNT]; // Per state, the time spent in it once left.
    uint64_t authorization_ms;
} pp_jit_machine_t;

// Starts the machine in not_debugged, with no reading yet.
void pp_jit_machine_init(pp_jit_machine_t *machine, uint64_t now_ms);

// Applies a reading of the flags: the broker-recognition rule in not_debugged, the broker's
// leaving in debugger_attached and requesting, and the detach in waiting_detach.
void pp_jit_machine_apply_flags(pp_jit_machine_t *machine, pp_jit_flags_t flags, uint64_t now_ms);

// Returns whether the state takes the notice that the app opens the enabler (opened), or that
// iOS refused to open it: not_debugged takes both, debugger_attached only the refusal.
bool pp_jit_machine_takes_enabler(const pp_jit_machine_t *machine, bool opened);

// Records that the app opens the enabler (opened), with the reading of the flags at that
// moment, or that iOS refused to open it. A debugger attached at that moment is foreign until
// it detaches. Taking it back in debugger_attached returns to not_debugged, because the
// attached debugger is then not the broker. Returns false, and changes nothing, in a state that
// does not take it: pp_jit_set_enabler_opened() lists them.
bool pp_jit_machine_set_enabler(pp_jit_machine_t *machine, bool opened, pp_jit_flags_t flags,
                                uint64_t now_ms);

// Starts a pool request of requested_mib, with the memory limit read now. Moves to requesting,
// or to failed when one eighth of the limit is below the minimum. Returns false, and changes
// nothing, in any state but debugger_attached.
bool pp_jit_machine_request(pp_jit_machine_t *machine, uint32_t requested_mib, uint64_t limit_bytes,
                            uint64_t now_ms);

// Records one more region prepared and mapped. In requesting only.
void pp_jit_machine_region_mapped(pp_jit_machine_t *machine, uint64_t now_ms);

// Records that command 0 is being sent, which starts the detach timeout. In requesting, with
// the pool complete, only.
void pp_jit_machine_detach_sent(pp_jit_machine_t *machine, uint64_t now_ms);

// Records that command 0 returned: requesting becomes waiting_detach.
void pp_jit_machine_detach_returned(pp_jit_machine_t *machine, uint64_t now_ms);

// Records the probe's verdict: verifying becomes ready or failed.
void pp_jit_machine_verified(pp_jit_machine_t *machine, bool passed, uint64_t now_ms);

// Fails the request for a reason, from any state but ready and failed.
void pp_jit_machine_fail(pp_jit_machine_t *machine, pp_jit_failure_t failure, uint64_t now_ms);

// Applies the timeouts of C-0006 section 3 at now_ms.
void pp_jit_machine_tick(pp_jit_machine_t *machine, uint64_t now_ms);

// Returns whether a debugger is attached that the host does not take for the broker.
bool pp_jit_machine_foreign(const pp_jit_machine_t *machine);

// Fills every member of status, struct_size included, at now_ms.
void pp_jit_machine_status(const pp_jit_machine_t *machine, uint64_t now_ms,
                           pp_jit_status_t *status);

// The pool size of C-0006 section 4: requested_mib limited to the minimum and the maximum, and
// to one eighth of limit_bytes in whole MiB. Returns 0 when that eighth is below the minimum.
uint32_t pp_jit_pool_size_mib(uint32_t requested_mib, uint64_t limit_bytes);

// The number of regions of a pool of pool_mib, and the size in bytes of its region index.
uint32_t pp_jit_region_count(uint32_t pool_mib);
uint64_t pp_jit_region_bytes(uint32_t pool_mib, uint32_t index);

// The name of a state as C-0006 section 5 spells it, such as "waiting_detach", and a sentence
// for a failure's reason. Both return text for unknown values too.
const char *pp_jit_state_name(pp_jit_state_t state);
const char *pp_jit_failure_text(pp_jit_failure_t failure);

#endif // PANEPORT_HOST_JIT_MACHINE_H
