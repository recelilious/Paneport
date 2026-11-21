// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;

namespace Paneport.Steam.Ports;

// Answers the questions that Steam asks during a sign-in with a password. The SteamKit2 adapter
// passes them on from its IAuthenticator. SteamKit2 asks AcceptApprovalAsync once, when the
// account can approve sign-ins in the Steam mobile app; true waits for the approval, false asks
// for a code instead.
internal interface ICodeSource
{
    Task<bool> AcceptApprovalAsync();

    // The code that Steam asks for; previousWasWrong says that Steam refused the last one.
    Task<string> GetCodeAsync(SteamCodeKind kind, bool previousWasWrong);
}
