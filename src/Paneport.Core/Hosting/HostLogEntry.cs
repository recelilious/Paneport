// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.Hosting;

/// <summary>A log line of the native host (<c>pp_log_line_t</c>, C-0005 section 2).</summary>
/// <param name="Level">How important the line is.</param>
/// <param name="ThreadId">The system's identifier of the thread that logged it.</param>
/// <param name="Text">The text, which never holds a password, token or other secret (C-0010 section 2).</param>
public readonly record struct HostLogEntry(LogLevel Level, ulong ThreadId, string Text);
