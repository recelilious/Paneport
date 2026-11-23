// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The macOS backend of the JIT provider, for the test builds and the runtime harness, where no
// StikDebug exists. A simulated broker stands in for it: it attaches when the app opens the
// enabler, sets the debugged flag, which then stays set as CS_DEBUGGED does, maps each region
// as anonymous memory that is readable and executable and never writable, as the debugger
// hands a region over on iOS, and detaches on command 0. The provider's own code, the alias,
// the dispatcher's ranges and the probe, is the same on both platforms. jit.md section 2 says
// why the regions are not MAP_JIT memory. CMake builds this file for macOS only.

#include "jit/jit_platform.h"

#include <mach/mach.h>
#include <mach/vm_map.h>
#include <stdatomic.h>
#include <stddef.h>
#include <stdint.h>
#include <sys/mman.h>

// The simulated broker: a process-wide singleton (C-0010 section 3).
static struct {
    _Atomic(bool) debugged;
    _Atomic(bool) attached;
} s_broker;

static bool read_flags(void *context, pp_jit_flags_t *flags) {
    (void)context;
    flags->debugged_since_launch = atomic_load_explicit(&s_broker.debugged, memory_order_acquire);
    flags->attached_now = atomic_load_explicit(&s_broker.attached, memory_order_acquire);
    return true;
}

static void enabler_opened(void *context) {
    (void)context;
    atomic_store_explicit(&s_broker.debugged, true, memory_order_release);
    atomic_store_explicit(&s_broker.attached, true, memory_order_release);
}

static uint64_t map_region(uint64_t length) {
    // Readable and executable from the start, so no page is ever writable and executable; the
    // maximum protection of anonymous memory keeps the write permission that the alias takes.
    void *region = mmap(NULL, (size_t)length, PROT_READ | PROT_EXEC, MAP_PRIVATE | MAP_ANON, -1, 0);
    return region == MAP_FAILED ? 0U : (uint64_t)(uintptr_t)region;
}

#ifdef PANEPORT_TEST_BUILD
uint64_t pp_jit_macos_prepare_region(uint64_t length) { return map_region(length); }

void pp_jit_macos_release_region(uint64_t address, uint64_t length) {
    if (address != 0U && length != 0U) {
        (void)vm_deallocate(mach_task_self(), (vm_address_t)address, (vm_size_t)length);
    }
}
#endif

static uint64_t prepare_region(void *context, uint64_t length) {
    (void)context;
    return map_region(length);
}

static void detach(void *context) {
    (void)context;
    atomic_store_explicit(&s_broker.attached, false, memory_order_release);
}

static uint64_t now_ms(void *context) {
    (void)context;
    return pp_jit_monotonic_ms();
}

static const pp_jit_platform_t k_macos_platform = {
    .context = NULL,
    .read_flags = read_flags,
    .enabler_opened = enabler_opened,
    .prepare_region = prepare_region,
    .detach = detach,
    .now_ms = now_ms,
    .poll_ms = PP_JIT_POLL_MS,
};

const pp_jit_platform_t *pp_jit_default_platform(void) { return &k_macos_platform; }
