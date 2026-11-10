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
}
