// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Logging;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The shell: the navigation between the top-level sections, the back stack and the page shown.
/// </summary>
/// <remarks>
/// Opening a section shows a new page for it and clears the back stack. A page that another
/// page opens goes on top of it, and going back returns to the page below. The head shows the
/// shell's view and then runs <see cref="StartCommand"/>, which loads the Library page.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    private readonly AppServices _services;
    private readonly Stack<PageViewModel> _backStack = new();
    private bool _openingSection;

    /// <summary>Creates the shell on the Library section. Its page loads when the shell starts.</summary>
    /// <param name="services">The services that the head provides.</param>
    public ShellViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        MarkSection(ShellSection.Library);
        CurrentPage = CreatePage(ShellSection.Library);
    }

    /// <summary>Gets the sections that the navigation offers, in order.</summary>
    public IReadOnlyList<ShellSection> Sections { get; } = [ShellSection.Library, ShellSection.Settings, ShellSection.Diagnostics];

    /// <summary>Gets or sets the section that the navigation marks. Setting it opens the section.</summary>
    [ObservableProperty]
    public partial ShellSection SelectedSection { get; set; }

    /// <summary>Gets the page that the shell shows.</summary>
    [ObservableProperty]
    public partial PageViewModel CurrentPage { get; private set; }

    /// <summary>Gets a value indicating whether there is a page to go back to.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    public partial bool CanGoBack { get; private set; }

    /// <inheritdoc />
    public Task PushAsync(PageViewModel page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        _backStack.Push(CurrentPage);
        return ShowAsync(page, cancellationToken);
    }

    partial void OnSelectedSectionChanged(ShellSection value)
    {
        if (!_openingSection)
        {
            OpenSectionCommand.Execute(value);
        }
    }

    /// <summary>Loads the page that the shell starts on.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the page has loaded, or its load was cancelled.</returns>
    [RelayCommand]
    private Task StartAsync(CancellationToken cancellationToken) => LoadPageAsync(CurrentPage, cancellationToken);

    /// <summary>Opens a section on a new page, and clears the back stack.</summary>
    /// <param name="section">The section to open.</param>
    /// <param name="cancellationToken">Cancels the page's load.</param>
    /// <returns>A task that completes when the page is shown and loaded, or its load was cancelled.</returns>
    [RelayCommand]
    private Task OpenSectionAsync(ShellSection section, CancellationToken cancellationToken)
    {
        var page = CreatePage(section);
        MarkSection(section);
        _backStack.Clear();
        return ShowAsync(page, cancellationToken);
    }

    /// <summary>Returns to the page below the current one.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_backStack.TryPop(out var page))
        {
            CurrentPage = page;
            CanGoBack = _backStack.Count > 0;
            _services.Log.Write(LogLevel.Information, nameof(ShellViewModel), $"Went back to {page.GetType().Name}.");
        }
    }

    // Marks a section that the shell opens itself, which must not open it a second time.
    private void MarkSection(ShellSection section)
    {
        _openingSection = true;
        try
        {
            SelectedSection = section;
        }
        finally
        {
            _openingSection = false;
        }
    }

    private Task ShowAsync(PageViewModel page, CancellationToken cancellationToken)
    {
        CurrentPage = page;
        CanGoBack = _backStack.Count > 0;
        _services.Log.Write(LogLevel.Information, nameof(ShellViewModel), $"Opened {page.GetType().Name}.");
        return LoadPageAsync(page, cancellationToken);
    }

    // A newer navigation cancels the load of the page that it replaces, through the command's
    // token. That is a normal end. Let through, the cancellation would reach the command, which
    // rethrows it on the UI thread, where nothing handles it.
    private static async Task LoadPageAsync(PageViewModel page, CancellationToken cancellationToken)
    {
        try
        {
            await page.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page is no longer shown, so it needs no data.
        }
    }

    private PageViewModel CreatePage(ShellSection section) => section switch
    {
        ShellSection.Library => new LibraryViewModel(_services.Library, this, _services.Log),
        ShellSection.Settings => new SettingsViewModel(_services.Settings, _services.Steam, _services.Log),
        ShellSection.Diagnostics => new DiagnosticsViewModel(_services.Host, _services.Log),
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, "The section is not defined."),
    };
}
