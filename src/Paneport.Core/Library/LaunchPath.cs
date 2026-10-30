// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// The way a title starts, which its classification decides (C-0007 section 5, C-0008 section 2).
/// </summary>
public enum LaunchPath
{
    /// <summary>The title has not been classified yet. It is classified before its first launch.</summary>
    Unclassified,

    /// <summary>The title's program starts directly in the Wine session.</summary>
    Direct,

    /// <summary>The Dock host starts first and launches the title through the Steam client.</summary>
    Dock,
}
