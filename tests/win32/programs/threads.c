// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// threads.exe: interlocked updates, a critical section, events, the clocks, misaligned accesses
// from a second thread, one worker suspended, its context changed, then resumed, and a thread
// that loops on a syscall suspended again and again, each on a line of its own with its counts.

#include "test_output.h"
#include "threads_check.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"threads", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "threads: an %s build", PP_TEST_ARCHITECTURE);
    pp_threads_report(&run, "threads", true);
    return pp_test_finish(&run, exit_code);
}
