// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Paneport.Core.ViewModels;
using Paneport.UI.Views;
using Xunit;

namespace Paneport.UI.Tests;

// The automated twin of the desktop head: the same shell over the test fakes.
public sealed class MainViewTests
{
    [AvaloniaFact]
    public void ShowsTheShellWithItsNavigationEntries()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();

        var entries = harness.NavigationButtons();
        Assert.Equal(["Library", "Settings", "Diagnostics"], entries.Select(entry => ShellHarness.TextsIn(entry).Single()));
        Assert.Equal(["Library"], entries.Where(entry => entry.Classes.Contains("selected")).Select(entry => entry.Content));
        Assert.All(entries, entry => Assert.True(entry.IsEffectivelyEnabled));

        Assert.Equal("Library", harness.View.FindControl<TextBlock>("Heading")?.Text);
        Assert.False(harness.View.FindControl<Button>("BackButton")?.IsVisible);
        Assert.NotNull(harness.Page<LibraryPage>());
        Assert.Empty(warnings.Entries);
    }

    [AvaloniaFact]
    public void EveryPageViewModelHasItsView()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();
        var library = Assert.IsType<LibraryViewModel>(harness.Shell.CurrentPage);

        PageViewModel[] pages =
        [
            library,
            new TitleDetailsViewModel(library.Titles[0], harness.Fakes.Log),
            new SettingsViewModel(harness.Fakes.Settings, harness.Fakes.Steam, harness.Fakes.Log),
            new DiagnosticsViewModel(harness.Fakes.Host, harness.Fakes.Log),
        ];
        Type[] views = [typeof(LibraryPage), typeof(TitleDetailsPage), typeof(SettingsPage), typeof(DiagnosticsPage)];

        var templates = Avalonia.Application.Current!.DataTemplates;
        Assert.Equal(views, pages.Select(page => templates.Single(template => template.Match(page)).Build(page)?.GetType()));
        Assert.Empty(warnings.Entries);
    }
}
