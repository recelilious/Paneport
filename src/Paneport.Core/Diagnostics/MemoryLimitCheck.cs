// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Tells whether a memory limit is far enough below the expected one to mean that the install
/// dropped <c>increased-memory-limit</c> (<c>ios_platform_constraints.md</c> section 4). The
/// threshold is set in docs/design/ios-app-head.md section 8.
/// </summary>
public static class MemoryLimitCheck
{
    /// <summary>The bytes of a MB, the unit that C-0005 and C-0009 use for the memory limit.</summary>
    public const ulong BytesPerMegabyte = 1024 * 1024;

    /// <summary>
    /// The limit that an iPhone with 12 GB gives an app with <c>increased-memory-limit</c> before
    /// Game Mode: 6,144 MB (<c>process_and_memory_model.md</c>).
    /// </summary>
    public const ulong ExpectedLimitBytes = 6_144 * BytesPerMegabyte;

    /// <summary>
    /// The limit below which Diagnostics warns: 512 MB below the expected limit, far above the
    /// 3.3 GB measured without the entitlement on 8 GB iPhones.
    /// </summary>
    public const ulong WarningThresholdBytes = 5_632 * BytesPerMegabyte;

    /// <summary>Checks a limit.</summary>
    /// <param name="limitBytes">The memory limit that the host reported.</param>
    /// <returns>True when the limit is below <see cref="WarningThresholdBytes"/>.</returns>
    public static bool IsFarBelowExpected(ulong limitBytes) => limitBytes < WarningThresholdBytes;
}
