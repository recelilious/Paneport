// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The <c>id</c> object of a title entry (C-0008 section 2). A Steam title has
/// <see cref="SteamAppId"/> and a local title <see cref="ExeSha256"/>, never both.
/// </summary>
public sealed record TitleEntryId
{
    /// <summary>Gets the Steam app ID of a Steam title, or null for a local title.</summary>
    public uint? SteamAppId { get; init; }

    /// <summary>
    /// Gets the SHA-256 values, in lowercase hexadecimal, of the program that the entry's
    /// executable names, one per known build of a local title; null for a Steam title.
    /// </summary>
    public IReadOnlyList<string>? ExeSha256 { get; init; }

    /// <summary>
    /// Gets the slug, which every title has. A local title's file is named after it
    /// (C-0008 section 1).
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>Gets the identity that names the title's file.</summary>
    /// <returns>
    /// <c>steam-&lt;appid&gt;</c> when <see cref="SteamAppId"/> is set, otherwise
    /// <c>local-&lt;slug&gt;</c>.
    /// </returns>
    /// <exception cref="ArgumentException">The identity is not a valid one.</exception>
    public TitleId ToTitleId() => SteamAppId is { } appId
        ? TitleId.Steam(appId)
        : TitleId.Local(Slug);
}
