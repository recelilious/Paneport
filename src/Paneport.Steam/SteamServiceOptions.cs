// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Security;

namespace Paneport.Steam;

/// <summary>
/// What an app head gives the Steam service (<c>docs/design/steam-service.md</c>).
/// </summary>
public sealed class SteamServiceOptions
{
    /// <summary>Gets the app's log. Every line the service writes passes the service's secret filter first.</summary>
    public required ILog Log { get; init; }

    /// <summary>Gets the token store: the keychain on the device, memory on the desktop (C-0007 section 3).</summary>
    public required ITokenStore Tokens { get; init; }

    /// <summary>
    /// Gets the name under which Steam lists the sign-in, such as "Paneport (iPhone)". It is fixed
    /// text, never the device's own name.
    /// </summary>
    public required string DeviceName { get; init; }

    /// <summary>Gets the downloads folder of C-0004 section 4, below which the gate's test folder goes.</summary>
    public required string DownloadsFolder { get; init; }

    /// <summary>
    /// Gets the installation's random identity, from which the machine identity that Steam sees is
    /// derived (<see cref="MachineIdentity"/>); null leaves the machine identity to SteamKit2, as
    /// the desktop head does.
    /// </summary>
    public byte[]? MachineIdentity { get; init; }

    /// <summary>
    /// Gets a value indicating whether the connection uses TCP only. The default also allows
    /// WebSocket, SteamKit2's first choice.
    /// </summary>
    public bool TcpOnly { get; init; }

    /// <summary>
    /// Gets a value indicating whether SteamKit2's own debug output goes to the log, through the same
    /// secret filter. It is off unless a developer turns it on.
    /// </summary>
    public bool SteamKitDebugOutput { get; init; }

    /// <summary>Gets the clock, the system's unless a test gives another.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;
}
