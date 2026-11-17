# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks that the code that runs in a signal handler calls only async-signal-safe functions: it
# lists the undefined symbols of one object of the host with nm and fails on any that is not on
# that object's list (docs/design/native-host.md section 8, docs/design/signals.md section 10).
# tests/native/CMakeLists.txt runs it as a CTest test for each such source:
#
#   cmake -DNM=<nm> -DOBJECTS=<objects of the host> -DSTRIP_UNDERSCORE=<ON on Apple>
#         -DSOURCE=<crash_record_writer.c or signal_handler.c> -P check_signal_safe_calls.cmake
#
# Allowed: the functions that POSIX lists as async-signal-safe and the object uses, errno's
# accessor, the host's own functions that are async-signal-safe themselves, and what the
# compiler and the sanitizers add.

set(common_allowed
    __error __errno_location
    __stack_chk_fail __stack_chk_guard)
if(SOURCE STREQUAL "crash_record_writer.c")
    # The crash record writer (M1.2).
    set(allowed ${common_allowed}
        clock_gettime close fsync memcpy memmove memset open rename unlink write
        pp_utf8_cut)
elseif(SOURCE STREQUAL "signal_handler.c")
    # The signal dispatcher's handler, the chain and the last-resort handler (M2.2). It also
    # calls the crash record writer, the thread role, which reads atomics and pthread_self, and
    # the stack bounds, which read the calling thread's own pthread record.
    set(allowed ${common_allowed}
        memcpy memmove memset pthread_kill pthread_self pthread_sigmask raise sigaction sigaddset
        sigemptyset
        pp_crash_record_write pp_host_thread_role pp_platform_stack_bounds)
else()
    message(FATAL_ERROR "No list of allowed calls for ${SOURCE}.")
endif()

foreach(object IN LISTS OBJECTS)
    string(REPLACE "." "\\." pattern "${SOURCE}")
    if(object MATCHES "/${pattern}\\.o(bj)?$")
        set(checked "${object}")
    endif()
endforeach()
if(NOT checked)
    message(FATAL_ERROR "No object of ${SOURCE} among: ${OBJECTS}")
endif()

execute_process(COMMAND "${NM}" -u "${checked}"
    OUTPUT_VARIABLE output ERROR_VARIABLE errors RESULT_VARIABLE result)
if(NOT result EQUAL 0)
    message(FATAL_ERROR "${NM} -u failed with ${result}: ${errors}")
endif()

string(REPLACE "\n" ";" lines "${output}")
set(calls "")
set(refused "")
foreach(line IN LISTS lines)
    # GNU nm prints a type letter before the name, such as U or w; Apple's nm the name alone.
    # string(REGEX REPLACE) would apply "^" again after each match, so a match with one group
    # takes exactly one prefix away.
    string(STRIP "${line}" symbol)
    if(symbol MATCHES "^[A-Za-z] +(.+)$")
        set(symbol "${CMAKE_MATCH_1}")
    endif()
    if(symbol STREQUAL "")
        continue()
    endif()
    # Apple's toolchain puts one underscore in front of every C name.
    if(STRIP_UNDERSCORE AND symbol MATCHES "^_(.+)$")
        set(symbol "${CMAKE_MATCH_1}")
    endif()
    # The sanitizers' own symbols, such as __asan_report_load8 or __start_asan_globals.
    if(symbol MATCHES "^__[a-z_]*(asan|ubsan|tsan|sanitizer)")
        continue()
    endif()
    list(APPEND calls "${symbol}")
    if(NOT symbol IN_LIST allowed)
        list(APPEND refused "${symbol}")
    endif()
endforeach()

message(STATUS "${SOURCE} calls: ${calls}")
if(refused)
    message(FATAL_ERROR "${SOURCE} calls functions that are not on its list of "
        "async-signal-safe calls: ${refused}")
endif()
