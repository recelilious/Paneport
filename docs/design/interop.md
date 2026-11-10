# Paneport.Interop

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-10T09:15Z |

Back to the [design index](README.md).

This document describes Paneport.Interop, the .NET side of the host interface of C-0005, as stage M1.2 built it (see the [L1 brief](stages/L1-app-on-iphone.md)). L0 recorded the project's role in [solution-and-build.md](solution-and-build.md) and assigned its full design to this stage. [native-host.md](native-host.md) describes the C side. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Role

Paneport.Interop holds the source-generated bindings to the native host (C-0004 section 2). It is the only project that may use unsafe code and `LibraryImport` (C-0010 section 2), and it depends on nothing else in the solution. Paneport.iOS adapts it to Paneport.Core's `IRuntimeHost` (M1.3), the device test flavor runs its conformance test on the device (M1.3), and its tests run on macOS against the host test library (section 8).

- **Build.** `net10.0`, `IsAotCompatible`, `AllowUnsafeBlocks`, and the assembly attribute `DisableRuntimeMarshalling`, which the project file adds. The `dotnet` job builds it in `eng/ci.slnf` with the AOT and trimming analyzers, and the trimmed test app roots it.
- **No package.** The bindings need only the base class library.

## 2. The library name per target

Every binding imports one name, `NativeHost.LibraryName`, which is `@rpath/paneport_host.framework/paneport_host` (C-0009 section 4). The name is a constant on every target, because `LibraryImport` takes a constant; what differs per target is how the runtime resolves it:

| Target | Resolution |
|---|---|
| iOS, the main app and the device test flavor | The runtime opens the name as it stands. It resolves when the app's executable carries `@executable_path/Frameworks` among its run paths, as Apple's packaging of frameworks asks, and the framework sits in `Frameworks/`, embedded with a `NativeReference` of kind `Framework`; M1.3 confirms both in its build. If the name does not resolve under Mono on the device, the fallback of C-0009 section 4 is `__Internal`: M1.3 then links the host into the executable and registers a resolver that returns `NativeLibrary.GetMainProgramHandle()` for the name, or changes the constant in a reviewed change. L1 records which import its build uses. |
| macOS, `tests/Paneport.Interop.Tests` | The tests register a resolver with `NativeLibrary.SetDllImportResolver` that loads the library that `PANEPORT_HOST_LIBRARY` names, `build/native/macos-test/libpaneport_host.dylib` (section 8). |
| Windows and Linux | Nothing loads the host. The desktop head uses a fake host (M0.3), and the `dotnet` job only builds the bindings. |

## 3. Bindings

- **Declarations.** `NativeMethods` declares one `LibraryImport` per export of `paneport/host.h`, `paneport/memory.h` and `paneport/testing.h`, with the C name as its entry point. With runtime marshalling off, only blittable types cross (C-0005 section 1): integers, pointers, function pointers and the structs below. The text parameters of the test exports are `string` with `StringMarshalling.Utf8`, which the source generator turns into code that converts them; no marshalling happens at run time.
- **Structs.** `NativeInitConfig`, `NativeEvent`, `NativeLogLine`, `NativeMemoryBudget`, `NativeLayoutMember` and `NativeLayoutStruct` mirror the C structs member by member, in order, with sequential layout. Pointer members are `nint`, so that the conformance test can measure them like any other member (section 6), and booleans are `byte`, as C-0005 section 1 has them cross. The structs are internal; the public API hands out records instead (section 4).
- **Test exports.** A test export exists only in the host's test builds. The runtime looks an export up when it is first called, so declaring the test exports costs the main app nothing, and calling one there throws `EntryPointNotFoundException`.

## 4. The safe API

| Member | Host function | Thread |
|---|---|---|
| `NativeHost.ReadAbiVersion()` | `pp_abi_version` | Any |
| `NativeHost.Initialize(folder)` | `pp_init`, after the version check | Main |
| `NativeHost.Shutdown()` | `pp_shutdown` | Main |
| `NativeHost.SetEventHandler(handler)` | `pp_set_event_callback` | Main |
| `NativeHost.SetLogHandler(handler)` | `pp_set_log_callback` | Main |
| `NativeHost.SetCallbackFailureHandler(handler)` | None | Any |
| `NativeHost.GetMemoryBudget()` | `pp_memory_get_budget` | Any |
| `HostTestExports` | The exports of `paneport/testing.h` | Any |
| `HostAbi` | `PP_ABI_VERSION_MAJOR` and `PP_ABI_VERSION_MINOR` as these bindings know them, and how to unpack and compare versions | Any |

- **Errors.** A call that fails throws `HostException` with the `HostStatus` that the host returned and the host's message, which the bindings read with `pp_last_error` on the same thread before any other call into the host can replace it.
- **The version check.** `Initialize` reads `pp_abi_version` and throws `HostAbiMismatchException` when the host's major version differs from `HostAbi.Major`, so the .NET side refuses to start on a mismatch (C-0005 section 1). M1.3 makes the same check before it calls `Initialize`, to show its message instead of the shell.
- **Results.** Events, log lines and the memory budget reach callers as the records `HostEvent`, `HostLogLine` and `HostMemoryBudget`, and log levels as `HostLogLevel`, whose values match Paneport.Core's `LogLevel` (C-0004 section 2 keeps Interop independent of Core, so the host adapter of M1.3 maps them).
- **Strings.** The crash record folder goes to `pp_init` as a UTF-8 copy that the bindings free once the call returns; the host copies what it keeps (C-0005 section 1). Texts from the host are read as UTF-8.

## 5. Callbacks

- **Static methods.** The host calls two static methods marked `[UnmanagedCallersOnly]` with the C calling convention, `OnEvent` and `OnLogLine`, through function pointers; no managed delegate is marshalled (C-0005 section 1).
- **user_data.** The handler that a caller sets is held by a `GCHandle`, whose value goes to the host as `user_data`. The callback turns it back into the handler. When `pp_set_event_callback` or `pp_set_log_callback` returns, the host guarantees that the previous callback is not running and is never called again ([native-host.md](native-host.md) section 3), so the bindings free the previous handle at once, and `Shutdown` frees both.
- **No exception into C.** Each callback catches every exception that its handler throws and hands it to the handler of `SetCallbackFailureHandler`, which the host adapter of M1.3 points at its log; an exception from that handler is dropped. No exception ever reaches the host: under CoreCLR it would end the process, and under Mono it would unwind through C frames (`avalonia_ui_layer.md` section 2.2).
- **The event thread.** The handlers run on the host event thread, which becomes known to the runtime when it first runs one (C-0004 section 1). They must return quickly and must not call into the host; the host adapter of M1.3 hands events to the UI thread through its dispatcher.

## 6. The layout conformance test

C-0005 section 5 asks that every struct's size and member offsets match between C and C#. `LayoutConformance` does that, and the device test flavor runs the same class on the device (M1.3).

- **Measuring C#.** `StructLayoutBuilder<TStruct>` takes, for each member, its C name and a `FieldAccessor` that returns a reference to the field. It measures the offset as the distance in bytes from the start of a default struct to that field, with `Unsafe.ByteOffset`, and the size with `Unsafe.SizeOf`. That is the layout in memory, which is what crosses the interface, because the structs are blittable and runtime marshalling is off. `Marshal.OffsetOf` describes a marshalled layout, which this assembly never uses, so it is not used. This answers the question of the brief of how C# measures member offsets under `DisableRuntimeMarshalling`.
- **The host's side.** `HostTestExports.ReadLayoutTable` reads the table of `pp_test_get_layout_table`, which the host builds with `offsetof` and `sizeof` ([native-host.md](native-host.md) section 9).
- **The comparison.** `LayoutConformance.Compare` reports one line per difference: a struct that only one side has, a different size, and a member that is missing, extra, or at another offset or size, each named as `<struct>.<member>`. `Check` compares `ManagedLayouts` with the host's table.
- **Adding a struct.** A struct added to a header gets its C# definition, its entry in `ManagedLayouts` and its row in the host's table in the same change; the test fails on any of the three alone.

## 7. AOT, trimming and runtimes

- The bindings use no reflection and no runtime marshalling, so the AOT and trimming analyzers report nothing, and the trimmed publish of `tests/Paneport.TrimTest` shows no warning.
- `UnmanagedCallersOnly` callbacks and function pointers work under Mono's full AOT on iOS and under CoreCLR, so they move to CoreCLR with M10.1 and M10.2 without a change.

## 8. Tests

`tests/Paneport.Interop.Tests` runs on macOS in the `native` job, outside `eng/ci.slnf` (C-0011 section 4).

- **The library.** An assembly fixture loads the library that `PANEPORT_HOST_LIBRARY` names and resolves `NativeHost.LibraryName` to it; without the variable every test fails. It starts the host once, because `pp_init` runs once per process, with a temporary crash record folder, and stops it after the last test.
- **The main thread.** The test runner runs tests on worker threads, and the host's test builds take the thread that first calls `pp_init` as the main thread ([native-host.md](native-host.md) section 3). Every main-thread call of the tests therefore runs on one dedicated thread, `HostThread`. The tests run one at a time, because they share the host's handlers.
- **What they show.** The host implements the version of the bindings; the conformance test passes, and a planted wrong definition of `pp_memory_budget_t` is reported with its struct and each member that moved; every binding works against the library: the memory budget, a failed call with its status and message, a main-thread call on another thread, events and log lines on the event thread, an exception in a handler that is caught and reported while the host keeps running, text through host memory and `pp_free`, and a test crash record whose bytes match the documented layout.

## 9. Hand-over

- **M1.3** adapts `NativeHost` to `IRuntimeHost`: `ImplementationName` names the native host, `AbiVersion` comes from `ReadAbiVersion`, and `GetMemoryBudget` maps to Paneport.Core's `MemoryBudget`. It checks the version before `Initialize`, passes `Library/Caches/Paneport/` as the crash record folder, sets the handlers on the main thread and dispatches events to the UI thread, and points the failure handler at its log. Its crash record reader follows the table in [native-host.md](native-host.md) section 8, and its developer pages call `LayoutConformance.Check` and `HostTestExports.WriteTestCrashRecord`.
- **M2.2 and later groups** add their bindings here with their functions, their structs with their measurements in `ManagedLayouts`, and their event kinds.

## 10. Sources

Public material consulted (C-0001 section 6):

- P/Invoke source generation with `LibraryImport`: <https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation>
- Disabled runtime marshalling: <https://learn.microsoft.com/en-us/dotnet/standard/native-interop/disabled-marshalling>
- Native library loading and `NativeLibrary.SetDllImportResolver`: <https://learn.microsoft.com/en-us/dotnet/standard/native-interop/native-library-loading> and <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativelibrary.setdllimportresolver>
- `UnmanagedCallersOnlyAttribute`: <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.unmanagedcallersonlyattribute>
- `Unsafe.ByteOffset`: <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.unsafe.byteoffset>
- xUnit's shared context, assembly fixtures among it: <https://xunit.net/docs/shared-context>
- Research note `avalonia_ui_layer.md`, section 2.2, on frameworks, P/Invoke and callbacks under Mono and CoreCLR: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
