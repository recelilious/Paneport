// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The memory figures that <c>pp_memory_get_budget</c> reports (C-0005 section 2), in bytes.
/// The memory limit is the host's estimate: the available memory plus the footprint.
/// </summary>
/// <param name="LimitBytes">The memory limit.</param>
/// <param name="AvailableBytes">The memory still available to the app.</param>
/// <param name="FootprintBytes">The app's current memory footprint.</param>
/// <param name="GameModeLevelReached">Whether the limit has reached the Game Mode level of about 8,192 MB.</param>
public readonly record struct HostMemoryBudget(
    ulong LimitBytes, ulong AvailableBytes, ulong FootprintBytes, bool GameModeLevelReached);
