// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Paneport.UI.Views;
using Xunit;

namespace Paneport.UI.Tests;

public sealed class LibraryPageTests
{
    [AvaloniaFact]
    public void ListsEveryTitleWithItsStateAndLaunchPath()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();

        var texts = ShellHarness.TextsIn(harness.Page<LibraryPage>());

        Assert.All(TestTitles.All.Select(title => new TitleViewModel(title)), title =>
        {
            Assert.Contains(title.Name, texts);
            Assert.Contains(title.Identity, texts);
            Assert.Contains(title.InstallStateText, texts);
            Assert.Contains(title.LaunchPathText, texts);
        });
        Assert.Empty(warnings.Entries);
    }

    // On a phone-sized window the long name needs several lines with any font, so this does not
    // depend on the headless platform's font metrics.
    [AvaloniaFact]
    public void TheLongNameWrapsInsteadOfBeingCutOff()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show(width: 390, height: 844);

        var name = harness.Page<LibraryPage>().GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text == TestTitles.LongName.Name);

        Assert.Equal(TextWrapping.Wrap, name.TextWrapping);
        Assert.Equal(TextTrimming.None, name.TextTrimming);
        Assert.True(name.Bounds.Width <= harness.Window.Bounds.Width);
        Assert.True(name.Bounds.Height > name.FontSize * 2, "The long name should take more than one line.");
        Assert.Empty(warnings.Entries);
    }

    [AvaloniaFact]
    public void ChoosingATitleOpensItsDetailsAndBackReturnsToTheLibrary()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();
        var chosen = new TitleViewModel(TestTitles.DownloadingDock);
        var button = harness.Page<LibraryPage>().GetVisualDescendants().OfType<Button>()
            .Single(candidate => AutomationProperties.GetName(candidate) == chosen.Name);

        harness.Click(button);

        Assert.IsType<TitleDetailsViewModel>(harness.Shell.CurrentPage);
        var details = ShellHarness.TextsIn(harness.Page<TitleDetailsPage>());
        Assert.Contains(chosen.Name, details);
        Assert.Contains(chosen.Identity, details);
        Assert.Contains(chosen.InstallStateText, details);
        Assert.Contains(chosen.LaunchPathText, details);
        Assert.Equal(chosen.Name, harness.View.FindControl<TextBlock>("Heading")?.Text);

        var back = harness.View.FindControl<Button>("BackButton");
        Assert.NotNull(back);
        Assert.True(back.IsVisible);
        harness.Click(back);

        Assert.IsType<LibraryViewModel>(harness.Shell.CurrentPage);
        Assert.NotNull(harness.Page<LibraryPage>());
        Assert.Equal("Library", harness.View.FindControl<TextBlock>("Heading")?.Text);
        Assert.False(back.IsVisible);
        Assert.Empty(warnings.Entries);
    }
}
