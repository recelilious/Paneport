// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Core.Compat;

/// <summary>
/// One test report of a title entry (C-0008 section 4).
/// </summary>
public sealed record TestReport
{
    /// <summary>Gets the test date in UTC.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>Gets the device model identifier, such as <c>iPhone18,1</c>.</summary>
    public required string Device { get; init; }

    /// <summary>Gets the iOS version and build, such as <c>27.2 (24B119)</c>.</summary>
    public required string IosVersion { get; init; }

    /// <summary>Gets the release tag, such as <c>v0.1.0</c>, or the full commit hash that was tested.</summary>
    public required string PaneportVersion { get; init; }

    /// <summary>
    /// Gets the Steam build ID of a Steam title, or the SHA-256 of the program file of a local
    /// title.
    /// </summary>
    public required string GameBuild { get; init; }

    /// <summary>Gets the rating that this test observed, which is never <see cref="CompatRating.Untested"/>.</summary>
    public required CompatRating Rating { get; init; }

    /// <summary>Gets the path of the evidence record under <c>docs/evidence/</c> (C-0012 section 4).</summary>
    public required string Evidence { get; init; }

    /// <summary>Gets the median frame rate, which a rating of runs or better requires.</summary>
    public double? FpsMedian { get; init; }

    /// <summary>Gets the 1% low frame rate, which a rating of runs or better requires.</summary>
    public double? FpsLow1 { get; init; }

    /// <summary>
    /// Gets the peak memory footprint of the whole process in MiB, which a rating of runs or better
    /// requires.
    /// </summary>
    public int? PeakMemoryMiB { get; init; }

    /// <summary>Gets the highest thermal state seen, if the report records it.</summary>
    public ThermalState? ThermalState { get; init; }

    /// <summary>Gets free-text notes, if any.</summary>
    public string? Notes { get; init; }
}
