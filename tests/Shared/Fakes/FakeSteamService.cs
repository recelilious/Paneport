// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;

namespace Paneport.Tests.Fakes;

// A Steam service whose sign-in state a test sets. It starts signed out, like the desktop head's.
internal sealed class FakeSteamService : ISteamService
{
    public SteamSignInState SignInState { get; set; } = SteamSignInState.SignedOut;
}
