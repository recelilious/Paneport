# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The CTest suite of the native host (C-0011 section 1). src/native/CMakeLists.txt adds this
# folder when PANEPORT_TESTS is on, as the asan-ubsan and tsan presets set it.

# One test program per small stage. Each case runs in a process of its own, because pp_init
# runs once per process, and CTest names it <program>.<case>. A program links the test build
# of the host, or the host that LIBRARY names. A case that cannot run in one of the builds
# says so in the first line of its output, which starts with "Skipped: ", and CTest reports it
# as skipped rather than passed.
function(paneport_native_test name)
    cmake_parse_arguments(PARSE_ARGV 1 TEST "" "LIBRARY" "CASES")
    if(NOT TEST_LIBRARY)
        set(TEST_LIBRARY paneport_host)
    endif()
    add_executable(${name} ${name}.c)
    target_link_libraries(${name} PRIVATE ${TEST_LIBRARY})
    target_include_directories(${name} PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}"
        "${PANEPORT_REPOSITORY_ROOT}/src/native/host")
    foreach(test_case IN LISTS TEST_CASES)
        add_test(NAME ${name}.${test_case} COMMAND ${name} ${test_case})
        set_tests_properties(${name}.${test_case} PROPERTIES
            TIMEOUT 300
            SKIP_REGULAR_EXPRESSION "^Skipped: ")
    endforeach()
endfunction()

paneport_native_test(test_status CASES
    abi_version last_error_per_thread free_host_memory device_model)
paneport_native_test(test_event_queue CASES
    full_queue_refuses many_producers)
paneport_native_test(test_event_thread CASES
    lines_before_an_event_arrive_first event_during_a_flood_arrives_soon
    no_event_dropped_during_a_flood held_producer_keeps_the_order held_producer_holds_an_event_once
    events_reach_event_thread none_after_shutdown post_in_progress_is_delivered
    event_overflow_logged_once second_init_fails wrong_thread replaced_callback_finished)
paneport_native_test(test_log CASES
    lines_whole_and_ordered early_lines_arrive overlong_line_cut overflow_logged_once
    overflow_notice_waits_for_a_callback)
paneport_native_test(test_memory_budget CASES
    limit_is_sum game_mode_level smaller_struct_size invalid_arguments)
paneport_native_test(test_crash_record CASES
    encode_layout message_cut written_in_signal_handler test_record_flag)

# The signal dispatcher (M2.2, docs/design/signals.md section 10). A case that ends by a signal
# runs that part in a child process of its own program, and checks how the child ended and the
# crash record it left.
paneport_native_test(test_signal_chain CASES
    every_signal_reaches_runtime_once plain_handler_gets_signal ignored_sent_signal_returns
    ignored_with_siginfo_returns first_page_fault_needs_no_record default_action_ends_with_record
    runtime_declined_fault_ends_with_record crash_path_illegal_keeps_record
    crash_path_managed_fault_keeps_record ignored_breakpoint_ends_with_record
    simulated_overflow_ends_with_record main_thread_overflow_ends_with_record
    main_thread_overflow_reaches_runtime busy_writer_keeps_first_record
    stuck_writer_wait_is_bounded)
paneport_native_test(test_signal_routing CASES
    routes_hold_while_writers_change pool_fault_goes_to_probe_only_in_probe_mode
    pool_counts_by_program_counter mark_ends_with_its_thread tables_refuse_when_full
    marked_thread_table_full)
paneport_native_test(test_signal_guest CASES
    handler_sees_only_guest_faults declined_fault_ends_with_record
    fault_without_handler_ends_with_record removal_waits_for_handler
    marked_thread_overflow_ends_with_record guest_handler_fault_ends_with_record
    probe_handler_fault_ends_with_record)
paneport_native_test(test_thread_control CASES
    register_change_seen_after_resume ended_thread_fails destroyed_request_fails_as_ended
    suspending_itself_refused)
paneport_native_test(test_signal_installed CASES
    passes_after_init fails_naming_replaced_signal fails_before_init stacks_follow_marks
    release_takes_stack_away uninstall_takes_stack_away last_resort_installed_at_load
    last_resort_only_where_default)
paneport_native_test(test_signal_harness CASES
    runs_sixty_seconds start_refused_before_init start_checks_its_conditions
    short_structs_follow_struct_size)

# The JIT provider (M3.1, docs/design/jit.md section 10). The provider's cases use the fake
# platform of jit_test_support.h or the macOS backend's simulated broker, and the regions of
# the macOS backend.
paneport_native_test(test_jit_machine CASES
    transitions_in_order broker_left_before_request foreign_debuggers pool_timeout
    detach_timeout debugger_lost probe_failure failed_is_terminal request_only_when_attached
    memory_limit_fails pool_sizes names)
paneport_native_test(test_jit_flags CASES
    decodes_both_flags system_reader_without_debugger broker_after_enabler
    debugger_before_enabler_is_foreign debugger_without_enabler_is_foreign
    refused_enabler_makes_debugger_foreign waits_for_cs_debugged foreground_reads_again
    calls_are_checked)
paneport_native_test(test_jit_mapping CASES
    alias_is_coherent no_view_is_writable_and_executable writable_executable_is_found
    commands_per_region pool_of_a_title pool_limited_to_an_eighth below_the_minimum_fails
    region_refused_fails debugger_lost_fails)
paneport_native_test(test_jit_probe CASES
    probed_offsets healthy_pool_passes fault_on_first_page fault_on_last_page
    fault_on_interval_page fault_off_the_probed_pages routine_of_another_page_fails
    routine_of_another_region_fails handler_takes_only_its_window fault_on_the_alias
    provider_fails_on_a_planted_fault)
paneport_native_test(test_jit_provider CASES
    simulated_broker_reaches_ready event_on_every_change pool_timeout_fails detach_timeout_fails
    ready_refuses_another_request failed_refuses_everything enabler_taken_back_is_reported
    commands_after_shutdown nothing_kept_after_shutdown shutdown_between_commands
    shutdown_stops_the_supervisor)
# The session and process groups (M4.3), against a fake of the Wine fork's Unix library that the
# host loads from a runtime folder, as it loads the fork, and the splitter of a program's output.
add_library(fake_wine_fork MODULE fake_wine_fork.c)
target_include_directories(fake_wine_fork PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}"
    "${PANEPORT_REPOSITORY_ROOT}/src/native/include")
# The fork's entry header is a system header, as for the host (C-0010 section 3).
target_include_directories(fake_wine_fork SYSTEM PRIVATE "${PANEPORT_WINE_ENTRY_INCLUDE}")
target_link_libraries(fake_wine_fork PRIVATE Threads::Threads)
paneport_native_test(test_session CASES
    start_needs_host_and_jit fork_receives_the_bridge events_arrive_on_event_thread
    output_lines_arrive_before_the_exit second_session_refused replaced_handler_fails_session
    launch_check_fails_the_session started_hook_runs_before_check runtime_refusal_fails_session launch_needs_a_session
    terminate_sets_the_exit_code get_state_asks_the_fork stop_ends_a_running_process
    stop_during_a_launch stop_counts_an_unreported_end stop_wakes_a_reader_without_an_end
    failed_start_is_reported
    launch_refusal_is_reported configuration_checks wrong_thread)
target_compile_definitions(test_session PRIVATE "FAKE_FORK_PATH=\"$<TARGET_FILE:fake_wine_fork>\"")
add_dependencies(test_session fake_wine_fork)
paneport_native_test(test_guest_output CASES
    every_chunking_gives_the_same_lines piece_ends_at_a_character carriage_returns
    empty_lines_and_the_end wake_ends_a_reader_whose_pipes_stay_open nul_bytes_become_replacements)

# The main-thread rule of the shipped builds, on the host that src/native/CMakeLists.txt builds
# without the test exports and without PANEPORT_TEST_BUILD.
paneport_native_test(test_main_thread LIBRARY paneport_host_shipped CASES
    main_functions_need_the_main_thread first_init_on_another_thread_fails)

# Every public header, each alone in a C17 source and a C++20 source, and a C++20 source that
# calls the host, so the program fails to link when a header loses its extern "C" guards
# (C-0005 section 5).
file(GLOB PANEPORT_PUBLIC_HEADERS CONFIGURE_DEPENDS
    "${PANEPORT_REPOSITORY_ROOT}/src/native/include/paneport/*.h")
set(header_sources)
foreach(header IN LISTS PANEPORT_PUBLIC_HEADERS)
    get_filename_component(header_name "${header}" NAME_WE)
    foreach(extension IN ITEMS c cpp)
        set(source "${CMAKE_CURRENT_BINARY_DIR}/headers/${header_name}_alone.${extension}")
        file(CONFIGURE OUTPUT "${source}" CONTENT
"// Generated by tests/native/CMakeLists.txt: paneport/${header_name}.h alone.
#include \"paneport/${header_name}.h\"
int paneport_header_${header_name}_${extension}(void);
int paneport_header_${header_name}_${extension}(void) { return 0; }
")
        list(APPEND header_sources "${source}")
    endforeach()
endforeach()
add_executable(test_headers test_headers.c test_headers_linkage.cpp ${header_sources})
target_include_directories(test_headers PRIVATE "${PANEPORT_REPOSITORY_ROOT}/src/native/include")
target_link_libraries(test_headers PRIVATE paneport_host)
add_test(NAME test_headers.c17_and_cxx20 COMMAND test_headers)

# The crash record writer, the signal dispatcher's handler with the thread role it reads, the
# harness's guest fault handler and the JIT probe handler call only async-signal-safe functions
# (docs/design/native-host.md section 8, docs/design/signals.md section 10). The check reads
# a group's objects with nm: <name>.async_signal_safe_calls reads them as the test build
# compiles them, and <name>_shipped.async_signal_safe_calls, for the two groups that ship, as
# paneport_host_shipped compiles them, without PANEPORT_TEST_BUILD, so that code that only the
# shipped builds have, such as their main-thread test, is read too.
if(APPLE)
    set(strip_underscore ON)
else()
    set(strip_underscore OFF)
endif()
function(paneport_signal_safe_check name library group)
    add_test(NAME ${name}.async_signal_safe_calls
        COMMAND "${CMAKE_COMMAND}"
            "-DNM=${CMAKE_NM}"
            "-DOBJECTS=$<TARGET_OBJECTS:${library}>"
            "-DSTRIP_UNDERSCORE=${strip_underscore}"
            "-DSOURCES=${group}"
            -P "${CMAKE_CURRENT_SOURCE_DIR}/check_signal_safe_calls.cmake")
endfunction()
paneport_signal_safe_check(crash_record_writer paneport_host_objects "crash_record_writer.c")
paneport_signal_safe_check(crash_record_writer_shipped paneport_host_shipped
    "crash_record_writer.c")
paneport_signal_safe_check(signal_handler paneport_host_objects "signal_handler.c|thread_role.c")
paneport_signal_safe_check(signal_handler_shipped paneport_host_shipped
    "signal_handler.c|thread_role.c")
paneport_signal_safe_check(signal_harness_handler paneport_host_objects
    "signal_harness_handler.c")
paneport_signal_safe_check(jit_probe_handler paneport_host_objects "jit_probe_handler.c")
paneport_signal_safe_check(jit_probe_handler_shipped paneport_host_shipped "jit_probe_handler.c")
