// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// Where a log line of the host comes from: the <c>PP_LOG_SOURCE_</c> values of
/// <c>paneport/host.h</c>, which version 1.4 of the interface added.
/// </summary>
public enum HostLogSource
{
    /// <summary>The native host's own line (<c>PP_LOG_SOURCE_HOST</c>).</summary>
    Host = 0,

    /// <summary>A line of the runtime's diagnostics: Wine, its wineserver and the translator (<c>PP_LOG_SOURCE_RUNTIME</c>).</summary>
    Runtime = 1,

    /// <summary>A line that a Windows program wrote to its standard output (<c>PP_LOG_SOURCE_GUEST_OUTPUT</c>).</summary>
    GuestOutput = 2,

    /// <summary>A line that a Windows program wrote to its standard error (<c>PP_LOG_SOURCE_GUEST_ERRORS</c>).</summary>
    GuestErrors = 3,
}
