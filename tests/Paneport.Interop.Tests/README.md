<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Interop.Tests

Tests of [Paneport.Interop](../../src/Paneport.Interop/README.md), which run on macOS against the host test library that the CMake preset `macos-test` builds (C-0011 section 4). They are in `Paneport.slnx` but not in `eng/ci.slnf`; the `native` CI job runs them.

| File | What it tests |
|---|---|
| `HostFixture.cs` | Not a test: loads the library that `PANEPORT_HOST_LIBRARY` names, without which every test fails, and starts the host once for all tests |
| `HostThread.cs` | Not a test: the thread that stands in for the main thread, because the test runner runs tests on worker threads |
| `AssemblySettings.cs` | Not a test: the fixture for the whole assembly, and one test at a time, because the tests share the host |
| `DependencyDirectionTests.cs` | Paneport.Interop references no other project, and this project only Paneport.Interop (C-0004 section 2) |
| `AbiVersionTests.cs` | The host implements the version of the bindings, and versions decode as major and minor |
| `LayoutConformanceTests.cs` | The interface conformance test of C-0005 section 5, with the planted wrong definition of `WrongMemoryBudget.cs`, which it reports by struct and member |
| `BindingTests.cs` | Every binding against the library: the memory budget, errors with their status and message, the main-thread rule, events and log lines on the event thread, an exception in a handler caught and reported, host memory freed with `pp_free`, and a test crash record |

Run them on a Mac from the repository root, once the library is built:

```bash
PANEPORT_HOST_LIBRARY="$PWD/build/native/macos-test/libpaneport_host.dylib" dotnet test tests/Paneport.Interop.Tests/Paneport.Interop.Tests.csproj
```
