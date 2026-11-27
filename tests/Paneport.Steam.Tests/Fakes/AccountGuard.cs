// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Tests.Fakes;

// How a fake account confirms a sign-in.
public enum AccountGuard
{
    // An approval in the Steam mobile app, with an authenticator code as the fallback.
    Approval,

    // A code from the authenticator only.
    AuthenticatorCode,

    // A code by email.
    Email,

    // Nothing: the password is enough.
    None,
}
