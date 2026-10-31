// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The program that the Direct path starts, the <c>executable</c> object (C-0008 section 2).
/// </summary>
/// <remarks>
/// Paths are relative to the install folder and use forward slashes. A layer replaces the whole
/// object, never single members, because the working directory and the arguments belong to the
/// program they were set for.
/// </remarks>
public sealed record ExecutableSpec
{
    /// <summary>Gets the program's path.</summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the working directory: <c>.</c> for the install folder itself, a relative path, or
    /// null when the entry sets none.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets the arguments, or null when the entry sets none.</summary>
    public IReadOnlyList<string>? Arguments { get; init; }
}
