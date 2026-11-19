// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// file_io.exe: file operations on drive C:, and GetLogicalDrives reporting C:.

#include "file_io_check.h"
#include "test_output.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"file_io", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "file_io: an %s build", PP_TEST_ARCHITECTURE);
    pp_file_io_report(&run, "file_io");
    return pp_test_finish(&run, exit_code);
}
