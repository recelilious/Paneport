// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The launch rule of C-0008 section 5: the Direct path only when <c>steamApi</c> and
/// <c>steamStub</c> are false and <c>ceg</c> is <c>no</c>; otherwise the Dock path for a Steam
/// title, and a refusal for a local title.
/// </summary>
public static class LaunchRule
{
    /// <summary>Derives what a classification allows.</summary>
    /// <param name="source">Where the title comes from.</param>
    /// <param name="drm">The title's effective classification; null when it has none yet.</param>
    /// <returns>
    /// <see cref="LaunchDecision.Unclassified"/> while <c>steamApi</c> or <c>steamStub</c> is
    /// missing and nothing that blocks the Direct path shows yet; otherwise the path the rule allows.
    /// A missing <c>ceg</c> counts as unknown, because the device's classification can never set it.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is not defined.</exception>
    public static LaunchDecision Decide(TitleSource source, DrmClassification? drm)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source), source, "The title source is not defined.");
        }

        if (BlocksDirect(drm))
        {
            return Closed(source);
        }

        if (drm?.SteamApi is null || drm.SteamStub is null)
        {
            return LaunchDecision.Unclassified;
        }

        return drm.Ceg == CegStatus.No ? LaunchDecision.Direct : Closed(source);
    }

    // Whether the classification allows the Direct path on its own: every member the rule names is
    // set, and to the value that allows it.
    internal static bool AllowsDirect(DrmClassification? drm) =>
        drm is { SteamApi: false, SteamStub: false, Ceg: CegStatus.No };

    // Whether a member that is set blocks the Direct path, whatever the missing members turn out to be.
    internal static bool BlocksDirect(DrmClassification? drm) =>
        drm is not null
        && (drm.SteamApi == true || drm.SteamStub == true || drm.Ceg is CegStatus.Yes or CegStatus.Unknown);

    private static LaunchDecision Closed(TitleSource source) =>
        source == TitleSource.Steam ? LaunchDecision.Dock : LaunchDecision.Refused;
}
