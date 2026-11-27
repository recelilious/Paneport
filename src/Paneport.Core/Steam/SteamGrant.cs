// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// Whether Steam granted a depot key or a manifest request code. The value itself is never shown
/// (L2 step 24).
/// </summary>
public enum SteamGrant
{
    /// <summary>Steam granted it, and the steps keep it.</summary>
    Obtained,

    /// <summary>Steam did not grant it.</summary>
    NotGranted,
}
