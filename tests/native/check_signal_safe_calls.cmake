# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks that the code that runs in a signal handler calls only async-signal-safe functions: it
# lists the undefined symbols of a group of the host's objects with nm and fails on any that
# the group does not define itself and that is not on the group's list (docs/design/native-host.md
# section 8, docs/design/signals.md section 10). A call from one object of the group to a
# function of another is checked through the other's calls. tests/native/CMakeLists.txt runs
# it as a CTest test for each group, on the test build's objects and, for the groups that ship,
# on the shipped build's too:
#
#   cmake -DNM=<nm> -DOBJECTS=<objects of the host> -DSTRIP_UNDERSCORE=<ON on Apple>
#         -DSOURCES=<the group's sources, separated by |> -P check_signal_safe_calls.cmake
#
# Allowed: the functions that POSIX lists as async-signal-safe and the group uses, errno's
# accessor, the few others that each list names with its reason, and what the compiler and the
# sanitizers add.

string(REPLACE "|" ";" sources "${SOURCES}")
list(GET sources 0 first_source)

set(common_allowed
    __error __errno_location
    __stack_chk_fail __stack_chk_guard)
if(first_source STREQUAL "crash_record_writer.c")
    # The crash record writer (M1.2), and the host's own pp_utf8_cut, which only counts bytes.
    set(allowed ${common_allowed}
        clock_gettime close fsync memcpy memmove memset open rename unlink write
        pp_utf8_cut)
elseif(first_source STREQUAL "signal_handler.c")
    # The signal dispatcher's handler, the chain and the last-resort handler (M2.2), with the
    # thread role and the stack bounds of thread_role.c. pthread_equal only compares two thread
    # identities; Apple's pthread_main_np, which the shipped builds' main-thread test calls,
    # and pthread_get_stackaddr_np and pthread_get_stacksize_np read the calling thread's own
    # pthread record without a lock. The crash record writer has a check of its own.
    set(allowed ${common_allowed}
        clock_gettime memcpy memmove memset pthread_kill pthread_self pthread_sigmask raise select
        sigaction sigaddset sigemptyset
        pthread_equal pthread_main_np pthread_get_stackaddr_np pthread_get_stacksize_np
        pp_crash_record_write pp_crash_record_writer_idle)
elseif(first_source STREQUAL "signal_harness_handler.c")
    # The guest fault handler of the signal gate's harness (M2.2), which uses atomics only.
    set(allowed ${common_allowed})
elseif(first_source STREQUAL "jit_probe_handler.c")
    # The JIT probe handler (M3.1), which uses atomics and the fault's context only.
    set(allowed ${common_allowed})
else()
    message(FATAL_ERROR "No list of allowed calls for ${SOURCES}.")
endif()

set(checked "")
foreach(source IN LISTS sources)
    set(found "")
    string(REPLACE "." "\\." pattern "${source}")
    foreach(object IN LISTS OBJECTS)
        if(object MATCHES "/${pattern}\\.o(bj)?$")
            set(found "${object}")
        endif()
    endforeach()
    if(NOT found)
        message(FATAL_ERROR "No object of ${source} among: ${OBJECTS}")
    endif()
    list(APPEND checked "${found}")
endforeach()

# Lists the symbols that nm prints for an object with the given options, as C names.
function(read_symbols object options result)
    execute_process(COMMAND "${NM}" ${options} "${object}"
        OUTPUT_VARIABLE output ERROR_VARIABLE errors RESULT_VARIABLE status)
    if(NOT status EQUAL 0)
        message(FATAL_ERROR "${NM} ${options} failed with ${status}: ${errors}")
    endif()
    string(REPLACE "\n" ";" lines "${output}")
    set(symbols "")
    foreach(line IN LISTS lines)
        # nm prints an address and a type letter before a defined name; GNU nm prints a type
        # letter before an undefined one, and Apple's nm the name alone. string(REGEX REPLACE)
        # would apply "^" again after each match, so a match with groups takes the name.
        string(STRIP "${line}" symbol)
        if(symbol MATCHES "^([0-9A-Fa-f]+ +)?[A-Za-z] +(.+)$")
            set(symbol "${CMAKE_MATCH_2}")
        endif()
        if(symbol STREQUAL "")
            continue()
        endif()
        # Apple's toolchain puts one underscore in front of every C name.
        if(STRIP_UNDERSCORE AND symbol MATCHES "^_(.+)$")
            set(symbol "${CMAKE_MATCH_1}")
        endif()
        # Apple's headers may name a variant of a function after a dollar sign, such as
        # select$DARWIN_EXTSN; the variant is the same call.
        if(symbol MATCHES "^([^$]+)[$].+$")
            set(symbol "${CMAKE_MATCH_1}")
        endif()
        list(APPEND symbols "${symbol}")
    endforeach()
    set(${result} "${symbols}" PARENT_SCOPE)
endfunction()

set(undefined "")
set(defined "")
foreach(object IN LISTS checked)
    read_symbols("${object}" "-u" object_undefined)
    list(APPEND undefined ${object_undefined})
    read_symbols("${object}" "-g;-U" object_defined)
    list(APPEND defined ${object_defined})
endforeach()
list(REMOVE_DUPLICATES undefined)

set(calls "")
set(refused "")
foreach(symbol IN LISTS undefined)
    # A function of another object of the group, whose calls the check reads too.
    if(symbol IN_LIST defined)
        continue()
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

message(STATUS "${SOURCES} call: ${calls}")
if(refused)
    message(FATAL_ERROR "${SOURCES} call functions that are not on the list of "
        "async-signal-safe calls: ${refused}")
endif()
