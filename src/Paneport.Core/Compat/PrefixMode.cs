// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// Which Wine prefix a title runs in, the <c>prefix</c> field (C-0008 section 2,
/// C-0004 section 4).
/// </summary>
/// <remarks>The default value, <see cref="Shared"/>, is also the schema default.</remarks>
public enum PrefixMode
{
    /// <summary>The prefix that every title shares, written <c>shared</c>.</summary>
    Shared,

    /// <summary>A prefix of the title's own, written <c>isolated</c>.</summary>
    Isolated,
}
