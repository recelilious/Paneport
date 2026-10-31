// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// What one layer says about a title: a file of the bundled snapshot or of the device layer
/// (C-0008 sections 1 and 2). A null member is a field that the file leaves out.
/// </summary>
/// <remarks>
/// <para>
/// The members follow the fields of C-0008 section 2 in order. A file always has the current
/// schema version, <see cref="TitleEntryJson.SchemaVersion"/>, so the model leaves it out.
/// </para>
/// <para>
/// An empty list differs from a null one: a layer that sets an empty list replaces the list of the
/// layer below, and a layer that leaves the list out keeps it. <see cref="CompatPrecedence"/> merges
/// the layers, and <see cref="TitleEntryJson.Validate"/> checks an entry made in code.
/// </para>
/// </remarks>
public sealed record TitleEntry
{
    /// <summary>Gets the title's identity.</summary>
    public required TitleEntryId Id { get; init; }

    /// <summary>Gets the display name. A bundled entry always has one.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the program that the Direct path starts.</summary>
    public ExecutableSpec? Executable { get; init; }

    /// <summary>Gets the architecture of that program.</summary>
    public TitleArchitecture? Architecture { get; init; }

    /// <summary>Gets the classification; null when the title is not classified yet.</summary>
    public DrmClassification? Drm { get; init; }

    /// <summary>
    /// Gets the launch path that a bundled entry states, which must follow from its own
    /// <see cref="Drm"/> (C-0008 section 5). Only <see cref="LaunchPath.Direct"/> and
    /// <see cref="LaunchPath.Dock"/> are values of the field, and the device layer never holds it.
    /// </summary>
    public LaunchPath? Launch { get; init; }

    /// <summary>Gets the graphics APIs the title uses.</summary>
    public IReadOnlyList<GraphicsApi>? GraphicsApi { get; init; }

    /// <summary>Gets the runtimes the title needs, from <see cref="CompatLists.RuntimeIds"/>.</summary>
    public IReadOnlyList<string>? Runtimes { get; init; }

    /// <summary>Gets the expected peak memory that the game adds, in MiB.</summary>
    public int? MemoryEstimateMiB { get; init; }

    /// <summary>Gets the JIT pool size in MiB, from 256 to 896 (C-0006 section 4).</summary>
    public int? JitPoolMiB { get; init; }

    /// <summary>Gets the translator options, whose keys come from <see cref="CompatLists.TranslatorKeys"/>.</summary>
    public IReadOnlyDictionary<string, string>? Translator { get; init; }

    /// <summary>Gets the graphics settings.</summary>
    public GraphicsSettings? Graphics { get; init; }

    /// <summary>
    /// Gets the environment variables for the game, whose names come from
    /// <see cref="CompatLists.EnvironmentKeys"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>Gets the helper programs the session refuses to start.</summary>
    public HelperPolicy? HelperPolicy { get; init; }

    /// <summary>Gets which Wine prefix the title runs in.</summary>
    public PrefixMode? Prefix { get; init; }

    /// <summary>
    /// Gets the rating computed from the reports (C-0008 section 3). Only a bundled entry holds it.
    /// </summary>
    public CompatRating? Rating { get; init; }

    /// <summary>Gets the test reports. Only a bundled entry holds them.</summary>
    public IReadOnlyList<TestReport>? Reports { get; init; }
}
