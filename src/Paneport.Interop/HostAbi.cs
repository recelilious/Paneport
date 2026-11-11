// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Paneport.Interop;

/// <summary>
/// The version of the host interface that these bindings were written against, from
/// <c>PP_ABI_VERSION_MAJOR</c> and <c>PP_ABI_VERSION_MINOR</c> (C-0005 section 1).
/// </summary>
public static class HostAbi
{
    /// <summary>The major version. A host with another one is refused.</summary>
    public const ushort Major = 1;

    /// <summary>The minor version, which grows when the interface gains a function or a struct member.</summary>
    public const ushort Minor = 0;

    /// <summary>Gets the version packed as <c>pp_abi_version</c> returns it.</summary>
    public static uint Packed => ((uint)Major << 16) | Minor;

    /// <summary>Gets the major version of a packed version.</summary>
    /// <param name="packed">The major version in the high 16 bits and the minor version in the low 16 bits.</param>
    /// <returns>The major version.</returns>
    public static ushort MajorOf(uint packed) => (ushort)(packed >> 16);

    /// <summary>Gets the minor version of a packed version.</summary>
    /// <param name="packed">The major version in the high 16 bits and the minor version in the low 16 bits.</param>
    /// <returns>The minor version.</returns>
    public static ushort MinorOf(uint packed) => (ushort)(packed & 0xFFFF);

    /// <summary>
    /// Checks whether these bindings can work with a host. Only the major version counts
    /// (C-0005 section 1).
    /// </summary>
    /// <param name="packed">The value that <c>pp_abi_version</c> returned.</param>
    /// <returns>True when the major versions are equal.</returns>
    public static bool IsCompatible(uint packed) => MajorOf(packed) == Major;

    /// <summary>Gets a packed version as <c>major.minor</c>.</summary>
    /// <param name="packed">The major version in the high 16 bits and the minor version in the low 16 bits.</param>
    /// <returns>The version text, for example <c>1.0</c>.</returns>
    public static string Format(uint packed) =>
        string.Create(CultureInfo.InvariantCulture, $"{MajorOf(packed)}.{MinorOf(packed)}");
}
