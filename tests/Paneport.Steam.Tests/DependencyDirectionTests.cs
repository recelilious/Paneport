// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Tests.Shared;
using Xunit;

namespace Paneport.Steam.Tests;

// C-0004 section 2: Paneport.Steam depends only on Paneport.Core, and a test project depends
// only on the project it tests and on that project's dependencies.
public sealed class DependencyDirectionTests
{
    [Fact]
    public void SteamReferencesOnlyCore()
    {
        Assert.Subset(new HashSet<string> { "Paneport.Core" }, ProjectReferences.Of("Paneport.Steam"));
    }

    [Fact]
    public void TestProjectReferencesSteamAndItsDependenciesOnly()
    {
        var references = ProjectReferences.Of("Paneport.Steam.Tests");

        Assert.Contains("Paneport.Steam", references);
        Assert.Subset(new HashSet<string> { "Paneport.Steam", "Paneport.Core" }, references);
    }
}
