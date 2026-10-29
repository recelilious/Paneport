// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Tests.Shared;
using Xunit;

namespace Paneport.UI.Tests;

// C-0004 section 2: Paneport.UI depends only on Paneport.Core, and a test project depends only
// on the project it tests and on that project's dependencies.
public sealed class DependencyDirectionTests
{
    [Fact]
    public void UIReferencesOnlyCore()
    {
        Assert.Subset(new HashSet<string> { "Paneport.Core" }, ProjectReferences.Of("Paneport.UI"));
    }

    [Fact]
    public void TestProjectReferencesUIAndItsDependenciesOnly()
    {
        var references = ProjectReferences.Of("Paneport.UI.Tests");

        Assert.Contains("Paneport.UI", references);
        Assert.Subset(new HashSet<string> { "Paneport.UI", "Paneport.Core" }, references);
    }
}
