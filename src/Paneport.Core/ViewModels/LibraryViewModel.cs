// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Library;
using Paneport.Core.Logging;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The Library page: the titles in the library, each of which opens its Title details page.
/// </summary>
public sealed partial class LibraryViewModel : PageViewModel
{
    private readonly ILibraryService _library;
    private readonly INavigator _navigator;

    /// <summary>Creates the page.</summary>
    /// <param name="library">The library to list.</param>
    /// <param name="navigator">Opens the Title details page.</param>
    /// <param name="log">The log.</param>
    public LibraryViewModel(ILibraryService library, INavigator navigator, ILog log)
        : base(log)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(navigator);
        _library = library;
        _navigator = navigator;
    }

    /// <summary>Gets the page's heading.</summary>
    public override string Header => "Library";

    /// <summary>Gets the titles, sorted by name.</summary>
    public ObservableCollection<TitleViewModel> Titles { get; } = [];

    /// <inheritdoc />
    protected override async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var titles = await _library.GetTitlesAsync(cancellationToken);

        Titles.Clear();
        foreach (var title in titles
            .OrderBy(title => title.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(title => title.Id.ToString(), StringComparer.Ordinal))
        {
            Titles.Add(new TitleViewModel(title));
        }

        Log.Write(
            LogLevel.Information,
            nameof(LibraryViewModel),
            string.Create(CultureInfo.InvariantCulture, $"Listed {Titles.Count} titles."));
    }

    /// <summary>Opens the Title details page of a title.</summary>
    /// <param name="title">The title to open.</param>
    /// <param name="cancellationToken">Cancels the page's load.</param>
    /// <returns>A task that completes when the page is shown.</returns>
    [RelayCommand]
    private Task OpenTitleAsync(TitleViewModel title, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(title);
        return _navigator.PushAsync(new TitleDetailsViewModel(title, Log), cancellationToken);
    }
}
