// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Paneport.Core.Security;

// The bytes of the keychain item that SecureTokenStore keeps, as JSON. It is a class, not a
// record, so that no generated ToString can show its values.
internal sealed class SignInItem
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("accountName")]
    public string? AccountName { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }
}
