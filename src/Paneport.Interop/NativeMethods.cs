// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// The source-generated bindings of every export of paneport/host.h, paneport/memory.h and
// paneport/testing.h (C-0005 section 1). Only blittable types cross: integers, pointers,
// function pointers and the structs of this assembly; strings go in as UTF-8 through the
// generated code. A test export exists only in the host's test builds, and the runtime looks
// it up when it is first called, so binding it costs the main app nothing.
internal static unsafe partial class NativeMethods
{
    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_last_error")]
    internal static partial byte* LastError();

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_free")]
    internal static partial void Free(void* memory);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_init")]
    internal static partial int Init(NativeInitConfig* config);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_shutdown")]
    internal static partial int Shutdown();

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_set_event_callback")]
    internal static partial int SetEventCallback(
        delegate* unmanaged[Cdecl]<NativeEvent*, void*, void> callback, void* userData);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_set_log_callback")]
    internal static partial int SetLogCallback(
        delegate* unmanaged[Cdecl]<NativeLogLine*, void*, void> callback, void* userData);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_memory_get_budget")]
    internal static partial int MemoryGetBudget(NativeMemoryBudget* budget);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_get_layout_table")]
    internal static partial int TestGetLayoutTable(NativeLayoutStruct** table, uint* count);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_write_crash_record", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestWriteCrashRecord(string message);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_set_memory_stub")]
    internal static partial int TestSetMemoryStub(ulong availableBytes, ulong footprintBytes);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_post_event")]
    internal static partial int TestPostEvent(uint kind, long code);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestLog(int level, string text);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_duplicate_text", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestDuplicateText(string text, byte** copy);
}
