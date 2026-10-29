// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Settings;
using Paneport.Core.Steam;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task ShowsTheSavedLocaleWithoutSavingIt()
    {
        var fakes = new FakeServices();
        fakes.Settings.Saved = new AppSettings { Locale = "de-DE" };

        var settings = await LoadAsync(fakes);

        Assert.Equal("Settings", settings.Header);
        Assert.Equal("de-DE", settings.SelectedLocale?.Tag);
        Assert.Equal(0, fakes.Settings.SaveCount);
    }

    [Fact]
    public async Task ChangingTheLocaleSavesIt()
    {
        var fakes = new FakeServices();
        var settings = await LoadAsync(fakes);

        settings.SelectedLocale = LocaleOption.Find("fr-FR");
        await (settings.SaveCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal("fr-FR", fakes.Settings.Saved?.Locale);
        Assert.Equal(1, fakes.Settings.SaveCount);
    }

    // S0.3.5: a setting round-trips through the store, as when the user leaves the page and
    // comes back to a new one.
    [Fact]
    public async Task ALocaleRoundTripsThroughTheStore()
    {
        var fakes = new FakeServices();
        var first = await LoadAsync(fakes);
        first.SelectedLocale = LocaleOption.Find("ja-JP");
        await (first.SaveCommand.ExecutionTask ?? Task.CompletedTask);

        var second = await LoadAsync(fakes);

        Assert.Equal("ja-JP", second.SelectedLocale?.Tag);
    }

    [Fact]
    public async Task ChoosingTheShownLocaleAgainDoesNotSave()
    {
        var fakes = new FakeServices();
        var settings = await LoadAsync(fakes);

        settings.SelectedLocale = LocaleOption.Find(AppSettings.Default.Locale);

        Assert.Equal(0, fakes.Settings.SaveCount);
    }

    [Fact]
    public async Task AFailedSaveIsLoggedWithoutItsMessage()
    {
        var fakes = new FakeServices();
        fakes.Settings.SaveFailure = new IOException("detail that must stay out of the log");
        var settings = await LoadAsync(fakes);

        settings.SelectedLocale = LocaleOption.Find("pl-PL");
        await (settings.SaveCommand.ExecutionTask ?? Task.CompletedTask);

        var error = Assert.Single(fakes.Log.Lines, line => line.Level == LogLevel.Error);
        Assert.Contains(nameof(IOException), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("detail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnsupportedSavedLocaleLeavesNoChoiceMarked()
    {
        var fakes = new FakeServices();
        fakes.Settings.Saved = new AppSettings { Locale = "xx-XX" };

        var settings = await LoadAsync(fakes);

        Assert.Null(settings.SelectedLocale);
    }

    [Theory]
    [InlineData(SteamSignInState.SignedOut, "Signed out")]
    [InlineData(SteamSignInState.SignedIn, "Signed in")]
    public void ShowsTheSteamSignInState(SteamSignInState state, string expected)
    {
        var fakes = new FakeServices();
        fakes.Steam.SignInState = state;

        var settings = new SettingsViewModel(fakes.Settings, fakes.Steam, fakes.Log);

        Assert.Equal(expected, settings.SteamAccountText);
    }

    private static async Task<SettingsViewModel> LoadAsync(FakeServices fakes)
    {
        var settings = new SettingsViewModel(fakes.Settings, fakes.Steam, fakes.Log);
        await settings.LoadAsync(TestContext.Current.CancellationToken);
        return settings;
    }
}
