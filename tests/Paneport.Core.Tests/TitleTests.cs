// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class TitleTests
{
    [Fact]
    public void KeepsItsValues()
    {
        var id = TitleId.Steam(4_100_000_010);
        var state = InstallState.Downloading(0.5);

        var title = new Title(id, "Sample", state, LaunchPath.Dock);

        Assert.Equal(id, title.Id);
        Assert.Equal("Sample", title.Name);
        Assert.Equal(state, title.InstallState);
        Assert.Equal(LaunchPath.Dock, title.LaunchPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyNameIsRejected(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new Title(TitleId.Steam(4_100_000_010), name, InstallState.Installed, LaunchPath.Direct));
    }

    [Fact]
    public void UndefinedLaunchPathIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Title(TitleId.Steam(4_100_000_010), "Sample", InstallState.Installed, (LaunchPath)42));
    }

    // C-0007 section 5: a title not obtained through Steam can only start directly.
    [Fact]
    public void LocalTitleNeverTakesTheDockPath()
    {
        Assert.Throws<ArgumentException>(() =>
            new Title(TitleId.Local("sample"), "Sample", InstallState.Installed, LaunchPath.Dock));
    }

    [Theory]
    [InlineData(LaunchPath.Unclassified)]
    [InlineData(LaunchPath.Direct)]
    public void LocalTitleStartsDirectlyOrIsUnclassified(LaunchPath launchPath)
    {
        var title = new Title(TitleId.Local("sample"), "Sample", InstallState.Installed, launchPath);

        Assert.Equal(launchPath, title.LaunchPath);
    }
}
