// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The kind of rule that a title file or entry breaks. <c>docs/design/compat-db.md</c> lists the
/// checks behind each kind.
/// </summary>
public enum CompatRule
{
    /// <summary>The file is not UTF-8 JSON holding one object, or it is larger than 1 MiB.</summary>
    Json,

    /// <summary>A value has the wrong JSON type. No field takes null.</summary>
    Type,

    /// <summary>A field that the title schema does not define.</summary>
    UnknownField,

    /// <summary>A field that appears twice in one object.</summary>
    DuplicateField,

    /// <summary>A required field is missing.</summary>
    MissingField,

    /// <summary>
    /// The file follows a newer schema version than this build reads, so nothing else in it is
    /// checked.
    /// </summary>
    NewerSchemaVersion,

    /// <summary>A value is outside its range or its list of values, or does not have its form.</summary>
    InvalidValue,

    /// <summary>A translator option or environment variable that is not on its allow-list.</summary>
    NotAllowed,

    /// <summary>The launch rule of C-0008 section 5.</summary>
    Launch,

    /// <summary>
    /// The identity: a Steam app ID or local builds, never both, and a file name that matches it
    /// (C-0008 section 1).
    /// </summary>
    Identity,

    /// <summary>A value that the device layer never holds.</summary>
    DeviceLayer,
}
