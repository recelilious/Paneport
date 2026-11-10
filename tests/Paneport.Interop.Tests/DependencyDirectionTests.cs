// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Tests.Shared;
using Xunit;

namespace Paneport.Interop.Tests;

// C-0004 section 2: Paneport.Interop depends on nothing else in the solution, and a test
// project depends only on the project it tests and on that project's dependencies.
public sealed class DependencyDirectionTests
{
    [Fact]
    public void InteropReferencesNoOtherProject()
    {
        Assert.Empty(ProjectReferences.Of("Paneport.Interop"));
    }

    [Fact]
    public void TestProjectReferencesInteropOnly()
    {
        Assert.Equal(["Paneport.Interop"], ProjectReferences.Of("Paneport.Interop.Tests").Order());
    }
}
