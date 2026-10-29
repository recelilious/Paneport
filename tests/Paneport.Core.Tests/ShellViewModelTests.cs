// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public async Task StartsOnTheLibraryWithThreeSections()
    {
        var fakes = new FakeServices();
        var shell = new ShellViewModel(fakes.ToAppServices());

        await shell.StartCommand.ExecuteAsync(null);

        Assert.Equal([ShellSection.Library, ShellSection.Settings, ShellSection.Diagnostics], shell.Sections);
        Assert.Equal(ShellSection.Library, shell.SelectedSection);
        var library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);
        Assert.Equal(TestTitles.All.Count, library.Titles.Count);
        Assert.False(shell.CanGoBack);
    }

    [Theory]
    [InlineData(ShellSection.Library, typeof(LibraryViewModel))]
    [InlineData(ShellSection.Settings, typeof(SettingsViewModel))]
    [InlineData(ShellSection.Diagnostics, typeof(DiagnosticsViewModel))]
    public async Task OpeningASectionShowsANewPageForIt(ShellSection section, Type pageType)
    {
        var shell = new ShellViewModel(new FakeServices().ToAppServices());
        var startPage = shell.CurrentPage;

        await shell.OpenSectionCommand.ExecuteAsync(section);

        Assert.IsType(pageType, shell.CurrentPage);
        Assert.NotSame(startPage, shell.CurrentPage);
        Assert.Equal(section, shell.SelectedSection);
        Assert.False(shell.CanGoBack);
    }

    [Fact]
    public void ChoosingASectionInTheNavigationOpensIt()
    {
        var shell = new ShellViewModel(new FakeServices().ToAppServices());

        shell.SelectedSection = ShellSection.Diagnostics;

        Assert.IsType<DiagnosticsViewModel>(shell.CurrentPage);
    }

    // S0.3.3: every page can be reached and left again.
    [Fact]
    public async Task NavigatesToEveryPageAndBack()
    {
        var fakes = new FakeServices();
        var shell = new ShellViewModel(fakes.ToAppServices());
        await shell.StartCommand.ExecuteAsync(null);

        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Settings);
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Diagnostics);
        Assert.IsType<DiagnosticsViewModel>(shell.CurrentPage);
        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Library);
        var library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);

        await library.OpenTitleCommand.ExecuteAsync(library.Titles[0]);
        Assert.IsType<TitleDetailsViewModel>(shell.CurrentPage);
        Assert.True(shell.CanGoBack);
        Assert.True(shell.GoBackCommand.CanExecute(null));

        shell.GoBackCommand.Execute(null);

        Assert.Same(library, shell.CurrentPage);
        Assert.Equal(ShellSection.Library, shell.SelectedSection);
        Assert.False(shell.CanGoBack);
        Assert.False(shell.GoBackCommand.CanExecute(null));
    }

    [Fact]
    public async Task OpeningASectionClearsTheBackStack()
    {
        var shell = new ShellViewModel(new FakeServices().ToAppServices());
        await shell.StartCommand.ExecuteAsync(null);
        var library = Assert.IsType<LibraryViewModel>(shell.CurrentPage);
        await library.OpenTitleCommand.ExecuteAsync(library.Titles[0]);

        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Settings);

        Assert.False(shell.CanGoBack);
        shell.GoBackCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(shell.CurrentPage);
    }

    [Fact]
    public async Task LogsThePagesItOpens()
    {
        var fakes = new FakeServices();
        var shell = new ShellViewModel(fakes.ToAppServices());

        await shell.OpenSectionCommand.ExecuteAsync(ShellSection.Settings);

        Assert.Contains(fakes.Log.Lines, line => line.Message == $"Opened {nameof(SettingsViewModel)}.");
    }
}
