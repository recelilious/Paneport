// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;

namespace Paneport.UI.Views;

/// <summary>
/// The Library page, which lists the titles. Choosing a title opens its Title details page.
/// </summary>
/// <remarks>Its data context is a <see cref="Core.ViewModels.LibraryViewModel"/>.</remarks>
public sealed partial class LibraryPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public LibraryPage()
    {
        InitializeComponent();
    }
}
