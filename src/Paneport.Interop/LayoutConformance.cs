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
            .Member("source", (ref NativeLogLine value) => ref value.Source)
            .Member("process", (ref NativeLogLine value) => ref value.Process)
            .Member("flags", (ref NativeLogLine value) => ref value.Flags)
            .Member("dropped", (ref NativeLogLine value) => ref value.Dropped)
            .Build(),
        new StructLayoutBuilder<NativeMemoryBudget>("pp_memory_budget_t")
            .Member("struct_size", (ref NativeMemoryBudget value) => ref value.StructSize)
            .Member("game_mode_level_reached", (ref NativeMemoryBudget value) => ref value.GameModeLevelReached)
            .Member("limit_bytes", (ref NativeMemoryBudget value) => ref value.LimitBytes)
            .Member("available_bytes", (ref NativeMemoryBudget value) => ref value.AvailableBytes)
            .Member("footprint_bytes", (ref NativeMemoryBudget value) => ref value.FootprintBytes)
            .Build(),
        new StructLayoutBuilder<NativeJitStatus>("pp_jit_status_t")
            .Member("struct_size", (ref NativeJitStatus value) => ref value.StructSize)
            .Member("state", (ref NativeJitStatus value) => ref value.State)
            .Member("failure", (ref NativeJitStatus value) => ref value.Failure)
            .Member("debugged_since_launch", (ref NativeJitStatus value) => ref value.DebuggedSinceLaunch)
            .Member("attached_now", (ref NativeJitStatus value) => ref value.AttachedNow)
            .Member("enabler_opened", (ref NativeJitStatus value) => ref value.EnablerOpened)
            .Member("foreign_debugger", (ref NativeJitStatus value) => ref value.ForeignDebugger)
            .Member("requested_mib", (ref NativeJitStatus value) => ref value.RequestedMiB)
            .Member("pool_mib", (ref NativeJitStatus value) => ref value.PoolMiB)
            .Member("regions_planned", (ref NativeJitStatus value) => ref value.RegionsPlanned)
            .Member("regions_mapped", (ref NativeJitStatus value) => ref value.RegionsMapped)
            .Member("in_state_ms", (ref NativeJitStatus value) => ref value.InStateMilliseconds)
            .Member("debugger_attached_ms", (ref NativeJitStatus value) => ref value.DebuggerAttachedMilliseconds)
            .Member("requesting_ms", (ref NativeJitStatus value) => ref value.RequestingMilliseconds)
            .Member("waiting_detach_ms", (ref NativeJitStatus value) => ref value.WaitingDetachMilliseconds)
            .Member("verifying_ms", (ref NativeJitStatus value) => ref value.VerifyingMilliseconds)
            .Member("authorization_ms", (ref NativeJitStatus value) => ref value.AuthorizationMilliseconds)
            .Build(),
        new StructLayoutBuilder<NativeSessionConfig>("pp_session_config_t")
            .Member("struct_size", (ref NativeSessionConfig value) => ref value.StructSize)
            .Member("architecture", (ref NativeSessionConfig value) => ref value.Architecture)
            .Member("prefix_folder", (ref NativeSessionConfig value) => ref value.PrefixFolder)
            .Member("runtime_folder", (ref NativeSessionConfig value) => ref value.RuntimeFolder)
            .Member("environment", (ref NativeSessionConfig value) => ref value.Environment)
            .Build(),
        new StructLayoutBuilder<NativeProcessConfig>("pp_process_config_t")
            .Member("struct_size", (ref NativeProcessConfig value) => ref value.StructSize)
            .Member("capture_output", (ref NativeProcessConfig value) => ref value.CaptureOutput)
            .Member("program_folder", (ref NativeProcessConfig value) => ref value.ProgramFolder)
            .Member("program", (ref NativeProcessConfig value) => ref value.Program)
            .Member("arguments", (ref NativeProcessConfig value) => ref value.Arguments)
            .Build(),
        new StructLayoutBuilder<NativeProcessState>("pp_process_state_t")
            .Member("struct_size", (ref NativeProcessState value) => ref value.StructSize)
            .Member("state", (ref NativeProcessState value) => ref value.State)
            .Member("exit_code", (ref NativeProcessState value) => ref value.ExitCode)
            .Build(),
        new StructLayoutBuilder<NativeLayoutMember>("pp_test_layout_member_t")
            .Member("struct_size", (ref NativeLayoutMember value) => ref value.StructSize)
            .Member("name", (ref NativeLayoutMember value) => ref value.Name)
            .Member("offset", (ref NativeLayoutMember value) => ref value.Offset)
            .Member("size", (ref NativeLayoutMember value) => ref value.Size)
            .Build(),
        new StructLayoutBuilder<NativeLayoutStruct>("pp_test_layout_struct_t")
            .Member("struct_size", (ref NativeLayoutStruct value) => ref value.StructSize)
            .Member("name", (ref NativeLayoutStruct value) => ref value.Name)
            .Member("size", (ref NativeLayoutStruct value) => ref value.Size)
            .Member("member_count", (ref NativeLayoutStruct value) => ref value.MemberCount)
            .Member("members", (ref NativeLayoutStruct value) => ref value.Members)
            .Build(),
        new StructLayoutBuilder<NativeSignalHarnessConfig>("pp_test_signal_harness_config_t")
            .Member("struct_size", (ref NativeSignalHarnessConfig value) => ref value.StructSize)
            .Member("marked_threads", (ref NativeSignalHarnessConfig value) => ref value.MarkedThreads)
            .Member("unmarked_threads", (ref NativeSignalHarnessConfig value) => ref value.UnmarkedThreads)
            .Member("pause_microseconds", (ref NativeSignalHarnessConfig value) => ref value.PauseMicroseconds)
            .Build(),
        new StructLayoutBuilder<NativeSignalCounters>("pp_test_signal_counters_t")
            .Member("struct_size", (ref NativeSignalCounters value) => ref value.StructSize)
            .Member("running", (ref NativeSignalCounters value) => ref value.Running)
            .Member("guest_faults_raised_by_mark", (ref NativeSignalCounters value) => ref value.GuestFaultsRaisedByMark)
            .Member("guest_faults_handled_by_mark", (ref NativeSignalCounters value) => ref value.GuestFaultsHandledByMark)
            .Member("guest_faults_raised_by_address", (ref NativeSignalCounters value) => ref value.GuestFaultsRaisedByAddress)
            .Member("guest_faults_handled_by_address", (ref NativeSignalCounters value) => ref value.GuestFaultsHandledByAddress)
            .Member("unexpected_faults", (ref NativeSignalCounters value) => ref value.UnexpectedFaults)
            .Member("runtime_faults", (ref NativeSignalCounters value) => ref value.RuntimeFaults)
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
