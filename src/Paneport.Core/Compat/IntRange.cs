// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

// The range of an integer field. Reading a file and checking an entry made in code report a value
// outside it with the same message.
internal sealed record IntRange(long Minimum, long Maximum, string Message)
{
    public static IntRange SteamAppId { get; } = new(1, uint.MaxValue, "steamAppId must be from 1 to 4294967295.");

    public static IntRange MemoryEstimateMiB { get; } = new(1, int.MaxValue, "memoryEstimateMiB must be at least 1.");

    public static IntRange JitPoolMiB { get; } = new(256, 896, "jitPoolMiB must be from 256 to 896.");

    public static IntRange FrameRateCap { get; } = new(1, int.MaxValue, "frameRateCap must be at least 1.");

    public static IntRange PeakMemoryMiB { get; } = new(1, int.MaxValue, "peakMemoryMiB must be at least 1.");

    public bool Contains(long value) => value >= Minimum && value <= Maximum;
}
