// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Settings;

/// <summary>
/// The app settings that the settings store keeps. <c>docs/design/app-shell.md</c> lists every
/// setting.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Gets the settings that apply until the user changes one.</summary>
    public static AppSettings Default { get; } = new() { Locale = "en-US" };

    /// <summary>
    /// Gets the locale that game sessions hand to Wine, as a BCP 47 language tag such as
    /// <c>en-US</c> (C-0005 section 2).
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public required string Locale
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }
}
