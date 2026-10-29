// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// The memory figures that the host reports through <c>pp_memory_get_budget</c>
/// (C-0005 section 2). All sizes are in bytes.
/// </summary>
/// <remarks>
/// The memory limit is the host's estimate: the available memory plus the current footprint,
/// sampled again over time. Wherever a contract says "memory limit", it means this value.
/// </remarks>
public readonly record struct MemoryBudget
{
    /// <summary>Creates a budget.</summary>
    /// <param name="limitBytes">The memory limit.</param>
    /// <param name="availableBytes">The memory still available to the app.</param>
    /// <param name="footprintBytes">The app's current memory footprint.</param>
    /// <param name="gameModeLevelReached">Whether the limit has reached the Game Mode level.</param>
    public MemoryBudget(ulong limitBytes, ulong availableBytes, ulong footprintBytes, bool gameModeLevelReached)
    {
        LimitBytes = limitBytes;
        AvailableBytes = availableBytes;
        FootprintBytes = footprintBytes;
        GameModeLevelReached = gameModeLevelReached;
    }

    /// <summary>Gets the memory limit.</summary>
    public ulong LimitBytes { get; }

    /// <summary>Gets the memory still available to the app.</summary>
    public ulong AvailableBytes { get; }

    /// <summary>Gets the app's current memory footprint.</summary>
    public ulong FootprintBytes { get; }

    /// <summary>
    /// Gets whether the limit has reached the Game Mode level of about 8,192 MB
    /// (C-0005 section 2).
    /// </summary>
    public bool GameModeLevelReached { get; }
}
