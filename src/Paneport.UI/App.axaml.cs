// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Markup.Xaml;

namespace Paneport.UI;

/// <summary>
/// The Paneport application, shared by the desktop and iOS heads.
/// </summary>
/// <remarks>
/// Each head creates its own application lifetime and its own top-level view, because the
/// desktop head shows a window and the iOS head shows a single main view.
/// </remarks>
public partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
