// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// How important a log line of the host is: the <c>PP_LOG_</c> values of
/// <c>paneport/host.h</c>, in the order of Paneport.Core's log levels.
/// </summary>
public enum HostLogLevel
{
    /// <summary>Detail that helps only while debugging (<c>PP_LOG_DEBUG</c>).</summary>
    Debug = 0,

    /// <summary>A normal event (<c>PP_LOG_INFO</c>).</summary>
    Information = 1,

    /// <summary>Something unexpected that the host recovered from (<c>PP_LOG_WARNING</c>).</summary>
    Warning = 2,

    /// <summary>A failure (<c>PP_LOG_ERROR</c>).</summary>
    Error = 3,
}
