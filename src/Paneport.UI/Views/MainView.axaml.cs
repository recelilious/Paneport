// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;

namespace Paneport.UI.Views;

/// <summary>
/// The shell's view: the navigation, the back button and heading, and the page shown. Each head
/// hosts it as its main view, in a window on the desktop and as the single main view on iOS.
/// </summary>
/// <remarks>Its data context is a <see cref="Core.ViewModels.ShellViewModel"/>.</remarks>
public sealed partial class MainView : UserControl
{
    /// <summary>Creates the view.</summary>
    public MainView()
    {
        InitializeComponent();
    }
}
