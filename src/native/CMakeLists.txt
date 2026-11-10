# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The native host (C-0004 sections 2 and 3). Build it through the presets of
# CMakePresets.json; docs/design/native-host.md section 10 lists them. The project builds for
# Apple platforms only: the framework for iOS, and test builds for macOS.

cmake_minimum_required(VERSION 3.28)

project(paneport_native VERSION 1.0.0 LANGUAGES C CXX)

if(NOT APPLE)
    message(FATAL_ERROR "The native host builds for Apple platforms only: iOS, and macOS for tests.")
endif()

set(PANEPORT_HOST_FORM "static" CACHE STRING
    "How the host is built: framework (iOS), shared (the macOS test library) or static (CTest).")
set_property(CACHE PANEPORT_HOST_FORM PROPERTY STRINGS framework shared static)
option(PANEPORT_TEST_BUILD "Build the test exports and the test rule for the main thread." OFF)
option(PANEPORT_TESTS "Build the CTest suite of tests/native." OFF)
set(PANEPORT_SANITIZERS "" CACHE STRING
    "The sanitizers for -fsanitize, such as address,undefined or thread; empty for none.")
set(PANEPORT_FRAMEWORK_IDENTIFIER "com.example.paneport.host" CACHE STRING
    "The framework's bundle identifier; a placeholder until decision D3 names the app's.")

# C17 without compiler extensions, and C++20 for the header test only (C-0005 section 5,
# C-0010 section 3). Apple's SDK headers come through the sysroot as system headers, so the
# warnings below apply to our own code alone.
set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

add_compile_options(-Wall -Wextra -Wpedantic -Werror -Wvla)

# Build paths are written relative to the source tree, so that no local path ends up in the
# binaries (C-0009 section 6).
get_filename_component(PANEPORT_REPOSITORY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../.." ABSOLUTE)
add_compile_options("-ffile-prefix-map=${PANEPORT_REPOSITORY_ROOT}/=")

if(PANEPORT_SANITIZERS)
    add_compile_options("-fsanitize=${PANEPORT_SANITIZERS}" -fno-omit-frame-pointer
                        -fno-sanitize-recover=all)
    add_link_options("-fsanitize=${PANEPORT_SANITIZERS}")
endif()

set(PANEPORT_HOST_SOURCES
    host/crash_record.c
    host/crash_record_writer.c
    host/error.c
    host/event_queue.c
    host/event_thread.c
    host/host.c
    host/log.c
    host/memory_budget.c
    host/platform_darwin.c
)
set(PANEPORT_HOST_TEST_SOURCES host/test_exports.c)

find_package(Threads REQUIRED)

# The host's code, compiled once for the library and for the tests. Test builds add the
# exports of paneport/testing.h.
if(PANEPORT_TEST_BUILD)
    add_library(paneport_host_objects OBJECT ${PANEPORT_HOST_SOURCES} ${PANEPORT_HOST_TEST_SOURCES})
else()
    add_library(paneport_host_objects OBJECT ${PANEPORT_HOST_SOURCES})
endif()
target_include_directories(paneport_host_objects
    PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include"
    PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/host")
if(PANEPORT_TEST_BUILD)
    target_compile_definitions(paneport_host_objects PUBLIC PANEPORT_TEST_BUILD=1)
endif()

if(PANEPORT_HOST_FORM STREQUAL "framework")
    # paneport_host.framework, which the app embeds and imports as
    # @rpath/paneport_host.framework/paneport_host (C-0009 section 4).
    add_library(paneport_host SHARED $<TARGET_OBJECTS:paneport_host_objects>)
    set_target_properties(paneport_host PROPERTIES
        FRAMEWORK TRUE
        MACOSX_FRAMEWORK_IDENTIFIER "${PANEPORT_FRAMEWORK_IDENTIFIER}"
        MACOSX_FRAMEWORK_BUNDLE_VERSION "${PROJECT_VERSION}"
        MACOSX_FRAMEWORK_SHORT_VERSION_STRING "${PROJECT_VERSION_MAJOR}.${PROJECT_VERSION_MINOR}"
        MACOSX_FRAMEWORK_INFO_PLIST "${CMAKE_CURRENT_SOURCE_DIR}/host/Info.plist.in"
        INSTALL_NAME_DIR "@rpath"
        BUILD_WITH_INSTALL_NAME_DIR TRUE
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}")
    target_link_libraries(paneport_host PRIVATE Threads::Threads)
elseif(PANEPORT_HOST_FORM STREQUAL "shared")
    # libpaneport_host.dylib, which tests/Paneport.Interop.Tests loads on macOS.
    add_library(paneport_host SHARED $<TARGET_OBJECTS:paneport_host_objects>)
    set_target_properties(paneport_host PROPERTIES
        INSTALL_NAME_DIR "@rpath"
        BUILD_WITH_INSTALL_NAME_DIR TRUE
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}")
    target_link_libraries(paneport_host PRIVATE Threads::Threads)
elseif(PANEPORT_HOST_FORM STREQUAL "static")
    add_library(paneport_host STATIC $<TARGET_OBJECTS:paneport_host_objects>)
    target_include_directories(paneport_host
        PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include"
        INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/host")
    if(PANEPORT_TEST_BUILD)
        target_compile_definitions(paneport_host INTERFACE PANEPORT_TEST_BUILD=1)
    endif()
    target_link_libraries(paneport_host PUBLIC Threads::Threads)
else()
    message(FATAL_ERROR "PANEPORT_HOST_FORM must be framework, shared or static, not ${PANEPORT_HOST_FORM}.")
endif()

if(PANEPORT_TESTS)
    if(NOT PANEPORT_HOST_FORM STREQUAL "static" OR NOT PANEPORT_TEST_BUILD)
        message(FATAL_ERROR "The tests need PANEPORT_HOST_FORM=static and PANEPORT_TEST_BUILD=ON.")
    endif()

    # The host as the shipped framework compiles it: without the test exports and without
    # PANEPORT_TEST_BUILD, so that a test runs the main-thread rule that ships.
    add_library(paneport_host_shipped STATIC ${PANEPORT_HOST_SOURCES})
    target_include_directories(paneport_host_shipped
        PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include"
        PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/host")
    target_link_libraries(paneport_host_shipped PUBLIC Threads::Threads)

    enable_testing()
    add_subdirectory("${PANEPORT_REPOSITORY_ROOT}/tests/native" "${CMAKE_BINARY_DIR}/tests")
endif()
