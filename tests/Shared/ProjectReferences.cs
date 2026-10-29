// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Paneport.Tests.Shared;

// Reads project references from the test assembly's deps.json file. The build records every
// project reference there, including one that no code uses yet; the compiler drops unused
// references from assembly metadata, so reflection would miss exactly those.
internal static class ProjectReferences
{
    // Returns the solution projects that the named project references directly.
    public static HashSet<string> Of(string project)
    {
        var testAssembly = typeof(ProjectReferences).Assembly.GetName().Name;
        var path = Path.Combine(AppContext.BaseDirectory, $"{testAssembly}.deps.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        var solutionProjects = root.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "project")
            .Select(library => NameOf(library.Name))
            .ToHashSet(StringComparer.Ordinal);

        var target = root.GetProperty("runtimeTarget").GetProperty("name").GetString()
            ?? throw new InvalidDataException($"{path} names no runtime target.");
        var entry = root.GetProperty("targets").GetProperty(target).EnumerateObject()
            .Single(library => NameOf(library.Name) == project);

        var references = new HashSet<string>(StringComparer.Ordinal);
        if (entry.Value.TryGetProperty("dependencies", out var dependencies))
        {
            references.UnionWith(dependencies.EnumerateObject()
                .Select(dependency => dependency.Name)
                .Where(solutionProjects.Contains));
        }

        return references;
    }

    // Library keys in deps.json have the form "<name>/<version>".
    private static string NameOf(string libraryKey) =>
        libraryKey[..libraryKey.IndexOf('/', StringComparison.Ordinal)];
}
