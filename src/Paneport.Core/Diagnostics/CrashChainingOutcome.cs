// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>What became of the call that turns off Mono's crash chaining at startup (C-0005 section 4).</summary>
public enum CrashChainingOutcome
{
    /// <summary>The head made no such call, because it does not run on Mono, as the desktop head.</summary>
    NotCalled,

    /// <summary>The call resolved and returned: Mono's crash chaining is off.</summary>
    TurnedOff,

    /// <summary>The runtime has no such function, or the library that the import names did not open.</summary>
    NotResolved,

    /// <summary>The call failed in another way.</summary>
    Failed,
}
