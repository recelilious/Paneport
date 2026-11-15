# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The runtime harness (docs/design/runtime-macos-harness.md). src/native/CMakeLists.txt adds
# this folder when PANEPORT_HARNESS is on, as the macos-harness preset sets it, and when
# PANEPORT_TESTS is on, as the asan-ubsan and tsan presets set it, which run the CTest cases of
# the harness's own parts.

# The harness's parts: the case files, the expected outputs and their comparison, and the child
# process of multi mode.
add_library(paneport_harness_core STATIC
    harness/case_file.c
    harness/child.c
    harness/expected.c
)
target_include_directories(paneport_harness_core PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/harness")

add_executable(paneport_harness harness/main.c)
target_link_libraries(paneport_harness PRIVATE paneport_harness_core)

if(PANEPORT_TESTS)
    # One test program per part, with one CTest case per check, as in tests/native, whose
    # assertion header the programs share.
    function(paneport_harness_test name)
        cmake_parse_arguments(PARSE_ARGV 1 TEST "" "" "CASES")
        add_executable(${name} unit/${name}.c)
        target_link_libraries(${name} PRIVATE paneport_harness_core)
        target_include_directories(${name} PRIVATE "${PANEPORT_REPOSITORY_ROOT}/tests/native")
        foreach(test_case IN LISTS TEST_CASES)
            add_test(NAME ${name}.${test_case} COMMAND ${name} ${test_case})
            set_tests_properties(${name}.${test_case} PROPERTIES TIMEOUT 120)
        endforeach()
    endfunction()

    paneport_harness_test(test_case_file CASES
        valid_case arguments_are_optional missing_or_repeated_member unknown_member
        wrong_type_or_range file_names_only malformed error_names_the_line)
    paneport_harness_test(test_expected CASES
        literal_and_pattern_lines carriage_returns pattern_matches_whole_line
        literal_is_not_a_pattern first_difference_named missing_lines extra_lines
        control_characters_quoted bad_expected_files unhandled_reported)
    paneport_harness_test(test_child CASES
        output_and_exit_code input_is_empty directory_is_used timeout_ends_the_group
        signal_reported output_cut_at_the_limit leftover_does_not_hold missing_program)
    paneport_harness_test(test_files CASES cases_are_valid expected_outputs_are_valid)
    target_compile_definitions(test_files PRIVATE
        "PP_REPOSITORY_ROOT=\"${PANEPORT_REPOSITORY_ROOT}\"")
endif()
