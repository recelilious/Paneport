// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class SignedOutSteamServiceTests
{
    [Fact]
    public void IsAlwaysSignedOut()
    {
        Assert.Equal(SteamSignInState.SignedOut, new SignedOutSteamService().SignInState);
    }
}
