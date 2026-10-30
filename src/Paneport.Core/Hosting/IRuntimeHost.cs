// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// The native runtime host as the app sees it. View models reach the host only through this
/// interface (C-0004 section 1).
/// </summary>
/// <remarks>
/// For now it covers only what the Diagnostics page shows. M1.3 implements it on the device over
/// Paneport.Interop; the desktop head implements it with a fake.
/// </remarks>
public interface IRuntimeHost
{
    /// <summary>
    /// Gets the name of the implementation, such as a fake or the native host, so that a fake
    /// value is never mistaken for a device reading.
    /// </summary>
    string ImplementationName { get; }

    /// <summary>Gets the version of the host interface (<c>pp_abi_version</c>).</summary>
    HostAbiVersion AbiVersion { get; }

    /// <summary>Reads the current memory budget (<c>pp_memory_get_budget</c>). Any thread may call it.</summary>
    /// <returns>The memory figures at the time of the call.</returns>
    MemoryBudget GetMemoryBudget();
}
