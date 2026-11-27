// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// How a sign-in or a resume ended.
/// </summary>
public enum SignInOutcome
{
    /// <summary>The session is logged on.</summary>
    LoggedOn,

    /// <summary>A resume found no saved sign-in.</summary>
    NothingSaved,

    /// <summary>The user cancelled.</summary>
    Cancelled,

    /// <summary>The sign-in failed; the result says why.</summary>
    Failed,
}
