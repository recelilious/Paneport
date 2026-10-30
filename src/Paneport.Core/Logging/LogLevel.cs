// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Logging;

/// <summary>
/// How important a log line is.
/// </summary>
public enum LogLevel
{
    /// <summary>Detail that helps only while debugging.</summary>
    Debug,

    /// <summary>A normal event, such as opening a page.</summary>
    Information,

    /// <summary>Something unexpected that the app recovered from.</summary>
    Warning,

    /// <summary>A failure, such as a page that could not load its data.</summary>
    Error,
}
