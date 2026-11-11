<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# native

The native host (C-0004 sections 2 and 3): a CMake project in C17 that the app loads as `paneport_host.framework`. It implements the host interface of [C-0005](../../docs/contracts/C-0005-native-host-interface.md), and [native-host.md](../../docs/design/native-host.md) describes its design. The project builds for Apple platforms only: the framework for iOS, and test builds for macOS.

| Path | What it holds |
|---|---|
| `CMakeLists.txt` | The project: the host's sources, its three forms (the framework, the shared test library and the static library of the tests), the compiler warnings, the sanitizers, and the CTest suite of [tests/native](../../tests/native/README.md) |
| `CMakePresets.json` | The presets `ios-device`, `ios-device-test`, `macos-test`, `asan-ubsan` and `tsan`, on top of the shared presets and toolchain files in `eng/cmake/` |
| [include/paneport/](include/paneport/README.md) | The public headers of the host interface |
| [host/](host/README.md) | The host's code |

On a Mac that `eng/mac/bootstrap.sh` has prepared, build from this folder with `cmake --preset <name>` and `cmake --build --preset <name>`, and run the tests of `asan-ubsan` and `tsan` with `ctest --preset <name>`. The output goes to `build/native/<preset>/` at the repository root, which git ignores. In CI the `native` job runs the checks and tests, and the `ios` job builds both frameworks.
