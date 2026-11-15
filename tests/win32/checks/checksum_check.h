// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// A checksum over a fixed 64 MiB buffer, as a measure of translated integer and SSE2 code.
//
// The buffer holds 8,388,608 little-endian 64-bit words. Word n is x(n) ^ (x(n) >> 33), where
// x(0) = 6364136223846793005 * 0x50616e65706f7274 + 1442695040888963407 and every further x
// follows from the one before by the same step, modulo 2^64. The checksum combines two sums over
// the buffer: h, the 64-bit FNV-1a hash taken over the words, one word per step; and four 32-bit
// lane sums, where lane j adds every 32-bit value whose index modulo 4 is j, modulo 2^32. It is
//
//     h ^ ((uint64_t)(lane0 ^ rotl32(lane1, 8)) << 32) ^ (lane2 ^ rotl32(lane3, 16))
//
// The lane sums use SSE2 when the compiler targets it and plain C otherwise, with the same
// result. PP_CHECKSUM_EXPECTED is that checksum, computed independently in C, with both ways of
// summing the lanes, and in JavaScript with BigInt arithmetic when the program was written.

#ifndef PP_CHECKSUM_CHECK_H
#define PP_CHECKSUM_CHECK_H

#include "test_output.h"

#include <stdint.h>

#define PP_CHECKSUM_EXPECTED UINT64_C(0x5e75a40f80343ce2)

// Fills the buffer, computes the checksum several times, and prints one line,
// "checksum: 0x<value> (expected 0x<expected>), <n> MiB/s", with the throughput of the fastest
// pass. It checks that every pass gives the expected value and returns whether they did.
bool pp_checksum_report(struct pp_test_run *run);

#endif
