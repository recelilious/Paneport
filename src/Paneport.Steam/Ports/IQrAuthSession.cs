// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// A sign-in with a QR code. Steam replaces the challenge from time to time, and the page draws
// the new one.
internal interface IQrAuthSession : IAuthSession
{
    // The text that the QR code encodes. It is never logged.
    string Challenge { get; }

    // Raised after Challenge changed, on the adapter's thread.
    event EventHandler? ChallengeChanged;
}
