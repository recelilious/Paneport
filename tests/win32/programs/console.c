// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// console.exe, the console test program and L4's sample program: a greeting that names the
// architecture, then the CPU feature, checksum, structured exception handling and thread checks,
// and the result line. An optional argument sets the exit code that a passing run returns.

#include "checksum_check.h"
#include "cpu_check.h"
#include "seh_check.h"
#include "test_output.h"
#include "threads_check.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"console", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "console: hello from an %s build", PP_TEST_ARCHITECTURE);
    pp_cpu_report(&run, "cpu");
    pp_checksum_report(&run);
    pp_seh_basic(&run, "seh");
    pp_threads_report(&run, "threads", false);
    return pp_test_finish(&run, exit_code);
}
