// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Paneport.Core.Compat;

// Source-generated serialization of title files, with no reflection (C-0010 section 2). Files are
// written with two-space indents and LF line endings (C-0002 section 7), whatever the platform,
// and leave out what a layer does not set. A field the document does not define fails the read;
// TitleEntryJson finds such fields first, to name them in its own terms.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    IndentSize = 2,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TitleDocument))]
internal sealed partial class CompatJsonContext : JsonSerializerContext;
