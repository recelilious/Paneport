// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Paneport.UI.Views;

namespace Paneport.UI.Tests;

// Shows the shell the way the desktop head's composition root does, over the test fakes: the
// shell view model, the main view in a window, and the start once the window has opened.
internal sealed class ShellHarness : IDisposable
{
    private ShellHarness(FakeServices fakes, double width, double height)
    {
        Fakes = fakes;
        Shell = new ShellViewModel(fakes.ToAppServices());
        View = new MainView { DataContext = Shell };
        Window = new Window { Width = width, Height = height, Content = View };
        Window.Opened += (_, _) => Shell.StartCommand.Execute(null);
        Window.Show();
        Settle();
    }

    public FakeServices Fakes { get; }

    public ShellViewModel Shell { get; }

    public MainView View { get; }

    public Window Window { get; }

    // The default size is the desktop head's window; a phone held upright is about 390 by 844.
    public static ShellHarness Show(FakeServices? fakes = null, double width = 1024, double height = 720) =>
        new(fakes ?? new FakeServices(), width, height);

    // The page view that the shell shows now.
    public T Page<T>()
        where T : Control =>
        View.GetVisualDescendants().OfType<T>().Single();

    // The text of every visible text block below a control, in visual order.
    public static IReadOnlyList<string> TextsIn(Visual root) =>
        root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Text is not null)
            .Select(text => text.Text!)
            .ToList();

    // Clicks the middle of a control with the mouse, as a user would.
    public void Click(Control control)
    {
        var middle = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The control is not in the window.");
        Window.MouseDown(middle, MouseButton.Left);
        Window.MouseUp(middle, MouseButton.Left);
        Settle();
    }

    // The navigation bar's buttons, in order.
    public IReadOnlyList<Button> NavigationButtons()
    {
        var navigation = View.FindControl<ItemsControl>("Navigation")
            ?? throw new InvalidOperationException("The main view has no navigation bar.");
        return navigation.GetVisualDescendants().OfType<Button>().ToList();
    }

    // Opens a section through the navigation bar, as a user would.
    public void OpenSection(ShellSection section)
    {
        Click(NavigationButtons().Single(button => Equals(button.Content, section.ToString())));
    }

    // Runs what the dispatcher has queued and lays out the window, so that the view matches the
    // view models.
    public void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        Window.Close();
    }
}
