// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;

namespace Paneport.UI.Views;

/// <summary>
/// The Title details page: a title's name, identity, source, install state and launch path.
/// </summary>
/// <remarks>Its data context is a <see cref="Core.ViewModels.TitleDetailsViewModel"/>.</remarks>
public sealed partial class TitleDetailsPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public TitleDetailsPage()
    {
        InitializeComponent();
    }
}
