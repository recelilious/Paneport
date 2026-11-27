// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Prefixes;

/// <summary>What a tree entry of a prefix is.</summary>
public enum PrefixEntryKind
{
    /// <summary>A folder.</summary>
    Directory,

    /// <summary>A regular file.</summary>
    File,

    /// <summary>A symbolic link, which the installer copies as a link and never follows.</summary>
    Symlink,
}

/// <summary>One entry of a prefix or template tree.</summary>
/// <param name="Path">The path relative to the tree's root, with forward slashes.</param>
/// <param name="Kind">What the entry is.</param>
/// <param name="LinkTarget">For a symbolic link, where it points; otherwise null.</param>
public sealed record PrefixEntry(string Path, PrefixEntryKind Kind, string? LinkTarget = null);
