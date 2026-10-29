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
/// Opening a section, even the one already marked, shows a new page for it and clears the back
/// stack. A page that another page opens goes on top of it, and going back returns to the page
/// below. The head shows the shell's view and then runs <see cref="StartCommand"/>, which loads
/// the Library page.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    private readonly AppServices _services;
    private readonly Stack<PageViewModel> _backStack = new();

    /// <summary>Creates the shell on the Library section. Its page loads when the shell starts.</summary>
    /// <param name="services">The services that the head provides.</param>
    public ShellViewModel(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Sections = [new(ShellSection.Library), new(ShellSection.Settings), new(ShellSection.Diagnostics)];
        MarkSection(ShellSection.Library);
        CurrentPage = CreatePage(ShellSection.Library);
    }

    /// <summary>Gets the entries of the navigation bar, one per section, in order.</summary>
    public IReadOnlyList<NavigationEntry> Sections { get; }

    /// <summary>Gets the section that the navigation bar marks: the last one opened.</summary>
    [ObservableProperty]
    public partial ShellSection SelectedSection { get; private set; }

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

    /// <summary>Loads the page that the shell starts on.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the page has loaded, or its load was cancelled.</returns>
    [RelayCommand]
    private Task StartAsync(CancellationToken cancellationToken) => LoadPageAsync(CurrentPage, cancellationToken);

    /// <summary>
    /// Opens a section on a new page, and clears the back stack. The section already marked opens
    /// again too, so choosing it returns to its first page.
    /// </summary>
    /// <param name="section">The section to open.</param>
    /// <param name="cancellationToken">Cancels the page's load.</param>
    /// <returns>A task that completes when the page is shown and loaded, or its load was cancelled.</returns>
    /// <remarks>
    /// The command stays available while a page loads, so that the navigation bar never waits for
    /// a slow page; a newer run cancels the load of the page it replaces.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
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

    private void MarkSection(ShellSection section)
    {
        SelectedSection = section;
        foreach (var entry in Sections)
        {
            entry.IsSelected = entry.Section == section;
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
