// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class InstallStateTests
{
    [Fact]
    public void DefaultIsNotInstalled()
    {
        var state = default(InstallState);

        Assert.Equal(InstallStatus.NotInstalled, state.Status);
        Assert.Null(state.DownloadProgress);
        Assert.Equal(InstallState.NotInstalled, state);
    }

    [Fact]
    public void StatesOtherThanDownloadingHaveNoProgress()
    {
        Assert.Equal(InstallStatus.Installed, InstallState.Installed.Status);
        Assert.Null(InstallState.Installed.DownloadProgress);
        Assert.Equal(InstallStatus.UpdatePending, InstallState.UpdatePending.Status);
        Assert.Null(InstallState.UpdatePending.DownloadProgress);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.42)]
    [InlineData(1.0)]
    public void DownloadingKeepsItsProgress(double progress)
    {
        var state = InstallState.Downloading(progress);

        Assert.Equal(InstallStatus.Downloading, state.Status);
        Assert.Equal(progress, state.DownloadProgress);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void DownloadingRejectsProgressOutsideZeroToOne(double progress)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => InstallState.Downloading(progress));
    }
}
