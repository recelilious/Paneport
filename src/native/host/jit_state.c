// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "jit_state.h"

#include <stdatomic.h>

#include "jit/jit_provider.h"

#ifdef PANEPORT_TEST_BUILD
// The test builds' own answer: a process-wide singleton (C-0010 section 3).
static _Atomic(bool) s_ready_for_tests;
#endif

bool pp_jit_state_is_ready(void) {
#ifdef PANEPORT_TEST_BUILD
    if (atomic_load_explicit(&s_ready_for_tests, memory_order_acquire)) {
        return true;
    }
#endif
    return pp_jit_is_ready();
}

#ifdef PANEPORT_TEST_BUILD
void pp_jit_state_set_ready_for_tests(bool ready) {
    atomic_store_explicit(&s_ready_for_tests, ready, memory_order_release);
}
#endif
