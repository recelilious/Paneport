// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// shared_data.exe: the shared user data at its fixed address, 0x7ffe0000, as x86-64 code sees
// it. It calls ntdll's syscall exports, whose x64 thunks test a byte of the page, reads the
// page's fields in many instruction forms and its clocks, and writes to it, which must fault.

#include "shared_data_check.h"
#include "test_output.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"shared_data", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "shared_data: an %s build", PP_TEST_ARCHITECTURE);
    pp_shared_data_imported_syscalls(&run, "shared_data");
    pp_shared_data_syscalls_by_address(&run, "shared_data");
    pp_shared_data_version_fields(&run, "shared_data");
    pp_shared_data_clocks(&run, "shared_data");
    pp_shared_data_writes(&run, "shared_data");
    return pp_test_finish(&run, exit_code);
}
