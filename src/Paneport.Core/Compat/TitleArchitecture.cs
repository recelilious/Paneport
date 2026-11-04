// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The architecture of a title's program, the <c>architecture</c> field (C-0008 section 2).
/// </summary>
public enum TitleArchitecture
{
    /// <summary>A 64-bit program, written <c>x86_64</c>.</summary>
    X64,

    /// <summary>A 32-bit program, written <c>x86</c>.</summary>
    X86,
}
