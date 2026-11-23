// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "jit/jit_probe.h"

#include <stdatomic.h>
#include <stddef.h>
#include <string.h>

// The test routine of a probed page: movz w0, #value, then ret.
#define PP_JIT_MOVZ_W0 0x52800000U
#define PP_JIT_RET 0xD65F03C0U

// Writes two instruction words through the alias, each with a single store, so that the
// probe handler can skip one that faults.
static void store_routine(uint64_t write_address, uint32_t first, uint32_t second) {
    __asm__ volatile("str %w1, [%0]\n"
                     "str %w2, [%0, #4]\n"
                     :
                     : "r"(write_address), "r"(first), "r"(second)
                     : "memory");
}

// Makes the words written at write_address visible to instruction fetches at exec_address,
// with the sequence of the Arm architecture for modified code, by address and for one cache
// line: clean the data cache through the alias, which was written, and invalidate the
// instruction cache through the view, which is run.
static void flush_line(uint64_t write_address, uint64_t exec_address) {
    __asm__ volatile("dc cvau, %0\n"
                     "dsb ish\n"
                     "ic ivau, %1\n"
                     "dsb ish\n"
                     "isb\n"
                     :
                     : "r"(write_address), "r"(exec_address)
                     : "memory");
}

// Calls the routine at exec_address and returns its x0. The routine is ours and changes only
// x0, and the probe handler's recovery only x0 and the program counter.
static uint64_t run_routine(uint64_t exec_address) {
    uint64_t returned = 0U;
    __asm__ volatile("blr %1\n"
                     "mov %0, x0\n"
                     : "=r"(returned)
                     : "r"(exec_address)
                     : "x0", "x30", "cc", "memory");
    return returned;
}

static void set_phase(uint32_t phase) {
    atomic_store_explicit(&pp_jit_probe_window.phase, phase, memory_order_release);
}

uint64_t pp_jit_probe_page(uint64_t write_address, uint64_t exec_address, uint32_t value) {
    pp_jit_probe_window_t *window = &pp_jit_probe_window;
    const uint32_t before = atomic_load_explicit(&window->faults, memory_order_acquire);
    atomic_store_explicit(&window->first_signal, 0, memory_order_relaxed);
    atomic_store_explicit(&window->write_start, write_address, memory_order_relaxed);
    atomic_store_explicit(&window->exec_start, exec_address, memory_order_relaxed);

    set_phase(PP_JIT_PROBE_STORE);
    store_routine(write_address, PP_JIT_MOVZ_W0 | ((value & 0xFFFFU) << 5U), PP_JIT_RET);
    set_phase(PP_JIT_PROBE_FLUSH);
    flush_line(write_address, exec_address);
    set_phase(PP_JIT_PROBE_RUN);
    const uint64_t returned = run_routine(exec_address);

    // Nothing of the probe stays in the pool: zero words are permanently undefined
    // instructions, which is what an unused page of the pool holds anyway.
    set_phase(PP_JIT_PROBE_STORE);
    store_routine(write_address, 0U, 0U);
    set_phase(PP_JIT_PROBE_FLUSH);
    flush_line(write_address, exec_address);
    set_phase(PP_JIT_PROBE_IDLE);

    if (atomic_load_explicit(&window->faults, memory_order_acquire) != before) {
        return PP_JIT_PROBE_FAULTED;
    }
    return returned;
}

uint32_t pp_jit_probe_offsets(uint64_t region_size, uint64_t page_size, uint64_t *offsets,
                              uint32_t capacity) {
    if (page_size == 0U || region_size < page_size || capacity == 0U) {
        return 0U;
    }
    uint32_t count = 0U;
    for (uint64_t offset = 0U; offset < region_size && count < capacity;
         offset += PP_JIT_PROBE_INTERVAL) {
        offsets[count++] = offset;
    }
    const uint64_t last = region_size - page_size;
    if (count < capacity && offsets[count - 1U] != last) {
        offsets[count++] = last;
    }
    return count;
}

void pp_jit_probe_run(const pp_jit_pool_region_t *regions, uint32_t count,
                      pp_jit_probe_result_t *result) {
    memset(result, 0, sizeof *result);
    result->passed = true;
    pp_jit_probe_window_t *window = &pp_jit_probe_window;
    atomic_store_explicit(&window->page_size, pp_jit_page_size(), memory_order_relaxed);
    atomic_store_explicit(&window->faults, 0U, memory_order_relaxed);
    set_phase(PP_JIT_PROBE_IDLE);
    (void)pp_signals_set_probe_handler(pp_jit_probe_handle_fault, NULL);
    (void)pp_signals_set_probe_mode(1U);

    uint32_t serial = 0U;
    for (uint32_t index = 0U; index < count && result->passed; index++) {
        uint64_t offsets[PP_JIT_PROBE_MAX_PAGES];
        const uint32_t pages = pp_jit_probe_offsets(regions[index].size, pp_jit_page_size(),
                                                    offsets, PP_JIT_PROBE_MAX_PAGES);
        for (uint32_t page = 0U; page < pages; page++) {
            // A value of its own for each page, so a routine that ran from another page fails.
            const uint32_t value = 0x5000U + (serial & 0x0FFFU);
            serial++;
            const uint64_t returned =
                pp_jit_probe_page(regions[index].write_address + offsets[page],
                                  regions[index].exec_address + offsets[page], value);
            result->pages++;
            if (returned != value) {
                result->passed = false;
                result->region = index;
                result->offset = offsets[page];
                result->returned = returned;
                result->signal = atomic_load_explicit(&window->first_signal, memory_order_relaxed);
                break;
            }
        }
    }

    (void)pp_signals_set_probe_mode(0U);
    (void)pp_signals_set_probe_handler(NULL, NULL);
    result->faults = atomic_load_explicit(&window->faults, memory_order_acquire);
}
