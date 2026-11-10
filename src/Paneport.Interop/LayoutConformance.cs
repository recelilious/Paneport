// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The interface conformance test of C-0005 section 5: every struct's size and member offsets
/// must match between the host and these bindings. tests/Paneport.Interop.Tests runs it on
/// macOS against the host test library, and the device test flavor runs it on the device.
/// </summary>
public static class LayoutConformance
{
    /// <summary>Measures the C# definition of every struct that crosses the interface.</summary>
    /// <returns>One record per struct, named after its C type.</returns>
    public static IReadOnlyList<StructLayoutRecord> ManagedLayouts() =>
    [
        new StructLayoutBuilder<NativeInitConfig>("pp_init_config_t")
            .Member("struct_size", (ref NativeInitConfig value) => ref value.StructSize)
            .Member("crash_record_folder", (ref NativeInitConfig value) => ref value.CrashRecordFolder)
            .Build(),
        new StructLayoutBuilder<NativeEvent>("pp_event_t")
            .Member("struct_size", (ref NativeEvent value) => ref value.StructSize)
            .Member("kind", (ref NativeEvent value) => ref value.Kind)
            .Member("code", (ref NativeEvent value) => ref value.Code)
            .Member("handle", (ref NativeEvent value) => ref value.Handle)
            .Member("address", (ref NativeEvent value) => ref value.Address)
            .Build(),
        new StructLayoutBuilder<NativeLogLine>("pp_log_line_t")
            .Member("struct_size", (ref NativeLogLine value) => ref value.StructSize)
            .Member("level", (ref NativeLogLine value) => ref value.Level)
            .Member("thread_id", (ref NativeLogLine value) => ref value.ThreadId)
            .Member("text", (ref NativeLogLine value) => ref value.Text)
            .Build(),
        new StructLayoutBuilder<NativeMemoryBudget>("pp_memory_budget_t")
            .Member("struct_size", (ref NativeMemoryBudget value) => ref value.StructSize)
            .Member("game_mode_level_reached", (ref NativeMemoryBudget value) => ref value.GameModeLevelReached)
            .Member("limit_bytes", (ref NativeMemoryBudget value) => ref value.LimitBytes)
            .Member("available_bytes", (ref NativeMemoryBudget value) => ref value.AvailableBytes)
            .Member("footprint_bytes", (ref NativeMemoryBudget value) => ref value.FootprintBytes)
            .Build(),
        new StructLayoutBuilder<NativeLayoutMember>("pp_test_layout_member_t")
            .Member("name", (ref NativeLayoutMember value) => ref value.Name)
            .Member("offset", (ref NativeLayoutMember value) => ref value.Offset)
            .Member("size", (ref NativeLayoutMember value) => ref value.Size)
            .Build(),
        new StructLayoutBuilder<NativeLayoutStruct>("pp_test_layout_struct_t")
            .Member("name", (ref NativeLayoutStruct value) => ref value.Name)
            .Member("size", (ref NativeLayoutStruct value) => ref value.Size)
            .Member("member_count", (ref NativeLayoutStruct value) => ref value.MemberCount)
            .Member("members", (ref NativeLayoutStruct value) => ref value.Members)
            .Build(),
    ];

    /// <summary>Compares the C# layouts with the host's and describes every difference.</summary>
    /// <param name="managed">The layouts of the C# definitions.</param>
    /// <param name="native">The layouts of the host, from its layout table.</param>
    /// <returns>One line per difference, naming its struct and member; empty when they match.</returns>
    public static IReadOnlyList<string> Compare(
        IReadOnlyList<StructLayoutRecord> managed, IReadOnlyList<StructLayoutRecord> native)
    {
        ArgumentNullException.ThrowIfNull(managed);
        ArgumentNullException.ThrowIfNull(native);
        var problems = new List<string>();
        foreach (var expected in native)
        {
            var actual = managed.FirstOrDefault(layout => layout.Name == expected.Name);
            if (actual is null)
            {
                problems.Add($"{expected.Name}: the host defines it, but Paneport.Interop does not.");
                continue;
            }

            if (actual.Size != expected.Size)
            {
                problems.Add($"{expected.Name}: {actual.Size} bytes in C#, {expected.Size} bytes in C.");
            }

            foreach (var member in expected.Members)
            {
                var other = actual.Members.FirstOrDefault(candidate => candidate.Name == member.Name);
                if (other is null)
                {
                    problems.Add($"{expected.Name}.{member.Name}: missing in C#.");
                }
                else if (other.Offset != member.Offset || other.Size != member.Size)
                {
                    problems.Add(
                        $"{expected.Name}.{member.Name}: offset {other.Offset} and size {other.Size} in C#, "
                            + $"offset {member.Offset} and size {member.Size} in C.");
                }
            }

            foreach (var extra in actual.Members.Where(candidate => expected.Members.All(member => member.Name != candidate.Name)))
            {
                problems.Add($"{expected.Name}.{extra.Name}: only in C#.");
            }
        }

        foreach (var extra in managed.Where(layout => native.All(candidate => candidate.Name != layout.Name)))
        {
            problems.Add($"{extra.Name}: only in C#.");
        }

        return problems;
    }

    /// <summary>
    /// Runs the conformance test against the host that this process loaded, which must be a
    /// test build of the host.
    /// </summary>
    /// <returns>Every difference; empty when the layouts match.</returns>
    public static IReadOnlyList<string> Check() => Compare(ManagedLayouts(), HostTestExports.ReadLayoutTable());
}
