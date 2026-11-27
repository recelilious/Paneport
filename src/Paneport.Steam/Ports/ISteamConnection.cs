// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
using Paneport.Core.Steam;

namespace Paneport.Steam.Ports;

// The connection to Steam's connection manager servers and the logon on it. The SteamKit2
// adapter implements it over a SteamClient and its callback loop; the tests use a fake
// (steam-service.md section 3). Every request returns at once. Its result arrives later through
// the attached listener, on the adapter's callback thread.
internal interface ISteamConnection
{
    // How the current or the last connection runs.
    SteamTransport Transport { get; }

    // Whether the client is connected now. SteamKit2 sets it false as soon as the connection ends,
    // before the listener hears of the drop, so the session asks it before it trusts a connection.
    bool IsConnected { get; }

    // Gives the connection the listener of its results. Called once, before any request.
    void Attach(IConnectionListener listener);

    void Connect();

    void Disconnect();

    // Asks the server list not to pick the current server for the next connection, after Steam
    // asked the client to try another one.
    void AvoidCurrentServer();

    void LogOn(string accountName, Secret refreshToken);

    void LogOff();
}
