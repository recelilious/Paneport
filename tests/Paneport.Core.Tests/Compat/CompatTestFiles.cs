// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Tests.Compat;

// Where the compatibility tests find their files in the build output: the fixtures of this
// project, and the copies of repository files that the project file makes.
internal static class CompatTestFiles
{
    public static string Fixture(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "Compat", "Fixtures", .. parts]);

    public static string Repository(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "Repository", .. parts]);

    public static IEnumerable<string> FixtureNames(params string[] folder) =>
        Directory.EnumerateFiles(Fixture(folder), "*.json")
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal);
}
