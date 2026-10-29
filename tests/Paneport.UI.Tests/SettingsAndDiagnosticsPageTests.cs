// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Paneport.Core.Settings;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Paneport.UI.Views;
using Xunit;

namespace Paneport.UI.Tests;

public sealed class SettingsAndDiagnosticsPageTests
{
    [AvaloniaFact]
    public void SettingsShowsTheLocaleAndTheSteamState()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();

        harness.OpenSection(ShellSection.Settings);

        var page = harness.Page<SettingsPage>();
        var locale = page.FindControl<ComboBox>("LocaleChoice");
        Assert.NotNull(locale);
        Assert.Equal(AppSettings.Default.Locale, Assert.IsType<LocaleOption>(locale.SelectedItem).Tag);
        Assert.Equal("Signed out", page.FindControl<TextBlock>("SteamAccount")?.Text);
        Assert.Equal("Settings", harness.View.FindControl<TextBlock>("Heading")?.Text);
        Assert.Empty(warnings.Entries);
    }

    // The automated twin of L0 acceptance step 8: a changed setting is still shown after
    // another page was opened.
    [AvaloniaFact]
    public void AChangedLocaleIsStillShownAfterVisitingAnotherPage()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();
        harness.OpenSection(ShellSection.Settings);

        harness.Page<SettingsPage>().FindControl<ComboBox>("LocaleChoice")!.SelectedItem = LocaleOption.Find("de-DE");
        harness.Settle();
        harness.OpenSection(ShellSection.Diagnostics);
        harness.OpenSection(ShellSection.Settings);

        var locale = harness.Page<SettingsPage>().FindControl<ComboBox>("LocaleChoice");
        Assert.Equal("de-DE", Assert.IsType<LocaleOption>(locale?.SelectedItem).Tag);
        Assert.Equal("de-DE", harness.Fakes.Settings.Saved?.Locale);
        Assert.Empty(warnings.Entries);
    }

    // A choice made before the saved value arrives would be overwritten by it.
    [AvaloniaFact]
    public void TheLocaleChoiceWaitsForTheSettingsToLoad()
    {
        using var warnings = AvaloniaWarnings.Record();
        var fakes = new FakeServices();
        fakes.Settings.Saved = new AppSettings { Locale = "de-DE" };
        fakes.Settings.LoadGate = new TaskCompletionSource();
        using var harness = ShellHarness.Show(fakes);

        harness.OpenSection(ShellSection.Settings);
        var locale = harness.Page<SettingsPage>().FindControl<ComboBox>("LocaleChoice");
        Assert.NotNull(locale);
        Assert.False(locale.IsEffectivelyEnabled);

        fakes.Settings.LoadGate.SetResult();
        harness.Settle();

        Assert.True(locale.IsEffectivelyEnabled);
        Assert.Equal("de-DE", Assert.IsType<LocaleOption>(locale.SelectedItem).Tag);
        Assert.Empty(warnings.Entries);
    }

    // A newer navigation cancels the load of the page it leaves. With a store that takes its time,
    // that cancellation must not reach the dispatcher, where it would end the app.
    [AvaloniaFact]
    public void OpeningAnotherSectionWhileSettingsLoadsRaisesNothing()
    {
        using var warnings = AvaloniaWarnings.Record();
        var fakes = new FakeServices();
        fakes.Settings.LoadGate = new TaskCompletionSource();
        using var harness = ShellHarness.Show(fakes);

        harness.OpenSection(ShellSection.Settings);
        Assert.True(harness.Shell.CurrentPage.IsLoading);
        harness.OpenSection(ShellSection.Diagnostics);

        Assert.IsType<DiagnosticsViewModel>(harness.Shell.CurrentPage);
        Assert.NotNull(harness.Page<DiagnosticsPage>());
        Assert.Empty(warnings.Entries);
    }

    [AvaloniaFact]
    public void DiagnosticsShowsTheHostsValues()
    {
        using var warnings = AvaloniaWarnings.Record();
        using var harness = ShellHarness.Show();

        harness.OpenSection(ShellSection.Diagnostics);

        var texts = ShellHarness.TextsIn(harness.Page<DiagnosticsPage>());
        Assert.Contains("Fake host for tests", texts);
        Assert.Contains("1.0", texts);
        Assert.Contains("6,144 MB", texts);
        Assert.Contains("5,120 MB", texts);
        Assert.Contains("1,024 MB", texts);
        Assert.Contains("Not reached", texts);
        Assert.Equal("Diagnostics", harness.View.FindControl<TextBlock>("Heading")?.Text);
        Assert.Empty(warnings.Entries);
    }
}
