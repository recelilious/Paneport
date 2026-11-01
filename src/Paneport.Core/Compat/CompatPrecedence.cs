// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The precedence rules of C-0008 section 1: the effective settings are the schema defaults, then
/// the bundled entry, then the device layer, with two exceptions. A <c>drm</c> value set in the
/// bundled entry wins over a computed one while the device runs the reviewed program, and the device
/// layer can never turn the launch path into Direct for a title whose classification requires the
/// Dock path.
/// </summary>
/// <remarks>
/// <para>
/// A value the higher layer sets replaces the lower layer's. Lists, and <c>executable</c> as a
/// whole, are replaced; the members of <c>graphics</c> and <c>helperPolicy</c> are replaced one by
/// one; the keys of <c>translator</c> and <c>environment</c> are replaced key by key. Their
/// allow-lists are enforced where an entry is read or written (<see cref="TitleEntryJson"/>), not in
/// the merge, which takes checked entries as <see cref="CompatDatabase"/> passes them.
/// </para>
/// <para>
/// The device layer never decides the launch path: <c>launch</c> follows from the merged
/// <c>drm</c> (<see cref="LaunchRule"/>), the bundled entry's members win, and the merge takes
/// only <c>unknown</c> from the device layer's <c>ceg</c>, whatever an unchecked entry holds. A
/// title can therefore take the Direct path only when a reviewed entry sets <c>ceg</c> to
/// <c>no</c>.
/// </para>
/// <para>
/// The bundled entry's <c>drm</c> describes the program that was reviewed, so it counts only while
/// the device runs that program: the device layer names no other <c>executable.path</c>, and for a
/// local title the device layer has an entry whose <c>exeSha256</c> values are all among the
/// bundled entry's. Otherwise only the computed classification counts, which gives the Dock path or
/// a refusal.
/// </para>
/// </remarks>
public static class CompatPrecedence
{
    private static readonly IReadOnlyDictionary<string, string> s_noKeys = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>Merges the layers of a title.</summary>
    /// <param name="id">The title's identity.</param>
    /// <param name="bundled">
    /// The bundled entry, checked against the bundled layer's rules; null when there is none.
    /// </param>
    /// <param name="device">
    /// The device entry, checked against the device layer's rules; null when there is none.
    /// </param>
    /// <returns>The settings the title runs with. Without entries, they are the schema defaults.</returns>
    /// <exception cref="ArgumentException">An entry belongs to another title.</exception>
    public static EffectiveTitleSettings Merge(TitleId id, TitleEntry? bundled, TitleEntry? device)
    {
        ArgumentNullException.ThrowIfNull(id);
        CheckBelongs(id, bundled, nameof(bundled));
        CheckBelongs(id, device, nameof(device));

        var reviewedDrm = RunsReviewedProgram(id, bundled, device) ? bundled?.Drm : null;
        var drm = MergeDrm(reviewedDrm, device?.Drm);
        var executable = device?.Executable ?? bundled?.Executable;
        return new EffectiveTitleSettings
        {
            Id = id,
            Name = device?.Name ?? bundled?.Name,
            Executable = executable is null ? null : executable with { Arguments = executable.Arguments ?? [] },
            Architecture = device?.Architecture ?? bundled?.Architecture,
            Drm = drm,
            Launch = LaunchRule.Decide(id.Source, drm),
            GraphicsApi = device?.GraphicsApi ?? bundled?.GraphicsApi ?? [],
            Runtimes = device?.Runtimes ?? bundled?.Runtimes ?? [],
            MemoryEstimateMiB = device?.MemoryEstimateMiB ?? bundled?.MemoryEstimateMiB,
            JitPoolMiB = device?.JitPoolMiB ?? bundled?.JitPoolMiB,
            Translator = MergeKeys(bundled?.Translator, device?.Translator),
            RenderScale = device?.Graphics?.RenderScale ?? bundled?.Graphics?.RenderScale ?? 1.0,
            MetalFx = device?.Graphics?.MetalFx ?? bundled?.Graphics?.MetalFx ?? MetalFxMode.Off,
            FrameRateCap = device?.Graphics?.FrameRateCap ?? bundled?.Graphics?.FrameRateCap,
            Environment = MergeKeys(bundled?.Environment, device?.Environment),
            BlockedExecutables =
                device?.HelperPolicy?.BlockedExecutables ?? bundled?.HelperPolicy?.BlockedExecutables ?? [],
            Prefix = device?.Prefix ?? bundled?.Prefix ?? PrefixMode.Shared,

            // Only reviewed entries hold ratings and reports, so the device layer adds none.
            Rating = bundled?.Rating ?? CompatRating.Untested,
            Reports = bundled?.Reports ?? [],
        };
    }

    // Whether the device runs the program that the bundled entry was reviewed for. A device executable
    // with another path, or with none, is another program, and a local title's builds come only from
    // its device entry.
    private static bool RunsReviewedProgram(TitleId id, TitleEntry? bundled, TitleEntry? device)
    {
        if (bundled is null)
        {
            return false;
        }

        if (device?.Executable is { } executable
            && (executable.Path is null || executable.Path != bundled.Executable?.Path))
        {
            return false;
        }

        return id.Source == TitleSource.Steam
            || (device?.Id.ExeSha256 is { Count: > 0 } builds
                && bundled.Id.ExeSha256 is { } reviewed
                && builds.All(reviewed.Contains));
    }

    private static DrmClassification? MergeDrm(DrmClassification? bundled, DrmClassification? device)
    {
        var merged = new DrmClassification
        {
            SteamApi = bundled?.SteamApi ?? device?.SteamApi,
            SteamStub = bundled?.SteamStub ?? device?.SteamStub,
            SteamAppIdFile = bundled?.SteamAppIdFile ?? device?.SteamAppIdFile,
            Ceg = bundled?.Ceg ?? (device?.Ceg == CegStatus.Unknown ? CegStatus.Unknown : null),
        };

        return merged is { SteamApi: null, SteamStub: null, SteamAppIdFile: null, Ceg: null } ? null : merged;
    }

    private static IReadOnlyDictionary<string, string> MergeKeys(
        IReadOnlyDictionary<string, string>? lower, IReadOnlyDictionary<string, string>? upper)
    {
        if (upper is null || lower is null)
        {
            return upper ?? lower ?? s_noKeys;
        }

        var merged = new Dictionary<string, string>(lower, StringComparer.Ordinal);
        foreach (var (key, value) in upper)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static void CheckBelongs(TitleId id, TitleEntry? entry, string parameterName)
    {
        if (entry is not null && entry.Id.ToTitleId() != id)
        {
            throw new ArgumentException("The entry belongs to another title.", parameterName);
        }
    }
}
