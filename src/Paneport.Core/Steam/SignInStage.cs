// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// Where a sign-in attempt stands.
/// </summary>
public enum SignInStage
{
    /// <summary>Connecting to Steam.</summary>
    Connecting,

    /// <summary>Asking Steam to start the sign-in.</summary>
    Starting,

    /// <summary>Waiting for the user to approve the sign-in in the Steam mobile app.</summary>
    WaitingForApproval,

    /// <summary>Waiting for the user to enter a Steam Guard code.</summary>
    WaitingForCode,

    /// <summary>Showing a QR code for the Steam mobile app on another device to scan.</summary>
    ShowingQrCode,

    /// <summary>The sign-in was confirmed, and the session is logging on.</summary>
    LoggingOn,

    /// <summary>Logged on. The attempt has ended.</summary>
    LoggedOn,

    /// <summary>The sign-in failed. The attempt has ended.</summary>
    Failed,

    /// <summary>The user cancelled the sign-in. The attempt has ended.</summary>
    Cancelled,
}
