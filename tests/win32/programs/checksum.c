// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// checksum.exe: the checksum of a fixed 64 MiB buffer beside its expected value, and the
// throughput of the fastest pass.

#include "checksum_check.h"
#include "test_output.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"checksum", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "checksum: an %s build", PP_TEST_ARCHITECTURE);
    pp_checksum_report(&run);
    return pp_test_finish(&run, exit_code);
}
