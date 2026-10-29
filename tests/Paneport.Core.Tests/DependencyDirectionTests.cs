// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Tests.Shared;
using Xunit;

namespace Paneport.Core.Tests;

// C-0004 section 2: Paneport.Core depends on nothing else in the solution, and a test project
// depends only on the project it tests and on that project's dependencies.
public sealed class DependencyDirectionTests
{
    [Fact]
    public void CoreReferencesNoOtherSolutionProject()
    {
        Assert.Empty(ProjectReferences.Of("Paneport.Core"));
    }

    [Fact]
    public void TestProjectReferencesOnlyCore()
    {
        Assert.Equal(["Paneport.Core"], ProjectReferences.Of("Paneport.Core.Tests"));
    }
}
