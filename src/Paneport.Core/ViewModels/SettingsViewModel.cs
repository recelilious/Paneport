// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Logging;
using Paneport.Core.Settings;
using Paneport.Core.Steam;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The Settings page. A changed setting is saved at once; there is no Save button.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly ISettingsStore _store;
    private readonly ISteamService _steam;
    private AppSettings _settings = AppSettings.Default;

    /// <summary>Creates the page.</summary>
    /// <param name="store">The settings store that the page reads and writes.</param>
    /// <param name="steam">The Steam service, whose sign-in state the page shows.</param>
    /// <param name="log">The log.</param>
    public SettingsViewModel(ISettingsStore store, ISteamService steam, ILog log)
        : base(log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(steam);
        _store = store;
        _steam = steam;
    }

    /// <summary>Gets the page's heading.</summary>
    public override string Header => "Settings";

    /// <summary>Gets the locales to choose from.</summary>
    public IReadOnlyList<LocaleOption> Locales { get; } = LocaleOption.Supported;

    /// <summary>
    /// Gets or sets the locale that game sessions hand to Wine. Setting it saves the settings,
    /// except while the page loads, when the view also keeps the choice disabled. It is null until
    /// the page has loaded, or when the saved locale is not one of <see cref="Locales"/>.
    /// </summary>
    [ObservableProperty]
    public partial LocaleOption? SelectedLocale { get; set; }

    /// <summary>Gets whether the user is signed in to Steam, as text.</summary>
    public string SteamAccountText => _steam.SignInState == SteamSignInState.SignedIn ? "Signed in" : "Signed out";

    /// <inheritdoc />
    protected override async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        _settings = await _store.LoadAsync(cancellationToken);
        SelectedLocale = LocaleOption.Find(_settings.Locale);
    }

    partial void OnSelectedLocaleChanged(LocaleOption? value)
    {
        // While the page loads, a change is either the loaded value arriving or a choice that the
        // load would overwrite; neither is saved.
        if (IsLoading || value is null || value.Tag == _settings.Locale)
        {
            return;
        }

        _settings = _settings with { Locale = value.Tag };
        SaveCommand.Execute(null);
    }

    /// <summary>Saves the settings as the page shows them.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes when the settings are saved or the failure is logged.</returns>
    /// <remarks>A newer change cancels a save that has not finished, and saves in its place.</remarks>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = _settings;
        try
        {
            await _store.SaveAsync(settings, cancellationToken);
            Log.Write(LogLevel.Information, nameof(SettingsViewModel), $"Saved the settings, locale {settings.Locale}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Not a failure: the newer change's own save follows.
        }
        catch (Exception exception)
        {
            // Only the exception type is logged; its message can hold a local path.
            Log.Write(LogLevel.Error, nameof(SettingsViewModel), $"Saving the settings failed with {exception.GetType().Name}.");
        }
    }
}
