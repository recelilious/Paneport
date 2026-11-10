// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The native host implements another major version of the interface than these bindings,
/// so the .NET side refuses to start it (C-0005 section 1).
/// </summary>
public sealed class HostAbiMismatchException : Exception
{
    /// <summary>Creates an exception without a version.</summary>
    public HostAbiMismatchException()
    {
    }

    /// <summary>Creates an exception with a message and no version.</summary>
    /// <param name="message">What went wrong.</param>
    public HostAbiMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message, the exception that caused it, and no version.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public HostAbiMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for the version that the host reported.</summary>
    /// <param name="hostVersion">The value that <c>pp_abi_version</c> returned.</param>
    public HostAbiMismatchException(uint hostVersion)
        : base($"The native host implements interface version {HostAbi.Format(hostVersion)}, but Paneport.Interop "
            + $"needs major version {HostAbi.Major}.")
    {
        HostVersion = hostVersion;
    }

    /// <summary>Gets the version that the host reported, packed as <c>pp_abi_version</c> returns it.</summary>
    public uint HostVersion { get; }
}
