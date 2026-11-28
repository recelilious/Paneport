// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The memory that a Windows program asks for as executable, when the host's only executable
// memory is the JIT code pool (C-0006, docs/design/runtime-ios.md section 4). The runtime serves
// requests for ARM64EC code from the pool and refuses those that the pool cannot serve, and
// gives every other executable request plain memory, which is all that x86-64 code needs,
// because the translator reads it and never runs it. The checks ask for both kinds, and run
// x86-64 code that the program generates itself, enough of it to fill the translator's code
// buffer several times when the case caps that buffer. They are checks of pool mode: on Windows,
// and in the harness's other modes, the request of the first check is not refused.

#ifndef PP_POOL_CHECK_H
#define PP_POOL_CHECK_H

#include "test_output.h"

// Asks VirtualAlloc2 for 1 GiB of memory with the attribute for ARM64EC code, which is more than
// any region of the pool holds and must fail with ERROR_NOT_ENOUGH_MEMORY; asks for 64 KiB the
// same way, which the pool serves, zeroed, and frees them; then asks VirtualAlloc for 64 KiB of
// PAGE_EXECUTE_READWRITE memory, which must succeed, report that protection, hold what is written
// to it, and run a function that is written into it. Prints one line. Returns whether every check
// passed.
bool pp_pool_requests(struct pp_test_run *run, const char *area);

// Writes PP_POOL_FUNCTIONS small x86-64 functions, each at an address of its own with a constant
// of its own, into PAGE_EXECUTE_READWRITE memory, and calls every one of them in each of
// PP_POOL_PASSES passes, comparing each result with the value that the function must return.
// Each function is a block of its own for the translator, so the passes generate several
// megabytes of host code. Prints one line that names the numbers. Returns whether every check
// passed.
bool pp_pool_generated_code(struct pp_test_run *run, const char *area);

#endif
