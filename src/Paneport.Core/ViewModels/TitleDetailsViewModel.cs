// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The Title details page: everything the shell knows about one title.
/// </summary>
public sealed class TitleDetailsViewModel : PageViewModel
{
    /// <summary>Creates the page for a title.</summary>
    /// <param name="title">The title to show.</param>
    /// <param name="log">The log.</param>
    public TitleDetailsViewModel(TitleViewModel title, ILog log)
        : base(log)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    /// <summary>Gets the title.</summary>
    public TitleViewModel Title { get; }

    /// <summary>Gets the title's name, which heads the page.</summary>
    public override string Header => Title.Name;
}
