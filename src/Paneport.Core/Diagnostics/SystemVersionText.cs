// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Reads the build out of the system's version text, such as <c>Version 26.1 (Build 23B85)</c>,
/// which <c>NSProcessInfo.operatingSystemVersionString</c> gives on iOS.
/// </summary>
public static class SystemVersionText
{
    private const string BuildMarker = "(Build ";

    /// <summary>The text for a build that the version text does not name.</summary>
    public const string Unknown = "unknown";

    /// <summary>Finds the build in a version text.</summary>
    /// <param name="versionText">The system's version text.</param>
    /// <returns>The build, such as <c>23B85</c>, or <see cref="Unknown"/>.</returns>
    public static string BuildOf(string? versionText)
    {
        if (string.IsNullOrEmpty(versionText))
        {
            return Unknown;
        }

        var start = versionText.IndexOf(BuildMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return Unknown;
        }

        start += BuildMarker.Length;
        var end = versionText.IndexOf(')', start);
        if (end <= start)
        {
            return Unknown;
        }

        var build = versionText[start..end];
        return build.All(char.IsAsciiLetterOrDigit) ? build : Unknown;
    }
}
