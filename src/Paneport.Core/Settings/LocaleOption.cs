// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Settings;

/// <summary>
/// A locale that the Settings page offers: a BCP 47 language tag and its English display name.
/// </summary>
/// <remarks>
/// The names are written out here instead of coming from <see cref="System.Globalization.CultureInfo"/>,
/// because the iOS build may run in globalization-invariant mode, where most cultures are missing.
/// </remarks>
public sealed record LocaleOption
{
    /// <summary>Creates a locale option.</summary>
    /// <param name="tag">The BCP 47 language tag, such as <c>en-US</c>.</param>
    /// <param name="displayName">The English display name, such as <c>English (United States)</c>.</param>
    public LocaleOption(string tag, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Tag = tag;
        DisplayName = displayName;
    }

    /// <summary>Gets the locales that the Settings page offers, sorted by display name.</summary>
    public static IReadOnlyList<LocaleOption> Supported { get; } =
    [
        new("zh-CN", "Chinese (Simplified, China)"),
        new("zh-TW", "Chinese (Traditional, Taiwan)"),
        new("en-GB", "English (United Kingdom)"),
        new("en-US", "English (United States)"),
        new("fr-FR", "French (France)"),
        new("de-DE", "German (Germany)"),
        new("it-IT", "Italian (Italy)"),
        new("ja-JP", "Japanese (Japan)"),
        new("ko-KR", "Korean (Korea)"),
        new("pl-PL", "Polish (Poland)"),
        new("pt-BR", "Portuguese (Brazil)"),
        new("ru-RU", "Russian (Russia)"),
        new("es-ES", "Spanish (Spain)"),
    ];

    /// <summary>Gets the BCP 47 language tag.</summary>
    public string Tag { get; }

    /// <summary>Gets the English display name.</summary>
    public string DisplayName { get; }

    /// <summary>Finds a supported locale by its tag.</summary>
    /// <param name="tag">The BCP 47 language tag; case does not matter.</param>
    /// <returns>The supported locale with that tag, or null when there is none.</returns>
    public static LocaleOption? Find(string? tag) =>
        Supported.FirstOrDefault(option => string.Equals(option.Tag, tag, StringComparison.OrdinalIgnoreCase));
}
