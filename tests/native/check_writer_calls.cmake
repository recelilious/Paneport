# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks that the crash record writer calls only async-signal-safe functions: it lists the
# undefined symbols of the object of src/native/host/crash_record_writer.c with nm and fails on
# any that is not allowed (docs/design/native-host.md section 8). tests/native/CMakeLists.txt
# runs it as a CTest test:
#
#   cmake -DNM=<nm> -DOBJECTS=<objects of the host> -DSTRIP_UNDERSCORE=<ON on Apple> -P check_writer_calls.cmake
#
# Allowed: the functions that POSIX lists as async-signal-safe and the writer uses, errno's
# accessor, the host's own pp_utf8_cut, and what the compiler and the sanitizers add.

set(allowed
    clock_gettime close fsync memcpy memmove memset open rename unlink write
    __error __errno_location
    pp_utf8_cut
    __stack_chk_fail __stack_chk_guard)

foreach(object IN LISTS OBJECTS)
    if(object MATCHES "crash_record_writer\\.c\\.o(bj)?$")
        set(writer "${object}")
    endif()
endforeach()
if(NOT writer)
    message(FATAL_ERROR "No object of crash_record_writer.c among: ${OBJECTS}")
endif()

execute_process(COMMAND "${NM}" -u "${writer}"
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

message(STATUS "The crash record writer calls: ${calls}")
if(refused)
    message(FATAL_ERROR "The crash record writer calls functions that are not on the list of "
        "async-signal-safe calls: ${refused}")
endif()
