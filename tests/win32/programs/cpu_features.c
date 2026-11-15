// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// cpu_features.exe: the vendor and feature flags that CPUID reports, and whether
// IsProcessorFeaturePresent agrees with them.

#include "cpu_check.h"
#include "test_output.h"

int main(int argc, char **argv) {
    struct pp_test_run run = {"cpu_features", 0U, false};
    int exit_code = pp_test_exit_code_argument(&run, argc, argv);
    pp_test_line(&run, "cpu_features: an %s build", PP_TEST_ARCHITECTURE);
    pp_cpu_report(&run, "cpu_features");
    pp_cpu_compare_with_windows(&run, "cpu_features");
    return pp_test_finish(&run, exit_code);
}
