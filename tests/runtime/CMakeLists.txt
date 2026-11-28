# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The runtime harness (docs/design/runtime-macos-harness.md). src/native/CMakeLists.txt adds
# this folder when PANEPORT_HARNESS is on, as the macos-harness preset sets it, and when
# PANEPORT_TESTS is on, as the asan-ubsan and tsan presets set it, which run the CTest cases of
# the harness's own parts.

# Single mode links the native host's test build into the harness (section 5.1).
if(NOT PANEPORT_HOST_FORM STREQUAL "static" OR NOT PANEPORT_TEST_BUILD)
    message(FATAL_ERROR "The harness needs PANEPORT_HOST_FORM=static and PANEPORT_TEST_BUILD=ON.")
endif()

# The harness's parts that need no host: the case files, the expected outputs and their
# comparison, the child process, the prefixes of single mode and the result of its runs.
add_library(paneport_harness_core STATIC
    harness/case_file.c
    harness/child.c
    harness/expected.c
    harness/prefix.c
    harness/run_result.c
)
target_include_directories(paneport_harness_core PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/harness")

# The fork and exec trap of single mode (S4.3.9, section 5.2), a library of its own: dyld applies
# the interposing of an image that it loads at launch to every image that it binds, except that
# image itself. It is built without the sanitizers, whose runtimes interpose some of the same
# calls, posix_spawn among them; with two interposers of a call, dyld hands the call to one of
# them first, so the trap's test would not see it.
add_library(paneport_trap SHARED trap/trap.c trap/trap_darwin.c)
target_include_directories(paneport_trap PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/trap")
if(PANEPORT_SANITIZERS)
    target_compile_options(paneport_trap PRIVATE -fno-sanitize=all)
    target_link_options(paneport_trap PRIVATE -fno-sanitize=all)
endif()

# The harness, which in single mode is the process's main executable and runs Wine's code. It is
# linked with the SDK that builds it, as an app is, so macOS zeroes x18 on every return to user
# mode, as iOS does: the forks keep the TEB pointer in the thread's TSD, and a use of x18 that
# is left in a PE module or in generated code fails the cases (runtime-ios.md section 6). The
# test presets build it only to compile and analyze it.
add_executable(paneport_harness harness/main.c harness/single_child.c)
target_link_libraries(paneport_harness PRIVATE paneport_harness_core paneport_host paneport_trap)

if(PANEPORT_TESTS)
    # One test program per part, with one CTest case per check, as in tests/native, whose
    # assertion header the programs share. A program links the harness's parts, or the library
    # that LIBRARY names.
    function(paneport_harness_test name)
        cmake_parse_arguments(PARSE_ARGV 1 TEST "" "LIBRARY" "CASES")
        if(NOT TEST_LIBRARY)
            set(TEST_LIBRARY paneport_harness_core)
        endif()
        add_executable(${name} unit/${name}.c)
        target_link_libraries(${name} PRIVATE ${TEST_LIBRARY})
        target_include_directories(${name} PRIVATE "${PANEPORT_REPOSITORY_ROOT}/tests/native")
        foreach(test_case IN LISTS TEST_CASES)
            add_test(NAME ${name}.${test_case} COMMAND ${name} ${test_case})
            set_tests_properties(${name}.${test_case} PROPERTIES TIMEOUT 120)
        endforeach()
    endfunction()

    paneport_harness_test(test_case_file CASES
        valid_case environment_and_runs environment_and_runs_refused environment_of_the_runs
        inherited_environment session_cases session_cases_refused arguments_are_optional
        missing_or_repeated_member unknown_member wrong_type_or_range file_names_only malformed
        error_names_the_line)
    paneport_harness_test(test_expected CASES
        literal_and_pattern_lines carriage_returns pattern_matches_whole_line
        literal_is_not_a_pattern first_difference_named missing_lines extra_lines
        control_characters_quoted bad_expected_files crossed_parentheses_refused unhandled_reported)
    paneport_harness_test(test_child CASES
        output_and_exit_code input_is_empty directory_is_used timeout_ends_the_group
        signal_reported output_cut_at_the_limit errors_keep_their_tail errors_cut_on_the_last_read
        leftover_does_not_hold missing_program)
    paneport_harness_test(test_files CASES cases_are_valid expected_outputs_are_valid)
    target_compile_definitions(test_files PRIVATE
        "PP_REPOSITORY_ROOT=\"${PANEPORT_REPOSITORY_ROOT}\"")
    paneport_harness_test(test_prefix CASES
        copy_keeps_the_tree copy_ignores_the_umask copy_needs_a_folder copy_needs_a_new_target
        copy_names_what_it_cannot_read remove_takes_everything
        remove_takes_links_and_missing_folders remove_names_what_it_cannot_read)
    paneport_harness_test(test_run_result CASES
        round_trip exit_codes texts_stay_on_their_lines failed_write_changes_nothing
        exit_code_compared_whole missing_file_named malformed_files_refused)

    # The parent role of single mode, main.c, end to end: the harness built with a fake of its
    # child role, which needs neither the host nor Wine and reports the exit code that its
    # case's argument names, also one above 255, which no Win32 test program can exit with.
    # test_single_parent runs it by the path that PP_FAKE_CHILD_HARNESS names.
    add_executable(paneport_harness_fake_child harness/main.c unit/fake_single_child.c)
    target_link_libraries(paneport_harness_fake_child PRIVATE paneport_harness_core)
    paneport_harness_test(test_single_parent CASES whole_exit_code_judged)
    target_compile_definitions(test_single_parent PRIVATE
        "PP_FAKE_CHILD_HARNESS=\"$<TARGET_FILE:paneport_harness_fake_child>\"")
    add_dependencies(test_single_parent paneport_harness_fake_child)

    # S4.3.9: the trap, in a process that holds no sanitizer runtime, as the harness's does not.
    paneport_harness_test(test_trap LIBRARY paneport_trap CASES
        armed_spawn_is_refused disarmed_spawn_runs armed_exec_is_refused disarmed_exec_forwards
        armed_fork_is_refused armed_vfork_is_refused armed_shell_is_refused
        signals_refused_while_armed armed_group_signal_is_refused
        armed_thread_port_signal_is_refused)
    if(PANEPORT_SANITIZERS)
        target_compile_options(test_trap PRIVATE -fno-sanitize=all)
        target_link_options(test_trap PRIVATE -fno-sanitize=all)
    endif()
endif()
