// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// The results of the connection's requests and what Steam sends on its own, as the adapter
// reports them: from SteamKit2's connected, disconnected, logged-on, logged-off and license-list
// callbacks.
internal interface IConnectionListener
{
    void OnConnected();

    // A connection ended, or a connection attempt failed. userInitiated is true when the client
    // asked for it.
    void OnDisconnected(bool userInitiated);

    void OnLoggedOn(SteamResult result);

    void OnLoggedOff(SteamResult result);

    // The licenses of the logged-on account, by package ID, which Steam sends after the logon.
    void OnLicenseList(SteamResult result, IReadOnlyList<uint> packageIds);
}
