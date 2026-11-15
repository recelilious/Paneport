// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Paneport.Core.Settings;

// The settings file as JSON: a version, so that a later layout can be told apart, and one field
// per setting of AppSettings. Fields that a later version adds are ignored by this one.
internal sealed class SettingsDocument
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }
}
