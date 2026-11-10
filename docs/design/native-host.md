# Native Host

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-10T09:15Z |

Back to the [design index](README.md).

This document describes the native host of C-0004 section 2 as stage M1.2 built it (see the [L1 brief](stages/L1-app-on-iphone.md)): its public headers, its life cycle, the event thread and its queues, the log pipeline, the memory budget, the crash record, the exports of its test builds, how it is built and packaged, and how it is checked. [interop.md](interop.md) describes the .NET side of the interface. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Scope

The host is a C17 library in `src/native/`. The app embeds it as `paneport_host.framework` (C-0009 section 4) and calls it through Paneport.Interop (C-0005 section 1). M1.2 implements the groups that every later stage stands on:

| Group (C-0005 section 2) | Functions | File |
|---|---|---|
| Life cycle | `pp_abi_version` [any], `pp_init` [main], `pp_shutdown` [main], `pp_set_event_callback` [main] | `host.c` |
| Diagnostics | `pp_set_log_callback` [main], `pp_last_error` [any], `pp_free` [any] | `host.c`, `error.c` |
| Memory | `pp_memory_get_budget` [any] | `memory_budget.c` |

The JIT, session, process, Steam, display, input and audio groups, the signal dispatcher and the Wine bridge come with their stages (M2.2 to M8.2), each adding its functions and raising the minor version (C-0005 section 1). The host uses no Objective-C yet, because no API it calls needs it.

## 2. Headers and conventions

The public headers live in `src/native/include/paneport/` (C-0005 section 1):

| Header | What it declares |
|---|---|
| `common.h` | `PP_ABI_VERSION_MAJOR` 1 and `PP_ABI_VERSION_MINOR` 0, `pp_status_t` and its codes, `PP_EXPORT`, and the `extern "C"` guards |
| `host.h` | The life cycle and diagnostics groups, `pp_init_config_t`, `pp_event_t`, `pp_log_line_t`, the `PP_LOG_` levels and the two callback types |
| `memory.h` | `pp_memory_get_budget` and `pp_memory_budget_t` |
| `testing.h` | The exports of test builds (section 9) |
| `paneport.h` | Every header of the shipped interface, without `testing.h` |

- **Names and types.** Functions start with `pp_`, types end in `_t`, macros and enum constants start with `PP_`. Integers have fixed widths, booleans are `uint8_t`, and pointers appear only for strings, buffers, callbacks and `user_data`.
- **Status codes.** `PP_OK` is 0 and every error is negative. Version 1.0 has nine: `PP_E_INVALID_ARGUMENT` (-1), `PP_E_WRONG_THREAD` (-2), `PP_E_NOT_INITIALIZED` (-3), `PP_E_ALREADY_INITIALIZED` (-4), `PP_E_OUT_OF_MEMORY` (-5), `PP_E_IO` (-6), `PP_E_UNSUPPORTED` (-7), `PP_E_BUSY` (-8) and `PP_E_SYSTEM` (-9). The codes that C-0005 section 2 names for later groups, such as `PP_E_JIT_NOT_READY`, come with those groups.
- **Structs.** Each starts with `uint32_t struct_size`. A caller sets it to the size it knows. The host writes or reads only the members that lie entirely inside it, so a caller built against an older header gets the members it knows and a newer one keeps its extra bytes; structs grow only at their end. The host sets `struct_size` of the structs it sends to its own size.
- **Exports.** Everything is compiled with `-fvisibility=hidden`; `PP_EXPORT` marks the functions of the interface, and nothing else is exported (section 11).
- **Comments.** Every function in a header has a Doxygen comment with its purpose, its thread rule and who owns the memory it takes or returns (C-0010 sections 3 and 5).
- **Errors.** `pp_last_error` returns the calling thread's message, which the host owns and which stays valid until that thread's next call into the host. Every exported function except `pp_abi_version`, `pp_last_error` and `pp_free` clears the message when it starts and fills it when it fails, so after a call that succeeded it is the empty string, never NULL. Messages are plain sentences; they name no path and hold no secret.

## 3. Life cycle

- **`pp_init`** checks its configuration, prepares the crash record folder (section 8), starts the host event thread (section 5), and logs a line with the interface version. It runs once per process. A second call fails with `PP_E_ALREADY_INITIALIZED`, also after `pp_shutdown`, because later stages install the process's single signal dispatcher in it (C-0005 section 4) and run at most one Wine session per launch (C-0004 section 1). A call that fails, for example on a missing folder, does not count, and may be retried.
- **`pp_shutdown`** stops the event thread after it has delivered what was already queued, then clears both callbacks. Once it returns, no callback runs again.
- **The main thread.** A function marked [main] fails with `PP_E_WRONG_THREAD` on another thread. In the shipped builds the main thread is the one for which `pthread_main_np` answers 1. Test runners do not run tests on the process's main thread, so in test builds (`PANEPORT_TEST_BUILD`, section 9) the thread that first calls `pp_init` stands in for it. The device test flavor calls `pp_init` on its main thread, so the rule changes nothing there.
- **Order of checks.** A [main] function first checks that the host runs (`PP_E_NOT_INITIALIZED`), then the thread (`PP_E_WRONG_THREAD`).
- **Callbacks.** `pp_set_event_callback` and `pp_set_log_callback` take effect at once. When one returns, the callback it replaced is not running and is never called again, because the event thread holds a lock while it runs a callback and the setter takes the same lock (section 5). The caller may therefore release the old `user_data` right away, which Paneport.Interop does with its `GCHandle`. The guarantee also means that a callback must not wait for the main thread, as C-0005 section 1 already asks.

## 4. The event queue

`event_queue.c` is a bounded queue of fixed-size entries with many producers and one consumer. It follows the published sequence-numbered ring of Dmitry Vyukov's bounded queue (section 13), written for this project in C11 atomics:

- Each slot has a sequence number. Slot `i` starts at `i`. A producer reads the tail position `p`, and the slot of `p` is free when its sequence equals `p`; the producer then claims `p` with one compare-and-swap on the tail, copies the entry, and publishes it by storing `p + 1` into the slot's sequence with release order.
- The consumer reads the slot of the head position `h` with acquire order. Sequence `h + 1` means a published entry; it copies the entry out and stores `h + capacity` so the slot takes the entry of the next lap.
- A slot whose sequence is behind `p` still holds the previous lap's entry: the queue is full. The push returns false at once and adds one to the queue's overflow count. It never blocks, never waits and never allocates; the slots are allocated once, when the event thread starts.
- The capacity is a power of two. The tail, the head and the overflow count sit on separate 128-byte lines, the cache-line size of Apple silicon, so producers and the consumer do not share a line.

Per producer, entries keep their order, because a producer claims increasing positions. The test of S1.2.2 moves 1,000,000 entries from eight producers through one consumer under ThreadSanitizer, and a mutation that weakens the release store to relaxed makes ThreadSanitizer report a data race (section 11).

## 5. The event thread

The host event thread (`event_thread.c`, named `paneport.host-events`) is the only thread that runs callbacks (C-0005 section 1).

- **Two queues.** Events (256 entries of `pp_event_t`) and log lines (512 entries of 528 bytes) have a queue each, so a flood of log lines never pushes out an event. Posting fails with `PP_E_NOT_INITIALIZED` while the thread is not running and with `PP_E_BUSY` when the queue is full.
- **Sleeping and waking.** The thread drains both queues, then sets a `sleeping` flag, issues a sequentially consistent fence, and checks the queues once more before it waits on a condition variable. A producer, after its push, issues the same fence and takes the flag with an atomic exchange; only when the flag was set does it take the mutex and signal. Either the thread sees the new entry before it sleeps, or the producer sees that it sleeps, so no wake-up is lost, and producers take a lock only while the thread sleeps. Waking can take that lock, so posting is not async-signal-safe.
- **Callbacks under a lock.** The thread holds the callback mutex while it runs a callback, and the setters of section 3 take it to replace one. A callback that blocks therefore delays a setter, which is one more reason for callbacks to return quickly.
- **Early log lines.** While no log callback is set, the thread keeps the first 64 lines that arrive in a buffer and counts the rest. Setting a callback wakes the thread, which delivers the buffered lines oldest first and, when it had to drop lines, a warning that says how many. Events that arrive without an event callback are discarded.
- **Overflow.** The first time each queue refuses an entry, the thread delivers one warning line for it; later refusals are only counted, so a flood does not become a flood of notices. The warning goes straight to the log callback or the buffer, not through the full queue.
- **Stopping.** `pp_shutdown` first refuses new posts, then waits for a counter of posts in progress to reach zero, and only then wakes the thread and waits for it to end. A producer that saw the host running just before it stopped may still be between its check and its push; once the counter is zero, every post that returned `PP_OK` has published its entry, so the thread delivers it in its last drain, and no claimed but unpublished slot can hide the entries behind it. A post that starts later sees the refusal and touches no queue, so the queues are freed after the thread ends. The test that holds a post at that point, through a hook of the test builds (section 9), checks it.

The thread becomes known to the .NET runtime when it first runs a managed callback. That is expected for this thread and for no other host thread (C-0004 section 1).

## 6. The log pipeline

`log.c` is the only path for the host's own log lines (C-0010 section 3); the host never prints and never calls `os_log`.

- A line is formatted on the calling thread into a queue entry with its level and the thread's system identifier (`pthread_threadid_np`), and queued for the event thread.
- A line has at most 511 bytes. A longer one is cut at a UTF-8 character boundary, never in the middle of a character, and ends with `...`, so the text that reaches .NET is always valid UTF-8 when its input was.
- No line holds a password, token or other secret (C-0010 section 2). The host also logs no path, because the paths inside the app container identify the installation; a log export scrubs personal data anyway (C-0004 section 4).
- The levels `PP_LOG_DEBUG` to `PP_LOG_ERROR` match Paneport.Core's `LogLevel` in order and value, so the host adapter of M1.3 converts them directly.

## 7. The memory budget

`pp_memory_get_budget` follows C-0005 section 2 and the method of `process_and_memory_model.md` (section 13):

- **On iOS** the host reads `phys_footprint` from `task_info(TASK_VM_INFO)` and the available memory from `os_proc_available_memory()`. The memory limit is their sum. No private call is used. The two readings come from two calls, so the sum can differ from the real limit by what the footprint changed in between. `os_proc_available_memory` returns 0 for a process that is not an app, and the limit is then the footprint alone.
- **On macOS** `os_proc_available_memory` does not exist, so the test builds report the values of a stub, which `pp_test_set_memory_stub` sets (section 9); both start at 0. The sampling cadence, pressure events and the governor stay with M6.3.
- **The Game Mode level.** `game_mode_level_reached` is 1 when the limit is at least 7,936 MiB (`PP_MEMORY_GAME_MODE_LEVEL_BYTES`), which is 256 MiB below the 8,192 MiB that Game Mode gives a game on a 12 GB iPhone. Without Game Mode the limit is about 6,144 MiB, so the threshold separates the two with room on both sides, and the margin absorbs the moment between the two readings: one published reading at Game Mode added up to 8,191 MiB (7,259 MiB available plus 932 MiB of footprint). The flag says that the limit has reached the level, not that Game Mode is active; a device whose ordinary limit reached the level would set it too.
- **Struct size.** The test of S1.2.5 shows that a caller with a smaller `struct_size` gets only the members it knows, and that a larger one keeps its extra bytes.

## 8. The crash record

A fatal fault outside the guest path ends the app, so the host writes a crash record that the app reads on its next launch (C-0005 section 2). M2.2's signal dispatcher calls the writer; the reader is M1.3's, in Paneport.Core, and follows the table below, not the C code.

- **Where.** `pp_init` takes the folder in `pp_init_config_t.crash_record_folder`, such as the `Library/Caches/Paneport/` folder of the app container (C-0004 section 4). The path must be absolute, because the writer opens it at fault time, whatever the working folder is then. `pp_init` checks that the folder exists and can be written, and composes the paths of `crash-record.bin` and `crash-record.tmp` in it. It does not touch an existing record, so the previous run's record waits for the reader whether the app reads it before or after `pp_init`. A folder of NULL means no crash record; the writer then reports `PP_E_NOT_INITIALIZED`. When `pp_init` fails after it prepared the folder, it takes the paths away again, so the writer reports the same.
- **How.** The writer encodes the record into 128 bytes on the stack, writes them to `crash-record.tmp`, flushes it with `fsync`, closes it and renames it to `crash-record.bin`, so a reader never sees half a record and a newer record replaces an older one. It calls only `clock_gettime`, `open`, `write`, `fsync`, `close`, `rename` and `unlink`, which POSIX lists as async-signal-safe, keeps `errno`, never allocates and never sets `pp_last_error`. An atomic flag lets one thread write at a time; another gets `PP_E_BUSY`. The writer lives in `crash_record_writer.c`, apart from the preparation in `crash_record.c`, so that a CTest test can list the undefined symbols of its object with `nm` and fail on any call outside an allowlist: those seven functions, `memcpy`, `memmove` and `memset`, which POSIX lists too, errno's accessor, the host's own `pp_utf8_cut`, the stack protector and the sanitizers' symbols. ThreadSanitizer, which runs the test that writes a record from a handler, reports only allocation and deallocation inside a handler, not every call that is not async-signal-safe, so the allowlist and review keep the rest out.
- **The thread role.** The caller passes the role. `pp_host_thread_role` returns the event thread, the main thread or other, from atomics and `pthread_self`; guest and translator threads come with M2.2 and the Wine session.

The record is little-endian, 128 bytes, version 1:

| Offset | Size | Field |
|---|---|---|
| 0 | 4 | Magic: the bytes `P`, `P`, `C`, `R` (0x52435050 read as little-endian) |
| 4 | 2 | Version: 1 |
| 6 | 2 | Record size: 128 |
| 8 | 8 | Time: whole seconds since 1970-01-01 UTC, signed |
| 16 | 4 | Time: nanoseconds, 0 to 999,999,999 |
| 20 | 4 | Signal number, signed; 0 for a test record |
| 24 | 4 | Signal code (`si_code`), signed |
| 28 | 4 | Thread role: 0 other, 1 main, 2 host event thread; later stages add values |
| 32 | 8 | Fault address (`si_addr`) |
| 40 | 8 | Program counter of the faulting instruction |
| 48 | 1 | Flags: bit 0 is set for a record that a test wrote |
| 49 | 15 | Reserved, zero |
| 64 | 64 | Message: UTF-8, at most 63 bytes, cut at a character boundary, then NUL bytes to the end |

A reader rejects a record whose magic, version or size differs, or whose file is not exactly 128 bytes long; a larger record of a later version keeps these 128 bytes as its start.

## 9. Test builds

The presets `ios-device-test`, `macos-test`, `asan-ubsan` and `tsan` define `PANEPORT_TEST_BUILD`. It adds the main-thread rule of section 3, the exports of `paneport/testing.h`, compiled from `test_exports.c`, and a hook with which a test holds a post between its check that the host runs and its push (section 5); the `ios-device` framework that the main app ships has none of them. The `asan-ubsan` and `tsan` presets also build the host as that framework does, without `PANEPORT_TEST_BUILD`, so that a test runs the main-thread rule that ships.

| Export | Use |
|---|---|
| `pp_test_get_layout_table` | The size and member offsets of every struct of the interface, the two structs of the table included, each of which starts with `struct_size` like every other struct, so that a reader steps through the entries and the members by the host's sizes: `pp_init_config_t`, `pp_event_t`, `pp_log_line_t`, `pp_memory_budget_t`, `pp_test_layout_member_t` and `pp_test_layout_struct_t`. The conformance test of C-0005 section 5 compares it with the C# definitions ([interop.md](interop.md) section 6). |
| `pp_test_write_crash_record` | A record with the test flag and signal 0, through the writer of section 8. The device test flavor's developer page uses it (M1.3). |
| `pp_test_set_memory_stub` | The stub's readings on macOS; `PP_E_UNSUPPORTED` on iOS. |
| `pp_test_post_event`, `pp_test_log` | An event or a log line from the calling thread, as the host's components post them, for the binding tests. |
| `pp_test_duplicate_text` | A copy of a text in host memory, which the caller frees with `pp_free`. |

A struct added to a header is added to the layout table and to Paneport.Interop in the same change.

## 10. Build and packaging

The CMake project's root is `src/native/` (C-0004 section 3). Its presets build on shared ones in `eng/cmake/`:

| Preset | Toolchain | Builds |
|---|---|---|
| `ios-device` | `eng/cmake/ios.cmake` | `paneport_host.framework` for `arm64-apple-ios`, Release, without test exports. The main app embeds it (M1.3). |
| `ios-device-test` | `eng/cmake/ios.cmake` | The same framework with the test exports, for the device test flavor. |
| `macos-test` | `eng/cmake/macos.cmake` | `libpaneport_host.dylib` with the test exports, which `tests/Paneport.Interop.Tests` loads. |
| `asan-ubsan` | `eng/cmake/macos.cmake` | A static host and the CTest suite, with AddressSanitizer and UndefinedBehaviorSanitizer, Debug. |
| `tsan` | `eng/cmake/macos.cmake` | The same with ThreadSanitizer, RelWithDebInfo, because ThreadSanitizer is slow without optimization. |

- **Shared presets.** `eng/cmake/base-presets.json` holds the hidden presets: the Unix Makefiles generator, which needs nothing beyond Xcode, the build folder `build/native/<preset>/` at the repository root, which git ignores, compile databases for clang-tidy, and a condition that hides the presets on hosts other than macOS. Each preset has a build preset of the same name, and the sanitizer presets have test presets that set the sanitizer options: errors stop the program, and ThreadSanitizer stops at the first report.
- **Toolchains.** The iOS toolchain sets `CMAKE_SYSTEM_NAME` to iOS, the `iphoneos` SDK, arm64 and iOS 26.0, the oldest Paneport supports (C-0001 section 2), and compiles CMake's checks into static libraries, because nothing could run a device executable. The macOS toolchain sets arm64 and macOS 26.0. Both use the Xcode that `DEVELOPER_DIR` or `xcode-select` selects; CI selects the pinned one first.
- **Compiler settings.** C17 and C++20 without extensions, `-Wall -Wextra -Wpedantic -Werror -Wvla` (C-0010 section 3), hidden visibility, position-independent code, and `-ffile-prefix-map`, which writes source paths relative to the repository, so no local path ends up in a binary (C-0009 section 6). Apple's SDK headers come through the sysroot as system headers, so the warnings apply to our code alone.
- **The framework.** CMake builds a flat iOS framework with the install name `@rpath/paneport_host.framework/paneport_host`, which the .NET bindings import (C-0009 section 4), and an Info.plist from `host/Info.plist.in` with `CFBundlePackageType` FMWK, `CFBundleSupportedPlatforms` iPhoneOS and `MinimumOSVersion` 26.0. Its bundle identifier is the placeholder `com.example.paneport.host` until decision D3 names the app's, from which M1.3 derives it. The framework is not signed beyond what Apple's linker gives arm64 code; M1.3 signs it ad hoc with the app (C-0009 section 6).

## 11. Checks

| Check | Where | What passes |
|---|---|---|
| CTest with `asan-ubsan` and `tsan` | `native` job | Every test of `tests/native` with no sanitizer report: 29 tests, which are 25 cases of the six programs of S1.2.1 to S1.2.6, 2 cases of the main-thread rule that ships, the header test, and the check of the crash record writer's calls (section 8). |
| Header test | `native` job, inside CTest | Every public header compiles alone as C17 and as C++20 under the project's warnings, and all of them together as C17 (C-0005 section 5). A C++20 source calls functions of the headers, and the program links the host's C objects, so it fails to link when a header loses its `extern "C"` guards. |
| clang-format | `native` job, `eng/mac/check-native-style.sh format` | Every C, Objective-C and C++ file of `src/`, `tests/` and `tools/` matches the root `.clang-format`. A planted format error in a scratch copy under `build/` fails the check. |
| clang-tidy | `native` job, `eng/mac/check-native-style.sh tidy` | No finding in the source files of the compile databases of `asan-ubsan`, `ios-device-test` and `ios-device`, so the iOS code is analyzed too, in both the test builds and the build that ships. |
| Framework | `ios` job, `eng/mac/check-framework.sh` | Both frameworks have the install name, arm64 code for iOS devices only and the Info.plist values above. The `ios-device` build exports exactly the `PP_EXPORT` functions of the headers outside `testing.h`, and the `ios-device-test` build exactly those and the functions of `testing.h`. |
| Bindings | `native` job | `tests/Paneport.Interop.Tests` against the `macos-test` library ([interop.md](interop.md) section 8). |

**clang-tidy and Apple's clang.** clang-tidy comes from the pinned LLVM release and reads compile commands that Apple's clang wrote, so the two can disagree about a flag or about a construct in an SDK header. Such a disagreement is handled in `.clang-tidy`, with the check or option that is off and the reason, never with a comment in the code. The configuration turns on whole families of checks; what is off, and why, is listed at its top.

**Local runs.** A Mac with the bootstrap's tools runs the same commands as the `native` job; [dev-environment.md](dev-environment.md) section 4 lists them. The workstation of this project has no Mac (D1), so CI is where they run.

## 12. Confirmed in this stage

- **C# offsets under disabled runtime marshalling.** Paneport.Interop measures each member as the distance from the start of a struct to the member, with `Unsafe.ByteOffset`, and each size with `Unsafe.SizeOf`. That is the layout that crosses the interface, because the structs are blittable and runtime marshalling is off; `Marshal.OffsetOf` would describe a marshalled layout that the assembly never uses ([interop.md](interop.md) section 6).
- **The library name per target.** One name, `@rpath/paneport_host.framework/paneport_host`, on every target; what differs is how it resolves ([interop.md](interop.md) section 2).
- **Sanitizers with the pinned Xcode.** AddressSanitizer with UndefinedBehaviorSanitizer, and ThreadSanitizer, work with the clang of Xcode 27.1 on the `xcode-27` image: in CI on 2026-11-10 both presets built and passed all 23 tests, in 1.0 s and 1.7 s. Before CI, the same suite also passed under both sanitizers in a Linux build with a local stand-in for the three calls of `platform_darwin.c`, and a mutation that weakened the queue's release store made ThreadSanitizer report a data race.
- **The framework.** CMake's Unix Makefiles generator builds the flat iOS framework with the install name the bindings import, arm64 code for iOS devices (`LC_BUILD_VERSION` platform 2) and the exports of section 11, with nothing else exported.
- **Presets and toolchains.** A hidden preset that an included file defines expands `${fileDir}` for the preset that inherits it, so `eng/cmake/base-presets.json` names its toolchain files from `${sourceDir}` instead. CMake 4 names no SDK unless asked, and Apple's compiler driver then finds one by itself, so a compile database holds no `-isysroot`, and clang-tidy, which is not Apple's, finds no SDK header; `eng/cmake/macos.cmake` names the `macosx` SDK, as `ios.cmake` names `iphoneos`.
- **Tool installs.** In CI the bootstrap installs CMake in about 3 s and the LLVM release in about 1 min 45 s, most of it for the 1.6 GB download and its unpacking.

## 13. Sources

Public material consulted (C-0001 section 6):

- Research note `process_and_memory_model.md`, on the memory limit, its estimate and the Game Mode level: [process_and_memory_model.md](../research/process_and_memory_model.md)
- Research note `avalonia_ui_layer.md`, sections 2.2 and 3.3, on frameworks, P/Invoke, callbacks, threads and queues: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
- `os_proc_available_memory`: <https://developer.apple.com/documentation/os/os_proc_available_memory>
- The bounded multi-producer queue with sequence numbers, by Dmitry Vyukov: <https://www.1024cores.net/home/lock-free-algorithms/queues/bounded-mpmc-queue>
- POSIX.1-2024, section 2.4.3, the async-signal-safe functions: <https://pubs.opengroup.org/onlinepubs/9799919799/functions/V2_chap02.html>
- CMake presets: <https://cmake.org/cmake/help/latest/manual/cmake-presets.7.html>
- CMake toolchains, cross compiling for iOS: <https://cmake.org/cmake/help/latest/manual/cmake-toolchains.7.html>
- CMake's `FRAMEWORK` and `MACOSX_FRAMEWORK_INFO_PLIST` properties: <https://cmake.org/cmake/help/latest/prop_tgt/FRAMEWORK.html> and <https://cmake.org/cmake/help/latest/prop_tgt/MACOSX_FRAMEWORK_INFO_PLIST.html>
- AddressSanitizer, ThreadSanitizer and UndefinedBehaviorSanitizer: <https://clang.llvm.org/docs/AddressSanitizer.html>, <https://clang.llvm.org/docs/ThreadSanitizer.html> and <https://clang.llvm.org/docs/UndefinedBehaviorSanitizer.html>
- clang-format's style options: <https://clang.llvm.org/docs/ClangFormatStyleOptions.html>
- clang-tidy's checks: <https://clang.llvm.org/extra/clang-tidy/checks/list.html>
- CMake 4.4.5 and LLVM 23.1.5, the pinned releases: <https://github.com/Kitware/CMake/releases/tag/v4.4.5> and <https://github.com/llvm/llvm-project/releases/tag/llvmorg-23.1.5>
