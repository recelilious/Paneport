// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;

namespace Paneport.UI.Views;

/// <summary>
/// The Diagnostics page: the host implementation, its interface version and its memory figures.
/// </summary>
/// <remarks>Its data context is a <see cref="Core.ViewModels.DiagnosticsViewModel"/>.</remarks>
public sealed partial class DiagnosticsPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public DiagnosticsPage()
    {
        InitializeComponent();
    }
}
