// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

// The source-generated bindings of every export of paneport/host.h, paneport/jit.h,
// paneport/memory.h, paneport/session.h and paneport/testing.h (C-0005 section 1). Only blittable types cross: integers, pointers,
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

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_device_get_model")]
    internal static partial int DeviceGetModel(byte* buffer, uint bufferSize);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_jit_get_state")]
    internal static partial int JitGetState(NativeJitStatus* status);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_jit_set_enabler_opened")]
    internal static partial int JitSetEnablerOpened(byte opened);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_jit_note_foreground")]
    internal static partial int JitNoteForeground();

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_jit_request_pool")]
    internal static partial int JitRequestPool(uint sizeMiB);
    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_session_start")]
    internal static partial int SessionStart(NativeSessionConfig* config);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_session_stop")]
    internal static partial int SessionStop();

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_process_launch")]
    internal static partial int ProcessLaunch(NativeProcessConfig* config, ulong* process);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_process_get_state")]
    internal static partial int ProcessGetState(ulong process, NativeProcessState* state);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_process_terminate")]
    internal static partial int ProcessTerminate(ulong process, uint exitCode);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_get_layout_table")]
    internal static partial int TestGetLayoutTable(NativeLayoutStruct** table, uint* count);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_write_crash_record", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestWriteCrashRecord(string message);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_set_memory_stub")]
    internal static partial int TestSetMemoryStub(ulong availableBytes, ulong footprintBytes);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_jit_plant_status")]
    internal static partial int TestJitPlantStatus(uint pattern);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_post_event")]
    internal static partial int TestPostEvent(uint kind, long code);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestLog(int level, string text);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_log_with_source", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestLogWithSource(int level, uint source, ulong process, string text);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_log_with_flags", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestLogWithFlags(int level, uint source, ulong process, uint flags, ulong dropped, string text);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_duplicate_text",StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int TestDuplicateText(string text, byte** copy);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_signals_check_installed")]
    internal static partial int TestSignalsCheckInstalled(int* replacedSignal);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_signal_harness_start")]
    internal static partial int TestSignalHarnessStart(NativeSignalHarnessConfig* config);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_signal_harness_stop")]
    internal static partial int TestSignalHarnessStop();

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_signal_harness_read")]
    internal static partial int TestSignalHarnessRead(NativeSignalCounters* counters);

    [LibraryImport(NativeHost.LibraryName, EntryPoint = "pp_test_jit_set_ready")]
    internal static partial int TestJitSetReady(byte ready);
}
