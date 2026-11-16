// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// The native runtime host as the app sees it. View models reach the host only through this
/// interface (C-0004 section 1).
/// </summary>
/// <remarks>
/// For now it covers what the Diagnostics page shows. On the device <see cref="NativeRuntimeHost"/>
/// implements it over Paneport.Interop; the desktop head implements it with a fake.
/// </remarks>
public interface IRuntimeHost
{
    /// <summary>
    /// Raised on the UI thread for each event of the host, in the order the host sent them
    /// (C-0005 section 2).
    /// </summary>
    event EventHandler<HostEventInfo>? EventReceived;

    /// <summary>
    /// Gets the name of the implementation, such as a fake or the native host, so that a fake
    /// value is never mistaken for a device reading.
    /// </summary>
    string ImplementationName { get; }

    /// <summary>Gets the version of the host interface that the host implements (<c>pp_abi_version</c>).</summary>
    HostAbiVersion AbiVersion { get; }

    /// <summary>
    /// Gets the version that the app's bindings were written against. The host is compatible
    /// when the major versions match (C-0005 section 1).
    /// </summary>
    HostAbiVersion BindingsVersion { get; }

    /// <summary>Gets how the app imports the host, such as the framework or <c>__Internal</c> (C-0009 section 4).</summary>
    string ImportName { get; }

    /// <summary>Gets how many events the host sent since the app started. UI thread.</summary>
    int EventCount { get; }

    /// <summary>Gets the last event that the host sent, or null when it sent none. UI thread.</summary>
    HostEventInfo? LastEvent { get; }

    /// <summary>Reads the current memory budget (<c>pp_memory_get_budget</c>). Any thread may call it.</summary>
    /// <returns>The memory figures at the time of the call.</returns>
    MemoryBudget GetMemoryBudget();
}
