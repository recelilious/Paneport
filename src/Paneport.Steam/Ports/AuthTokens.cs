// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Steam.Ports;

// What a confirmed sign-in gives: the account's name and a refresh token. It is a class, not a
// record, so that no generated ToString shows the account name.
internal sealed class AuthTokens
{
    public AuthTokens(string accountName, Secret refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        ArgumentNullException.ThrowIfNull(refreshToken);
        AccountName = accountName;
        RefreshToken = refreshToken;
    }

    public string AccountName { get; }

    public Secret RefreshToken { get; }

    public override string ToString() => $"{nameof(AuthTokens)} {Secret.Redacted}";
}
