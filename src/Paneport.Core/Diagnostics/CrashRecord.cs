// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>
/// A crash record that the native host wrote when a fatal fault ended the app (C-0005 section 2),
/// with the fields of version 1 of the layout in docs/design/native-host.md section 8.
/// </summary>
/// <param name="Time">When the host wrote the record, in UTC.</param>
/// <param name="Signal">The signal number; 0 for a test record.</param>
/// <param name="SignalCode">The signal's code (<c>si_code</c>).</param>
/// <param name="ThreadRole">The role of the thread that faulted: 0 other, 1 main, 2 host event thread, 3 guest thread, 4 translator thread.</param>
/// <param name="FaultAddress">The fault address (<c>si_addr</c>).</param>
/// <param name="ProgramCounter">The program counter of the faulting instruction.</param>
/// <param name="IsTest">Whether a test wrote the record.</param>
/// <param name="Message">The host's short message.</param>
public sealed record CrashRecord(
    DateTimeOffset Time,
    int Signal,
    int SignalCode,
    uint ThreadRole,
    ulong FaultAddress,
    ulong ProgramCounter,
    bool IsTest,
    string Message)
{
    /// <summary>Gets the thread role's name.</summary>
    public string ThreadRoleName => ThreadRole switch
    {
        0 => "other",
        1 => "main thread",
        2 => "host event thread",
        3 => "guest thread",
        4 => "translator thread",
        _ => $"role {ThreadRole}",
    };

    /// <summary>Gets the signal's name on Darwin, such as <c>SIGSEGV</c>, or its number.</summary>
    public string SignalName => Signal switch
    {
        0 => "none (test record)",
        4 => "SIGILL",
        5 => "SIGTRAP",
        6 => "SIGABRT",
        8 => "SIGFPE",
        10 => "SIGBUS",
        11 => "SIGSEGV",
        _ => $"signal {Signal}",
    };
}
