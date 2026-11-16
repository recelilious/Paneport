// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Paneport.Core.Settings;

// Source-generated serialization of the settings file, with no reflection (C-0010 section 2),
// written with two-space indents and LF line endings (C-0002 section 7).
[JsonSourceGenerationOptions(WriteIndented = true, IndentSize = 2, NewLine = "\n")]
[JsonSerializable(typeof(SettingsDocument))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
