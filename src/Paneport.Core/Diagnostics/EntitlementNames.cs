// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>
/// The entitlements of C-0009 section 4, the only ones that Diagnostics reads. Every other
/// entitlement and every other value of a signature or a profile is discarded unread.
/// </summary>
public static class EntitlementNames
{
    /// <summary>The debugger entitlement that the development profile gives (C-0006).</summary>
    public const string GetTaskAllow = "get-task-allow";

    /// <summary>The entitlement that raises the memory limit (C-0009 section 4).</summary>
    public const string IncreasedMemoryLimit = "com.apple.developer.kernel.increased-memory-limit";

    /// <summary>The entitlement for a larger address space, which the default build does not declare.</summary>
    public const string ExtendedVirtualAddressing = "com.apple.developer.kernel.extended-virtual-addressing";

    /// <summary>Gets the three names, in the order of C-0009 section 4.</summary>
    public static IReadOnlyList<string> All { get; } = [GetTaskAllow, IncreasedMemoryLimit, ExtendedVirtualAddressing];
}
