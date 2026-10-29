// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class TitleViewModelTests
{
    [Fact]
    public void ShowsTheNameIdentityAndSource()
    {
        var steam = new TitleViewModel(TestTitles.InstalledDirect);
        var local = new TitleViewModel(TestTitles.Local);

        Assert.Same(TestTitles.InstalledDirect, steam.Title);
        Assert.Equal("Installed Direct Sample", steam.Name);
        Assert.Equal("steam-4100000010", steam.Identity);
        Assert.Equal("Steam app 4100000010", steam.SourceText);
        Assert.Equal("local-local-sample", local.Identity);
        Assert.Equal("Brought in without Steam", local.SourceText);
    }

    [Fact]
    public void ShowsEveryInstallState()
    {
        Assert.Equal("Not installed", InstallStateText(InstallState.NotInstalled));
        Assert.Equal("Installed", InstallStateText(InstallState.Installed));
        Assert.Equal("Update pending", InstallStateText(InstallState.UpdatePending));
        Assert.Equal("Downloading, 0%", InstallStateText(InstallState.Downloading(0)));
        Assert.Equal("Downloading, 42%", InstallStateText(InstallState.Downloading(0.42)));
        Assert.Equal("Downloading, 99%", InstallStateText(InstallState.Downloading(0.999)));
    }

    // The doubles nearest to these fractions lie just below them, so their product with 100 does
    // too; rounding down must still give the whole percentage.
    [Theory]
    [InlineData(0.29, "Downloading, 29%")]
    [InlineData(0.57, "Downloading, 57%")]
    [InlineData(0.58, "Downloading, 58%")]
    [InlineData(0.9999999, "Downloading, 99%")]
    [InlineData(1.0, "Downloading, 100%")]
    public void ShowsExactPercentagesAndNever100BeforeTheEnd(double progress, string expected)
    {
        Assert.Equal(expected, InstallStateText(InstallState.Downloading(progress)));
    }

    [Fact]
    public void ShowsEveryLaunchPath()
    {
        var unclassified = WithLaunchPath(LaunchPath.Unclassified);
        var direct = WithLaunchPath(LaunchPath.Direct);
        var dock = WithLaunchPath(LaunchPath.Dock);

        Assert.Equal("Not classified yet", unclassified.LaunchPathText);
        Assert.Equal("Direct", direct.LaunchPathText);
        Assert.Equal("Dock", dock.LaunchPathText);
        Assert.Equal(3, new[] { unclassified, direct, dock }.Select(title => title.LaunchPathDescription).Distinct().Count());
    }

    private static string InstallStateText(InstallState state) =>
        new TitleViewModel(new Title(TitleId.Steam(4_100_000_010), "Sample", state, LaunchPath.Direct)).InstallStateText;

    private static TitleViewModel WithLaunchPath(LaunchPath launchPath) =>
        new(new Title(TitleId.Steam(4_100_000_010), "Sample", InstallState.Installed, launchPath));
}
