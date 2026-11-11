# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The toolchain of the native host's test builds on macOS: Apple's clang from the Xcode that
# DEVELOPER_DIR or xcode-select selects, for arm64 only. The presets of
# src/native/CMakePresets.json use it.

set(CMAKE_OSX_ARCHITECTURES arm64 CACHE STRING "The architecture of the macOS test builds.")
set(CMAKE_OSX_DEPLOYMENT_TARGET 26.0 CACHE STRING "The oldest macOS that the test builds run on.")

# CMake 4 names no SDK unless asked, and Apple's compiler driver then finds one by itself; the
# compile database would hold no -isysroot, and clang-tidy, which is not Apple's, would find no
# SDK header. Naming the SDK puts its path in every compile command.
set(CMAKE_OSX_SYSROOT macosx)
