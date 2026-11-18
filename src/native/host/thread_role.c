// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The thread role and the stack bounds of thread_role.h. Everything here may run in a signal
// handler, so it reads atomics and calls only pthread_self, which POSIX lists as
// async-signal-safe, pthread_equal, which compares two thread identities, and Apple's
// pthread_main_np, pthread_get_stackaddr_np and pthread_get_stacksize_np, which read the calling
// thread's own record without a lock. The CTest check of the handler's calls reads this file's
// object with the handler's (tests/native/check_signal_safe_calls.cmake).

#include "thread_role.h"

#include <pthread.h>
#include <stdatomic.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "crash_record.h"

// The threads whose roles the host knows: a process-wide singleton (C-0010 section 3). Each
// identity is stored before its flag turns true, and read only once the flag says so.
static struct {
    _Atomic(pthread_t) event_thread;
    _Atomic(bool) event_thread_known;
#ifdef PANEPORT_TEST_BUILD
    _Atomic(pthread_t) main_thread;
    _Atomic(bool) main_thread_known;
#endif
} s_roles;

void pp_thread_role_set_event_thread(void) {
    atomic_store_explicit(&s_roles.event_thread, pthread_self(), memory_order_relaxed);
    atomic_store_explicit(&s_roles.event_thread_known, true, memory_order_release);
}

void pp_thread_role_clear_event_thread(void) {
    atomic_store_explicit(&s_roles.event_thread_known, false, memory_order_release);
}

bool pp_thread_role_is_event_thread(void) {
    if (!atomic_load_explicit(&s_roles.event_thread_known, memory_order_acquire)) {
        return false;
    }
    return pthread_equal(pthread_self(),
                         atomic_load_explicit(&s_roles.event_thread, memory_order_relaxed)) != 0;
}

#ifdef PANEPORT_TEST_BUILD
void pp_thread_role_set_main_thread(void) {
    atomic_store_explicit(&s_roles.main_thread, pthread_self(), memory_order_relaxed);
    atomic_store_explicit(&s_roles.main_thread_known, true, memory_order_release);
}
#endif

bool pp_thread_role_is_main_thread(void) {
#ifdef PANEPORT_TEST_BUILD
    if (!atomic_load_explicit(&s_roles.main_thread_known, memory_order_acquire)) {
        return false;
    }
    return pthread_equal(pthread_self(),
                         atomic_load_explicit(&s_roles.main_thread, memory_order_relaxed)) != 0;
#else
    return pthread_main_np() == 1;
#endif
}

uint32_t pp_host_thread_role(void) {
    if (pp_thread_role_is_event_thread()) {
        return PP_THREAD_ROLE_EVENT;
    }
    if (pp_thread_role_is_main_thread()) {
        return PP_THREAD_ROLE_MAIN;
    }
    return PP_THREAD_ROLE_OTHER;
}

bool pp_thread_stack_bounds(uintptr_t *low, uintptr_t *high) {
    const pthread_t self = pthread_self();
    const uintptr_t top = (uintptr_t)pthread_get_stackaddr_np(self);
    const size_t size = pthread_get_stacksize_np(self);
    if (top == 0U || size == 0U || size > top) {
        return false;
    }
    *low = top - size;
    *high = top;
    return true;
}
