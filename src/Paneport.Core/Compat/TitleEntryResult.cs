// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The outcome of reading one title file: the entry, or the rules it breaks.
/// </summary>
public sealed class TitleEntryResult
{
    private TitleEntryResult(TitleEntryStatus status, TitleEntry? entry, IReadOnlyList<CompatProblem> problems)
    {
        Status = status;
        Entry = entry;
        Problems = problems;
    }

    /// <summary>Gets the result for a layer that has no file for the title.</summary>
    public static TitleEntryResult Missing { get; } = new(TitleEntryStatus.Missing, null, []);

    /// <summary>Gets the outcome.</summary>
    public TitleEntryStatus Status { get; }

    /// <summary>Gets the entry when the status is <see cref="TitleEntryStatus.Loaded"/>; otherwise null.</summary>
    public TitleEntry? Entry { get; }

    /// <summary>
    /// Gets the rules the file breaks when the status is <see cref="TitleEntryStatus.Rejected"/>;
    /// otherwise empty.
    /// </summary>
    public IReadOnlyList<CompatProblem> Problems { get; }

    internal static TitleEntryResult Loaded(TitleEntry entry) => new(TitleEntryStatus.Loaded, entry, []);

    internal static TitleEntryResult Rejected(IReadOnlyList<CompatProblem> problems) =>
        new(TitleEntryStatus.Rejected, null, problems);
}
