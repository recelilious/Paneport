<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Interop

Source-generated bindings to the native host interface (C-0004 section 2, C-0005). It is the only project allowed to use unsafe code (C-0010 section 2).

- **Depends on:** nothing else in the solution.
- **Build:** a library with `IsAotCompatible`, `AllowUnsafeBlocks`, `DisableRuntimeMarshalling` and an XML documentation file (C-0010 sections 2 and 5).
- **Tests:** [tests/Paneport.Interop.Tests](../../tests/Paneport.Interop.Tests/README.md), which run on macOS against the host's test library, so they stay out of `eng/ci.slnf` (C-0011 section 4).
- **Design:** [interop.md](../../docs/design/interop.md).

| File | What it holds |
|---|---|
| `NativeHost.cs` | The safe API: the interface version, `Initialize` and `Shutdown`, the event and log handlers, the failure handler for exceptions in them, and the memory budget |
| `HostTestExports.cs` | The exports of the host's test builds: the layout table, a test crash record, the memory stub, and helpers for the binding tests |
| `LayoutConformance.cs` | The interface conformance test of C-0005 section 5, with `StructLayoutBuilder.cs`, `FieldAccessor.cs`, `StructLayoutRecord.cs` and `MemberLayoutRecord.cs` |
| `HostAbi.cs`, `HostStatus.cs`, `HostException.cs`, `HostAbiMismatchException.cs` | The interface version these bindings know, the host's status codes, and the two exceptions |
| `HostEvent.cs`, `HostLogLine.cs`, `HostLogLevel.cs`, `HostMemoryBudget.cs` | What the host reports, as records |
| `NativeMethods.cs`, `HostCalls.cs` | The `LibraryImport` declarations, and turning a status into an exception |
| `Native*.cs` | The blittable structs that cross the interface, member by member as the C headers declare them |
