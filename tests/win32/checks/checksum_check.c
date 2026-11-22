// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

#include "checksum_check.h"

#include <inttypes.h>
#include <string.h>

#ifdef __SSE2__
#include <emmintrin.h>
#endif

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define PP_CHECKSUM_BYTES 67108864U // 64 MiB
#define PP_CHECKSUM_PASSES 4U

static void fill_buffer(unsigned char *bytes) {
    uint64_t state = UINT64_C(0x50616e65706f7274);
    for (size_t offset = 0; offset < PP_CHECKSUM_BYTES; offset += sizeof(uint64_t)) {
        state = (state * UINT64_C(6364136223846793005)) + UINT64_C(1442695040888963407);
        uint64_t word = state ^ (state >> 33U);
        memcpy(bytes + offset, &word, sizeof word);
    }
}

static uint32_t rotate_left(uint32_t value, unsigned count) {
    return (value << count) | (value >> (32U - count));
}

static uint64_t compute_checksum(const unsigned char *bytes) {
    uint64_t hash = UINT64_C(0xcbf29ce484222325);
    for (size_t offset = 0; offset < PP_CHECKSUM_BYTES; offset += sizeof(uint64_t)) {
        uint64_t word = 0;
        memcpy(&word, bytes + offset, sizeof word);
        hash ^= word;
        hash *= UINT64_C(0x100000001b3);
    }

    uint32_t lanes[4] = {0U, 0U, 0U, 0U};
#ifdef __SSE2__
    __m128i sums = _mm_setzero_si128();
    for (size_t offset = 0; offset < PP_CHECKSUM_BYTES; offset += 16U) {
        sums =
            _mm_add_epi32(sums, _mm_loadu_si128((const __m128i *)(const void *)(bytes + offset)));
    }
    _mm_storeu_si128((__m128i *)(void *)lanes, sums);
#else
    for (size_t offset = 0; offset < PP_CHECKSUM_BYTES; offset += 16U) {
        for (size_t lane = 0; lane < 4U; lane++) {
            uint32_t value = 0;
            memcpy(&value, bytes + offset + (lane * 4U), sizeof value);
            lanes[lane] += value;
        }
    }
#endif
    uint64_t high = lanes[0] ^ rotate_left(lanes[1], 8U);
    uint64_t low = lanes[2] ^ rotate_left(lanes[3], 16U);
    return hash ^ (high << 32U) ^ low;
}

bool pp_checksum_report(struct pp_test_run *run) {
    unsigned char *bytes =
        VirtualAlloc(NULL, PP_CHECKSUM_BYTES, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (bytes == NULL) {
        pp_test_check(run, false, "VirtualAlloc gives the 64 MiB buffer");
        return false;
    }
    fill_buffer(bytes);

    LARGE_INTEGER frequency;
    QueryPerformanceFrequency(&frequency);
    uint64_t value = 0;
    bool same = true;
    LONGLONG fastest = 0;
    for (unsigned pass = 0; pass < PP_CHECKSUM_PASSES; pass++) {
        LARGE_INTEGER start;
        LARGE_INTEGER end;
        QueryPerformanceCounter(&start);
        uint64_t result = compute_checksum(bytes);
        QueryPerformanceCounter(&end);
        LONGLONG ticks = end.QuadPart - start.QuadPart;
        if (pass == 0U || ticks < fastest) {
            fastest = ticks;
        }
        if (pass == 0U) {
            value = result;
        } else if (result != value) {
            same = false;
        }
    }
    VirtualFree(bytes, 0, MEM_RELEASE);

    // MiB per second of the fastest pass; a pass too short to time counts as one tick.
    if (fastest < 1) {
        fastest = 1;
    }
    uint64_t throughput = (uint64_t)((64.0 * (double)frequency.QuadPart) / (double)fastest);
    pp_test_line(run, "checksum: 0x%016" PRIx64 " (expected 0x%016" PRIx64 "), %" PRIu64 " MiB/s",
                 value, PP_CHECKSUM_EXPECTED, throughput);
    unsigned failures = run->failures;
    pp_test_check(run, same, "every checksum pass gives the same value");
    pp_test_check(run, value == PP_CHECKSUM_EXPECTED, "the checksum equals the expected value");
    return run->failures == failures;
}
