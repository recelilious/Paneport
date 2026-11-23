// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The JIT state as pp_session_start reads it (C-0005 section 2, C-0006 section 5): M3.1's state
// machine answers in every build (pp_jit_is_ready), and the test builds also report ready once a
// test sets it with pp_test_jit_set_ready, as the macOS harness does, whose FEX allocates its
// code memory with MAP_JIT instead of the pool (docs/design/runtime-macos-harness.md
// section 2.1).

#ifndef PANEPORT_HOST_JIT_STATE_H
#define PANEPORT_HOST_JIT_STATE_H

#include <stdbool.h>

// Returns whether the JIT state is ready, so that a session may start. Any thread.
bool pp_jit_state_is_ready(void);

#ifdef PANEPORT_TEST_BUILD
// Test builds only: sets what pp_jit_state_is_ready reports besides M3.1's state. Any thread.
void pp_jit_state_set_ready_for_tests(bool ready);
#endif

#endif // PANEPORT_HOST_JIT_STATE_H
