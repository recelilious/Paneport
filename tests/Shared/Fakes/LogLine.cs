// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Tests.Fakes;

// One line that a RecordingLog recorded.
internal sealed record LogLine(LogLevel Level, string Source, string Message);
