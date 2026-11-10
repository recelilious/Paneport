# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The toolchain of the native host for arm64-apple-ios: Apple's clang and the iPhoneOS SDK
# from the Xcode that DEVELOPER_DIR or xcode-select selects. iOS 26.0 is the oldest version
# Paneport supports (C-0001 section 2). The presets of src/native/CMakePresets.json use it.

set(CMAKE_SYSTEM_NAME iOS)
set(CMAKE_OSX_SYSROOT iphoneos)
set(CMAKE_OSX_ARCHITECTURES arm64 CACHE STRING "The architecture of the iOS builds.")
set(CMAKE_OSX_DEPLOYMENT_TARGET 26.0 CACHE STRING "The oldest iOS that the host runs on.")

# CMake's compiler checks link no executable for the device, which nothing could run here.
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)
