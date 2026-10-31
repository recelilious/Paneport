// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;

namespace Paneport.Core.Compat;

/// <summary>
/// The lists of the title schema that the app checks in code: the allow-lists of translator
/// options and environment variables, and the fixed list of runtimes (C-0008 section 2). A test
/// keeps them equal to <c>compat/schema/title.schema.json</c>.
/// </summary>
/// <remarks>
/// The allow-lists start empty. A later stage adds an entry to the schema and here in one change
/// (C-0008 section 5). The environment allow-list never holds <c>SteamAppId</c>,
/// <c>SteamGameId</c>, <c>SteamAppPath</c> or any other variable that <c>steam_api</c> or a DRM
/// wrapper reads.
/// </remarks>
public static class CompatLists
{
    /// <summary>Gets the translator options that an entry may set.</summary>
    public static IReadOnlySet<string> TranslatorKeys { get; } = FrozenSet<string>.Empty;

    /// <summary>Gets the environment variables that an entry may set.</summary>
    public static IReadOnlySet<string> EnvironmentKeys { get; } = FrozenSet<string>.Empty;

    /// <summary>
    /// Gets the runtime identifiers, such as <c>vcredist-v14-x64</c> for the Microsoft Visual C++
    /// v14 redistributable.
    /// </summary>
    public static IReadOnlySet<string> RuntimeIds { get; } =
        FrozenSet.Create(StringComparer.Ordinal, "vcredist-v14-x64", "vcredist-v14-x86");
}
