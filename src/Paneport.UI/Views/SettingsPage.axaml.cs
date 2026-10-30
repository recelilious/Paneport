// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;

namespace Paneport.UI.Views;

/// <summary>
/// The Settings page: the locale for game sessions, and the Steam sign-in state.
/// </summary>
/// <remarks>Its data context is a <see cref="Core.ViewModels.SettingsViewModel"/>.</remarks>
public sealed partial class SettingsPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public SettingsPage()
    {
        InitializeComponent();
    }
}
