// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Apps;

namespace Paneport.Steam.Ports;

// App information and depot keys, which the SteamKit2 adapter implements over SteamApps. Each
// call needs a logged-on session.
internal interface ISteamApps
{
    // The access token that PICS asks for an app's information, or 0 when the account gets none.
    // It is never logged.
    Task<ulong> GetAppAccessTokenAsync(uint appId, CancellationToken cancellationToken);

    // The app's information as a key-value tree, or null when Steam returns none for the app.
    Task<KeyValueNode?> GetAppInfoAsync(uint appId, ulong accessToken, CancellationToken cancellationToken);

    Task<DepotKeyResult> GetDepotKeyAsync(uint depotId, uint appId, CancellationToken cancellationToken);
}
