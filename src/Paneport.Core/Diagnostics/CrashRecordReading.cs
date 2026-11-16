// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>The result of reading the crash record file.</summary>
public sealed class CrashRecordReading
{
    private CrashRecordReading(CrashRecordStatus status, CrashRecord? record, string? problem)
    {
        Status = status;
        Record = record;
        Problem = problem;
    }

    /// <summary>Gets the reading for no record.</summary>
    public static CrashRecordReading None { get; } = new(CrashRecordStatus.None, null, null);

    /// <summary>Gets what the reader found.</summary>
    public CrashRecordStatus Status { get; }

    /// <summary>Gets the record, when it is valid.</summary>
    public CrashRecord? Record { get; }

    /// <summary>Gets why the file is malformed, when it is.</summary>
    public string? Problem { get; }

    /// <summary>Creates the reading of a valid record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The reading.</returns>
    public static CrashRecordReading Valid(CrashRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new(CrashRecordStatus.Valid, record, null);
    }

    /// <summary>Creates the reading of a malformed file.</summary>
    /// <param name="problem">Why the file is not a record, in fixed text.</param>
    /// <returns>The reading.</returns>
    public static CrashRecordReading Malformed(string problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(problem);
        return new(CrashRecordStatus.Malformed, null, problem);
    }
}
