// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// pool.exe: executable memory when the host's only executable memory is the JIT code pool. Its
// argument names what it checks: "requests", the requests for executable memory that the
// runtime refuses, serves from the pool, or serves with plain memory; or "generated", x86-64
// code that the program generates itself, enough of it to fill the translator's code buffer
// several times when the case caps that buffer. It is a program of the harness's pool mode.

#include <string.h>

#include "pool_check.h"
#include "test_output.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"pool", 0U, false};
    pp_test_line(&run, "pool: an %s build", PP_TEST_ARCHITECTURE);
    if (argc == 2 && strcmp(argv[1], "requests") == 0) {
        pp_pool_requests(&run, "pool");
    } else if (argc == 2 && strcmp(argv[1], "generated") == 0) {
        pp_pool_generated_code(&run, "pool");
    } else {
        pp_test_check(&run, false, "the argument is requests or generated");
    }
    return pp_test_finish(&run, 0);
}
