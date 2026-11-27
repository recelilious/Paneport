// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// Steam's authentication service, which the SteamKit2 adapter implements over
// SteamClient.Authentication. Both calls need a connected client. A failure that Steam reports
// arrives as SteamAuthException.
internal interface ISteamAuthenticator
{
    // Starts a sign-in with an account name and a password. The code source answers Steam's
    // questions: whether to wait for an approval in the Steam mobile app, and which code to send.
    Task<IAuthSession> BeginWithCredentialsAsync(
        string accountName,
        string password,
        ICodeSource codes,
        CancellationToken cancellationToken);

    // Starts a sign-in with a QR code, which the Steam mobile app on another device scans.
    Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken);
}
