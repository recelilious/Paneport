// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Prefixes;

/// <summary>
/// Makes a Wine prefix from the template that the app bundle holds, and brings it up to date when a
/// newer template ships (docs/design/runtime-ios.md section 7).
/// </summary>
public interface IPrefixInstaller
{
    /// <summary>
    /// Makes sure that a prefix exists and comes from the bundled template. The work runs on a
    /// worker thread; the caller may be the UI thread. Runs are serialized, so a call made while
    /// another runs waits for it and then finds the prefix current.
    /// </summary>
    /// <param name="prefixName">The prefix's folder name below the prefixes folder, such as <see cref="PrefixNames.Shared"/>.</param>
    /// <param name="cancellationToken">
    /// Stops the run between two files. The prefix is then whole or in a state that the next run repairs.
    /// </param>
    /// <returns>What the run did. A failure is a result, not an exception.</returns>
    Task<PrefixInstallResult> InstallAsync(string prefixName, CancellationToken cancellationToken);

    /// <summary>Gets the full path of a prefix's folder, which may not exist yet.</summary>
    /// <param name="prefixName">The prefix's folder name.</param>
    /// <returns>The folder's full path, for the session's start.</returns>
    string PathOf(string prefixName);
}

/// <summary>The names of the prefixes that Paneport makes.</summary>
public static class PrefixNames
{
    /// <summary>
    /// The prefix that every title shares by default (C-0004 section 4). No code fixed a name before
    /// the installer, so this is the name that it gives.
    /// </summary>
    public const string Shared = "shared";
}
