// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Paneport.Core.Hosting;

/// <summary>
/// The version of the native host interface. <c>pp_abi_version</c> returns it packed into a
/// 32-bit value, with the major version in the high 16 bits and the minor version in the low
/// 16 bits (C-0005 section 1).
/// </summary>
public readonly record struct HostAbiVersion
{
    /// <summary>Creates a version.</summary>
    /// <param name="major">The major version, which changes when anything is changed or removed.</param>
    /// <param name="minor">The minor version, which changes when something is added.</param>
    public HostAbiVersion(ushort major, ushort minor)
    {
        Major = major;
        Minor = minor;
    }

    /// <summary>Gets the major version.</summary>
    public ushort Major { get; }

    /// <summary>Gets the minor version.</summary>
    public ushort Minor { get; }

    /// <summary>Gets the version packed the way <c>pp_abi_version</c> returns it.</summary>
    public uint Packed => ((uint)Major << 16) | Minor;

    /// <summary>Unpacks the value that <c>pp_abi_version</c> returns.</summary>
    /// <param name="packed">The major version in the high 16 bits and the minor version in the low 16 bits.</param>
    /// <returns>The version.</returns>
    public static HostAbiVersion FromPacked(uint packed) => new((ushort)(packed >> 16), (ushort)(packed & 0xFFFF));

    /// <summary>
    /// Checks whether code built against one version can work with another. Only the major
    /// version counts: the .NET side refuses to start when it differs (C-0005 section 1).
    /// </summary>
    /// <param name="other">The other version.</param>
    /// <returns>True when both have the same major version.</returns>
    public bool IsCompatibleWith(HostAbiVersion other) => Major == other.Major;

    /// <summary>Gets the version as <c>major.minor</c>.</summary>
    /// <returns>The version text, for example <c>1.0</c>.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");
}
