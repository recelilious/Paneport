// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// Calls a host function that a minor version of the interface added, for an
/// <see cref="INativeHost"/> implementation.
/// </summary>
/// <remarks>
/// C-0005 section 1 counts a host of the same major version as compatible, whatever its minor
/// version, but a host of an older minor version lacks the functions that a later one added, and
/// the runtime throws <see cref="EntryPointNotFoundException"/> when such a function is called
/// (docs/design/interop.md section 3). A call made through <see cref="Call{T}"/> throws the
/// <see cref="NativeHostException"/> of every failed call instead, with the status
/// <see cref="UnsupportedStatus"/>, so that the caller's handling of a failed call applies.
/// </remarks>
public static class HostExports
{
    /// <summary>The status of a function that the host lacks, as Paneport.Interop names <c>PP_E_UNSUPPORTED</c>.</summary>
    public const string UnsupportedStatus = "Unsupported";

    /// <summary>Calls a host function that the given version of the interface added.</summary>
    /// <typeparam name="T">The type of the function's result.</typeparam>
    /// <param name="addedIn">The version of the interface that added the function.</param>
    /// <param name="call">The call through the bindings.</param>
    /// <returns>The function's result.</returns>
    /// <exception cref="NativeHostException">The host predates the function, or the call failed.</exception>
    public static T Call<T>(HostAbiVersion addedIn, Func<T> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            return call();
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new NativeHostException(UnsupportedStatus, $"The host predates interface {addedIn}, which added this function.", exception);
        }
    }
}
