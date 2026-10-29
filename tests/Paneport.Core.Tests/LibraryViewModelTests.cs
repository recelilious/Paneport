// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task ListsEveryTitleSortedByName()
    {
        var fakes = new FakeServices();
        var library = new LibraryViewModel(fakes.Library, new FakeNavigator(), fakes.Log);

        await library.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Library", library.Header);
        Assert.Null(library.LoadError);
        Assert.Equal(
            TestTitles.All.Select(title => title.Name).Order(StringComparer.OrdinalIgnoreCase),
            library.Titles.Select(title => title.Name));
    }

    [Fact]
    public async Task OpeningATitleShowsItsDetailsPage()
    {
        var fakes = new FakeServices();
        var navigator = new FakeNavigator();
        var library = new LibraryViewModel(fakes.Library, navigator, fakes.Log);
        await library.LoadAsync(TestContext.Current.CancellationToken);
        var chosen = library.Titles.Single(title => title.Title == TestTitles.DownloadingDock);

        await library.OpenTitleCommand.ExecuteAsync(chosen);

        var details = Assert.IsType<TitleDetailsViewModel>(Assert.Single(navigator.Pushed));
        Assert.Same(chosen, details.Title);
        Assert.Equal(TestTitles.DownloadingDock.Name, details.Header);
    }

    [Fact]
    public async Task ALibraryThatFailsShowsAnError()
    {
        var fakes = new FakeServices();
        fakes.Library.Failure = new InvalidOperationException();
        var library = new LibraryViewModel(fakes.Library, new FakeNavigator(), fakes.Log);

        await library.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(library.LoadError);
        Assert.Empty(library.Titles);
    }
}
