// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.ViewModels;

/// <summary>
/// Lets a page open another page on top of itself. The shell implements it.
/// </summary>
public interface INavigator
{
    /// <summary>
    /// Shows a page on top of the current one and loads it. Going back returns to the current one.
    /// </summary>
    /// <param name="page">The page to show.</param>
    /// <param name="cancellationToken">Cancels the page's load.</param>
    /// <returns>
    /// A task that completes when the page is shown and loaded. A cancelled load ends it without
    /// an exception, as a newer navigation does.
    /// </returns>
    Task PushAsync(PageViewModel page, CancellationToken cancellationToken);
}
