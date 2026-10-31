// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The settings a title runs with: the schema defaults, then the bundled entry, then the device
/// layer, with the exceptions of C-0008 section 1. <see cref="CompatPrecedence.Merge"/> makes them.
/// </summary>
/// <remarks>
/// Every field with a schema default holds a value. The others are null when no layer sets them,
/// and the code that uses them decides what that means, such as the JIT flow, which takes its
/// default pool size when <see cref="JitPoolMiB"/> is null (C-0006 section 4).
/// </remarks>
public sealed record EffectiveTitleSettings
{
    /// <summary>Gets the title's identity.</summary>
    public required TitleId Id { get; init; }

    /// <summary>Gets the display name, if a layer sets one.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets the program that the Direct path starts, if a layer names one. Its arguments are never
    /// null here.
    /// </summary>
    public ExecutableSpec? Executable { get; init; }

    /// <summary>Gets the architecture of that program, if a layer sets it.</summary>
    public TitleArchitecture? Architecture { get; init; }

    /// <summary>
    /// Gets the classification, member by member from the bundled entry and then from the device
    /// layer; null when no layer classifies anything yet.
    /// </summary>
    public DrmClassification? Drm { get; init; }

    /// <summary>Gets what the launch rule of C-0008 section 5 makes of <see cref="Drm"/>.</summary>
    public required LaunchDecision Launch { get; init; }

    /// <summary>Gets the graphics APIs the title uses.</summary>
    public required IReadOnlyList<GraphicsApi> GraphicsApi { get; init; }

    /// <summary>Gets the runtimes the title needs.</summary>
    public required IReadOnlyList<string> Runtimes { get; init; }

    /// <summary>Gets the expected peak memory that the game adds, in MiB, if a layer sets it.</summary>
    public int? MemoryEstimateMiB { get; init; }

    /// <summary>Gets the JIT pool size in MiB, if a layer sets it.</summary>
    public int? JitPoolMiB { get; init; }

    /// <summary>Gets the translator options.</summary>
    public required IReadOnlyDictionary<string, string> Translator { get; init; }

    /// <summary>Gets the internal resolution as a fraction of the output.</summary>
    public required double RenderScale { get; init; }

    /// <summary>Gets how MetalFX upscales the internal resolution.</summary>
    public required MetalFxMode MetalFx { get; init; }

    /// <summary>Gets the frame-rate cap, if a layer sets one.</summary>
    public int? FrameRateCap { get; init; }

    /// <summary>Gets the environment variables for the game.</summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>Gets the helper programs the session refuses to start.</summary>
    public required IReadOnlyList<string> BlockedExecutables { get; init; }

    /// <summary>Gets which Wine prefix the title runs in.</summary>
    public required PrefixMode Prefix { get; init; }

    /// <summary>Gets the stored rating, or <see cref="CompatRating.Untested"/> without one.</summary>
    public required CompatRating Rating { get; init; }

    /// <summary>Gets the test reports of the bundled entry.</summary>
    public required IReadOnlyList<TestReport> Reports { get; init; }
}
