// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The classification of C-0007 section 5, the <c>drm</c> object (C-0008 section 2). It only
/// describes a title; nothing in it skips, satisfies or replaces a Steam or DRM check.
/// </summary>
/// <remarks>
/// A null member is not classified yet. Each member is a value of its own: a reviewed entry may
/// set some members by hand, such as <see cref="Ceg"/>, while the device's classification fills
/// in the others (C-0008 section 1).
/// </remarks>
public sealed record DrmClassification
{
    /// <summary>
    /// Gets whether <c>steam_api.dll</c> or <c>steam_api64.dll</c> is in the install folder or
    /// imported by a program in it.
    /// </summary>
    public bool? SteamApi { get; init; }

    /// <summary>Gets whether the main program carries a SteamStub wrapper.</summary>
    public bool? SteamStub { get; init; }

    /// <summary>Gets whether the title ships a <c>steam_appid.txt</c> file. It is recorded only.</summary>
    public bool? SteamAppIdFile { get; init; }

    /// <summary>Gets whether the title uses Steam's Custom Executable Generation.</summary>
    public CegStatus? Ceg { get; init; }
}
