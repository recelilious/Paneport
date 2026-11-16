// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Security;

/// <summary>
/// What the token store keeps of a Steam sign-in: the refresh token, the only secret that
/// Paneport keeps (C-0007 section 3), and the account name, which signing in with the refresh
/// token needs too (<c>avalonia_ui_layer.md</c> section 2.3).
/// </summary>
/// <remarks>
/// The account name is personal data. Neither value appears in <see cref="ToString"/>, so neither
/// reaches a log line by accident (C-0010 section 2).
/// </remarks>
public sealed class SavedSignIn
{
    /// <summary>Creates a saved sign-in.</summary>
    /// <param name="accountName">The Steam account name, never empty.</param>
    /// <param name="refreshToken">The refresh token.</param>
    /// <exception cref="ArgumentException"><paramref name="accountName"/> is empty.</exception>
    public SavedSignIn(string accountName, Secret refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        ArgumentNullException.ThrowIfNull(refreshToken);
        AccountName = accountName;
        RefreshToken = refreshToken;
    }

    /// <summary>Gets the Steam account name. Never log or display it outside the sign-in flow.</summary>
    public string AccountName { get; }

    /// <summary>Gets the refresh token.</summary>
    public Secret RefreshToken { get; }

    /// <summary>Returns a text that names neither the account nor the token.</summary>
    /// <returns>The redacted text.</returns>
    public override string ToString() => $"{nameof(SavedSignIn)} {Secret.Redacted}";
}
