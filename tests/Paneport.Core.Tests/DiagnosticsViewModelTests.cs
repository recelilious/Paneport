// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class DiagnosticsViewModelTests
{
    private const ulong Megabyte = FakeRuntimeHost.BytesPerMegabyte;

    [Fact]
    public void NamesTheHostAndItsVersion()
    {
        var fakes = new FakeServices();

        var diagnostics = new DiagnosticsViewModel(fakes.Host, fakes.Log);

        Assert.Equal("Diagnostics", diagnostics.Header);
        Assert.Equal("Fake host for tests", diagnostics.HostName);
        Assert.Equal("1.0", diagnostics.AbiVersion);
    }

    [Fact]
    public void ShowsTheBudgetBelowTheGameModeLevel()
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = new MemoryBudget(6_144 * Megabyte, 5_632 * Megabyte, 512 * Megabyte, gameModeLevelReached: false);

        var diagnostics = new DiagnosticsViewModel(fakes.Host, fakes.Log);

        Assert.Equal("6,144 MB", diagnostics.MemoryLimit);
        Assert.Equal("5,632 MB", diagnostics.AvailableMemory);
        Assert.Equal("512 MB", diagnostics.Footprint);
        Assert.Equal("Not reached", diagnostics.GameModeLevel);
    }

    [Fact]
    public void ShowsTheBudgetAtTheGameModeLevel()
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = new MemoryBudget(8_192 * Megabyte, 7_168 * Megabyte, 1_024 * Megabyte, gameModeLevelReached: true);

        var diagnostics = new DiagnosticsViewModel(fakes.Host, fakes.Log);

        Assert.Equal("8,192 MB", diagnostics.MemoryLimit);
        Assert.Equal("7,168 MB", diagnostics.AvailableMemory);
        Assert.Equal("1,024 MB", diagnostics.Footprint);
        Assert.Equal("Reached", diagnostics.GameModeLevel);
    }

    [Fact]
    public void RefreshReadsTheBudgetAgainAndAnnouncesTheChange()
    {
        var fakes = new FakeServices();
        var diagnostics = new DiagnosticsViewModel(fakes.Host, fakes.Log);
        var changed = new List<string?>();
        diagnostics.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        fakes.Host.Budget = new MemoryBudget(8_192 * Megabyte, 4_096 * Megabyte, 4_096 * Megabyte, gameModeLevelReached: true);

        diagnostics.RefreshCommand.Execute(null);

        Assert.Equal(2, fakes.Host.BudgetReads);
        Assert.Equal("8,192 MB", diagnostics.MemoryLimit);
        Assert.Equal("Reached", diagnostics.GameModeLevel);
        Assert.Contains(nameof(DiagnosticsViewModel.MemoryLimit), changed);
        Assert.Contains(nameof(DiagnosticsViewModel.GameModeLevel), changed);
    }
}
