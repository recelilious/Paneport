// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The exports of the host's test builds (<c>paneport/testing.h</c>): the macOS test library
/// and the framework of the device test flavor. The framework of the main app exports none
/// of them, so a call there fails with <see cref="EntryPointNotFoundException"/>.
/// </summary>
public static unsafe class HostTestExports
{
    /// <summary>
    /// Reads the size and member offsets of every struct of the interface, as the host was
    /// compiled (<c>pp_test_get_layout_table</c>, C-0005 section 5). Any thread.
    /// </summary>
    /// <remarks>
    /// Each entry and each member of the table starts with its <c>struct_size</c> in the host,
    /// and the reader steps from one to the next by it, so a host whose table structs gained
    /// members at their end is read too.
    /// </remarks>
    /// <returns>One record per struct, with its members in declaration order.</returns>
    /// <exception cref="HostException">The host's table entries are smaller than these bindings read.</exception>
    public static IReadOnlyList<StructLayoutRecord> ReadLayoutTable()
    {
        NativeLayoutStruct* table = null;
        uint count = 0;
        HostCalls.Check(NativeMethods.TestGetLayoutTable(&table, &count));

        var structs = new List<StructLayoutRecord>((int)count);
        var entry = (byte*)table;
        for (var index = 0; index < count; index++)
        {
            var header = (NativeLayoutStruct*)entry;
            var entrySize = CheckedSize(header->StructSize, sizeof(NativeLayoutStruct), "pp_test_layout_struct_t");
            var records = new List<MemberLayoutRecord>((int)header->MemberCount);
            var member = (byte*)header->Members;
            for (var position = 0; position < header->MemberCount; position++)
            {
                var item = (NativeLayoutMember*)member;
                var memberSize = CheckedSize(item->StructSize, sizeof(NativeLayoutMember), "pp_test_layout_member_t");
                records.Add(new MemberLayoutRecord(HostCalls.Text(item->Name), (int)item->Offset, (int)item->Size));
                member += memberSize;
            }

            structs.Add(new StructLayoutRecord(HostCalls.Text(header->Name), (int)header->Size, records));
            entry += entrySize;
        }

        return structs;
    }

    // The host's struct_size of a table struct, which must hold at least what these bindings read.
    private static uint CheckedSize(uint hostSize, int knownSize, string name) => hostSize >= knownSize
        ? hostSize
        : throw new HostException(
            $"The host's {name} has {hostSize} bytes, fewer than the {knownSize} that Paneport.Interop reads.");

    /// <summary>
    /// Writes a crash record marked as a test, through the writer that a fatal fault uses
    /// (<c>pp_test_write_crash_record</c>). It needs a host started with a crash record folder.
    /// </summary>
    /// <param name="message">A short message; the host cuts it to 63 bytes of UTF-8.</param>
    public static void WriteTestCrashRecord(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        HostCalls.Check(NativeMethods.TestWriteCrashRecord(message));
    }

    /// <summary>
    /// Sets the readings of the memory stub that the host uses on macOS
    /// (<c>pp_test_set_memory_stub</c>). On iOS the host reads the system and refuses.
    /// </summary>
    /// <param name="availableBytes">The available memory to report.</param>
    /// <param name="footprintBytes">The footprint to report.</param>
    public static void SetMemoryStub(ulong availableBytes, ulong footprintBytes) =>
        HostCalls.Check(NativeMethods.TestSetMemoryStub(availableBytes, footprintBytes));

    /// <summary>
    /// Makes <see cref="NativeHost.GetJitStatus"/> report one of the three planted statuses that
    /// <c>paneport/testing.h</c> lists, in which the members can be told apart, or the provider's
    /// own status again (<c>pp_test_jit_plant_status</c>, version 1.3). The JIT state does not
    /// change. Any thread.
    /// </summary>
    /// <param name="pattern">1 to 3, the pattern to report; 0 for the provider's own status.</param>
    public static void PlantJitStatus(uint pattern) =>
        HostCalls.Check(NativeMethods.TestJitPlantStatus(pattern));

    /// <summary>Posts an event from the calling thread, as the host's components do (<c>pp_test_post_event</c>).</summary>
    /// <param name="kind">The event's kind; any value.</param>
    /// <param name="code">The event's code.</param>
    public static void PostEvent(uint kind, long code) => HostCalls.Check(NativeMethods.TestPostEvent(kind, code));

    /// <summary>Logs a line through the host's log pipeline, from the calling thread (<c>pp_test_log</c>).</summary>
    /// <param name="level">How important the line is.</param>
    /// <param name="text">The line; the host cuts a line that is too long.</param>
    public static void Log(HostLogLevel level, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        HostCalls.Check(NativeMethods.TestLog((int)level, text));
    }

    /// <summary>
    /// Copies a text into host memory and back, freeing the host's copy with <c>pp_free</c>
    /// (<c>pp_test_duplicate_text</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text, as it came back from host memory.</returns>
    public static string DuplicateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte* copy = null;
        HostCalls.Check(NativeMethods.TestDuplicateText(text, &copy));
        try
        {
            return HostCalls.Text(copy);
        }
        finally
        {
            NativeMethods.Free(copy);
        }
    }

    /// <summary>
    /// Checks that each of SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT still has the
    /// signal dispatcher's handler (<c>pp_test_signals_check_installed</c>, C-0005 section 4).
    /// Any thread.
    /// </summary>
    /// <returns>0 when all six have it, or the number of the first signal whose handler was replaced.</returns>
    /// <exception cref="HostException">The host has not started, so the dispatcher is not installed.</exception>
    public static int CheckSignalsInstalled()
    {
        var replaced = 0;
        var status = NativeMethods.TestSignalsCheckInstalled(&replaced);
        if (replaced != 0)
        {
            return replaced;
        }

        HostCalls.Check(status);
        return 0;
    }

    /// <summary>
    /// Starts the signal gate's native stress harness (<c>pp_test_signal_harness_start</c>,
    /// docs/design/signals.md section 9). It needs a started host and no other guest fault handler.
    /// Any thread.
    /// </summary>
    /// <param name="markedThreads">Threads marked as guest threads, which fault on a guard page and a read-only page; 0 to 16.</param>
    /// <param name="unmarkedThreads">Unmarked threads, which fault inside a registered guest reservation; 0 to 16.</param>
    /// <param name="pauseMicroseconds">Each thread's pause between two faults; at most one second.</param>
    public static void StartSignalHarness(uint markedThreads, uint unmarkedThreads, uint pauseMicroseconds)
    {
        var config = new NativeSignalHarnessConfig
        {
            StructSize = (uint)sizeof(NativeSignalHarnessConfig),
            MarkedThreads = markedThreads,
            UnmarkedThreads = unmarkedThreads,
            PauseMicroseconds = pauseMicroseconds,
        };
        HostCalls.Check(NativeMethods.TestSignalHarnessStart(&config));
    }

    /// <summary>
    /// Stops the stress harness; its counters keep their final values until the next start
    /// (<c>pp_test_signal_harness_stop</c>). Any thread.
    /// </summary>
    public static void StopSignalHarness() => HostCalls.Check(NativeMethods.TestSignalHarnessStop());

    /// <summary>
    /// Reads the counters of the stress harness and of the signal dispatcher
    /// (<c>pp_test_signal_harness_read</c>). Any thread.
    /// </summary>
    /// <returns>The counters.</returns>
    public static SignalHarnessCounters ReadSignalHarness()
    {
        var counters = new NativeSignalCounters { StructSize = (uint)sizeof(NativeSignalCounters) };
        HostCalls.Check(NativeMethods.TestSignalHarnessRead(&counters));
        return new SignalHarnessCounters(
            counters.Running != 0,
            counters.GuestFaultsRaisedByMark,
            counters.GuestFaultsHandledByMark,
            counters.GuestFaultsRaisedByAddress,
            counters.GuestFaultsHandledByAddress,
            counters.UnexpectedFaults,
            counters.RuntimeFaults);
    }

    /// <summary>
    /// Sets whether the JIT state counts as ready for a session to start besides the JIT
    /// provider's own state, which the macOS harness needs, where no pool exists
    /// (<c>pp_test_jit_set_ready</c>, version 1.4). Any thread.
    /// </summary>
    /// <param name="ready">True for ready.</param>
    public static void SetJitReady(bool ready) => HostCalls.Check(NativeMethods.TestJitSetReady(ready ? (byte)1 : (byte)0));
}
