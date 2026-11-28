// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Prefixes;

/// <summary>How a run of the prefix installer ended.</summary>
public enum PrefixInstallOutcome
{
    /// <summary>The bundle holds no template, as in today's builds. Nothing was done.</summary>
    NoTemplate,

    /// <summary>The prefix did not exist and was made from the template.</summary>
    Installed,

    /// <summary>The prefix came from the same template version. Nothing was copied.</summary>
    AlreadyCurrent,

    /// <summary>A newer template replaced the folders that its marker names.</summary>
    Updated,

    /// <summary>The bundled template is older than the installed one. The prefix was left alone.</summary>
    TemplateOlder,

    /// <summary>The prefix has no valid marker, so the installer did not make it. It was left alone.</summary>
    Unmanaged,

    /// <summary>The run failed. The next launch repeats it.</summary>
    Failed,
}

/// <summary>What a run of the prefix installer did.</summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="PrefixFolder">The prefix's folder relative to the app container, with forward slashes; never an absolute path.</param>
/// <param name="TemplateVersion">The bundled template's version, when the bundle has a valid marker.</param>
/// <param name="InstalledVersion">The version in the prefix's marker after the run, when it has one.</param>
/// <param name="FailureReason">For <see cref="PrefixInstallOutcome.Failed"/>, a short reason with no path in it.</param>
public sealed record PrefixInstallResult(
    PrefixInstallOutcome Outcome,
    string PrefixFolder,
    int? TemplateVersion,
    int? InstalledVersion,
    string? FailureReason = null)
{
    /// <summary>
    /// Gets a value indicating whether the run left a prefix that a session may use: made, current,
    /// updated, newer than the template, or not the installer's own and left as it was.
    /// </summary>
    public bool PrefixExists => Outcome is not (PrefixInstallOutcome.NoTemplate or PrefixInstallOutcome.Failed);
}
