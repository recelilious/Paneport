// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// seh.exe, the structured exception handling program: an access violation, an integer divide
// by zero and a RaiseException call, each caught by its own handler, then a __finally block
// during an unwind and the vectored handlers.

#include "seh_check.h"
#include "test_output.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"seh", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "seh: an %s build", PP_TEST_ARCHITECTURE);
    pp_seh_basic(&run, "seh");
    pp_seh_extended(&run, "seh");
    return pp_test_finish(&run, exit_code);
}
