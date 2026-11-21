// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// A sign-in that Steam has started. Waiting runs the confirmation that Steam chose, asking the
// code source where it needs to, and ends with the tokens.
internal interface IAuthSession
{
    Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken);
}
