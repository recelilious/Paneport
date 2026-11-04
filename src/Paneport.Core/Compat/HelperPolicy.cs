// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The <c>helperPolicy</c> object of a title entry (C-0008 section 2).
/// </summary>
public sealed record HelperPolicy
{
    /// <summary>
    /// Gets the helper programs that the session refuses to start, such as crash reporters, as
    /// paths relative to the install folder; null when the entry sets none. Never a program that
    /// performs a license or DRM check (C-0008 section 5).
    /// </summary>
    public IReadOnlyList<string>? BlockedExecutables { get; init; }
}
