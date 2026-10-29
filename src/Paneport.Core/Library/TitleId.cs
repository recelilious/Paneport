// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Paneport.Core.Library;

/// <summary>
/// The identity of a title, written the way C-0008 section 1 names the title's compatibility
/// file: <c>steam-&lt;appid&gt;</c> for a Steam title and <c>local-&lt;slug&gt;</c> for any other.
/// </summary>
/// <remarks>
/// A slug is one or more groups of lowercase ASCII letters and digits joined by single hyphens,
/// so it never starts or ends with a hyphen. An app ID is never 0 and is written without leading
/// zeros, so that each title has exactly one identity text.
/// </remarks>
public sealed record TitleId
{
    private const string SteamPrefix = "steam-";
    private const string LocalPrefix = "local-";

    private TitleId(TitleSource source, uint? steamAppId, string? slug)
    {
        Source = source;
        SteamAppId = steamAppId;
        Slug = slug;
    }

    /// <summary>Gets where the title comes from.</summary>
    public TitleSource Source { get; }

    /// <summary>Gets the Steam app ID of a Steam title, or null for a local title.</summary>
    public uint? SteamAppId { get; }

    /// <summary>Gets the slug of a local title, or null for a Steam title.</summary>
    public string? Slug { get; }

    /// <summary>Creates the identity of a Steam title.</summary>
    /// <param name="appId">The title's Steam app ID, which is never 0.</param>
    /// <returns>The identity <c>steam-&lt;appId&gt;</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="appId"/> is 0.</exception>
    public static TitleId Steam(uint appId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(appId);
        return new TitleId(TitleSource.Steam, appId, null);
    }

    /// <summary>Creates the identity of a title that the user brought in without Steam.</summary>
    /// <param name="slug">The title's slug; see <see cref="IsValidSlug"/>.</param>
    /// <returns>The identity <c>local-&lt;slug&gt;</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="slug"/> is not a valid slug.</exception>
    public static TitleId Local(string slug)
    {
        if (!IsValidSlug(slug))
        {
            throw new ArgumentException(
                "A slug is lowercase ASCII letters and digits joined by single hyphens.", nameof(slug));
        }

        return new TitleId(TitleSource.Local, null, slug);
    }

    /// <summary>Checks a local slug against C-0008 section 1.</summary>
    /// <param name="slug">The text to check.</param>
    /// <returns>
    /// True when the text is one or more groups of lowercase ASCII letters and digits joined by
    /// single hyphens.
    /// </returns>
    public static bool IsValidSlug([NotNullWhen(true)] string? slug)
    {
        if (string.IsNullOrEmpty(slug))
        {
            return false;
        }

        // Starting as if after a hyphen rejects a leading hyphen and a double one alike.
        var afterHyphen = true;
        foreach (var character in slug)
        {
            if (character == '-')
            {
                if (afterHyphen)
                {
                    return false;
                }

                afterHyphen = true;
            }
            else if (char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character))
            {
                afterHyphen = false;
            }
            else
            {
                return false;
            }
        }

        return !afterHyphen;
    }

    /// <summary>Reads an identity text such as <c>steam-480</c> or <c>local-my-game</c>.</summary>
    /// <param name="text">The text to read.</param>
    /// <param name="id">The identity, when the text is one.</param>
    /// <returns>True when the text is the identity text of a title.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out TitleId? id)
    {
        id = null;
        if (text is null)
        {
            return false;
        }

        if (text.StartsWith(SteamPrefix, StringComparison.Ordinal))
        {
            var digits = text.AsSpan(SteamPrefix.Length);

            // A leading zero would give a second text for the same app ID, and app ID 0 is invalid.
            if (digits.Length > 0 && digits[0] != '0'
                && uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var appId))
            {
                id = Steam(appId);
            }
        }
        else if (text.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            var slug = text[LocalPrefix.Length..];
            if (IsValidSlug(slug))
            {
                id = Local(slug);
            }
        }

        return id is not null;
    }

    /// <summary>Reads an identity text such as <c>steam-480</c> or <c>local-my-game</c>.</summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The identity.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> is not the identity text of a title.</exception>
    public static TitleId Parse(string text) =>
        TryParse(text, out var id) ? id : throw new FormatException($"'{text}' is not the identity of a title.");

    /// <summary>Gets the identity text, which is also the stem of the title's compatibility file name.</summary>
    /// <returns><c>steam-&lt;appid&gt;</c> or <c>local-&lt;slug&gt;</c>.</returns>
    public override string ToString() => Source == TitleSource.Steam
        ? string.Create(CultureInfo.InvariantCulture, $"{SteamPrefix}{SteamAppId}")
        : LocalPrefix + Slug;
}
