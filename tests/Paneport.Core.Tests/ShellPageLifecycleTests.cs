// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

// The shell tells a page when it starts and stops being shown, so that a page that follows a live
// source, such as Diagnostics with the memory timeline, follows it only while it is on screen; and
// a head that gives a developer menu gets a Developer section (M1.3).
public sealed class ShellPageLifecycleTests
{
    [Fact]
    public async Task APageIsShownWhileItIsTheCurrentPageAndOnItsReturn()
    {
        ProbePage? probe = null;
        var shell = new ShellViewModel(new FakeServices().ToAppServices(), _ => probe = new ProbePage(new RecordingLog()));

        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Developer);
        Assert.NotNull(probe);
        Assert.Equal(["shown"], probe.Events);

        await shell.PushAsync(new ProbePage(new RecordingLog()), TestContext.Current.CancellationToken);
        Assert.Equal(["shown", "hidden"], probe.Events);

        shell.GoBackCommand.Execute(null);
        Assert.Equal(["shown", "hidden", "shown"], probe.Events);
        Assert.Same(probe, shell.CurrentPage);

        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Library);
        Assert.Equal(["shown", "hidden", "shown", "hidden"], probe.Events);
    }

    [Fact]
    public void TheDeveloperSectionAppearsOnlyWithADeveloperMenu()
    {
        var plain = new ShellViewModel(new FakeServices().ToAppServices());
        var withMenu = new ShellViewModel(new FakeServices().ToAppServices(), _ => new ProbePage(new RecordingLog()));

        Assert.DoesNotContain(plain.Sections, entry => entry.Section == ShellSection.Developer);
        Assert.Equal(
            [ShellSection.Library, ShellSection.Settings, ShellSection.Diagnostics, ShellSection.Developer],
            withMenu.Sections.Select(entry => entry.Section));
        Assert.Equal("Developer", withMenu.Sections[^1].Name);
    }

    [Fact]
    public async Task TheDeveloperMenuGetsTheShellAsItsNavigator()
    {
        INavigator? given = null;
        var shell = new ShellViewModel(new FakeServices().ToAppServices(), navigator =>
        {
            given = navigator;
            return new ProbePage(new RecordingLog());
        });

        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Developer);

        Assert.Same(shell, given);
        Assert.Equal(ShellSection.Developer, shell.SelectedSection);
    }

    [Fact]
    public async Task OpeningTheDeveloperSectionWithoutAMenuFails()
    {
        var shell = new ShellViewModel(new FakeServices().ToAppServices());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => shell.OpenSectionCommand.ExecuteAsync(ShellSection.Developer));
    }

    private sealed class ProbePage(ILog log) : PageViewModel(log)
    {
        public List<string> Events { get; } = [];

        public override string Header => "Probe";

        protected override void OnShown() => Events.Add("shown");

        protected override void OnHidden() => Events.Add("hidden");
    }
}
