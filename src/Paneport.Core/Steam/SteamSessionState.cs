// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// Where the session with Steam stands.
/// </summary>
public enum SteamSessionState
{
    /// <summary>Not logged on, and nothing is trying to log on.</summary>
    SignedOut,

    /// <summary>Connecting to one of Steam's servers.</summary>
    Connecting,

    /// <summary>A sign-in is running: waiting for an approval, a code or a QR scan.</summary>
    SigningIn,

    /// <summary>Connected, and waiting for the result of the logon.</summary>
    LoggingOn,

    /// <summary>Logged on.</summary>
    LoggedOn,

    /// <summary>The connection dropped while logged on; waiting to connect again.</summary>
    Reconnecting,

    /// <summary>Reconnecting gave up at its limit. The saved sign-in stays, so a resume can try again.</summary>
    Offline,

    /// <summary>Logging off and disconnecting.</summary>
    SigningOut,
}
