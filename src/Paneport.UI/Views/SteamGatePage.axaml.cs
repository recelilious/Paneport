// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;

namespace Paneport.UI.Views;

/// <summary>
/// The Steam gate page of L2 steps 16 to 28 (S2.3.9): sign-in by approval, by code and by QR code,
/// and the steps from licenses to verified chunks, with a summary for the evidence record.
/// </summary>
/// <remarks>Its data context is a <see cref="Core.ViewModels.SteamGateViewModel"/>.</remarks>
public sealed partial class SteamGatePage : UserControl
{
    /// <summary>Creates the page.</summary>
    public SteamGatePage()
    {
        InitializeComponent();
    }
}
