// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Paneport.UI.Tests;

public sealed class HeadlessApplicationTests
{
    [AvaloniaFact]
    public void ApplicationStartsAndShowsAWindow()
    {
        var window = new Window { Title = "Paneport" };

        window.Show();

        Assert.IsType<App>(Application.Current);
        Assert.True(window.IsVisible);
        window.Close();
    }
}
