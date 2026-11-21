// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// How the connection to Steam's connection manager servers runs. The SteamKit2 gate reports it
/// (L2 step 27), because the WebSocket path may fail on iOS where TCP works
/// (<c>docs/design/steam-service.md</c>).
/// </summary>
public enum SteamTransport
{
    /// <summary>No connection has been made yet.</summary>
    None,

    /// <summary>A WebSocket connection.</summary>
    WebSocket,

    /// <summary>A plain TCP connection.</summary>
    Tcp,
}
