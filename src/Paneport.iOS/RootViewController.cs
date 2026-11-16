// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Paneport.Core.Logging;
using Paneport.UI.Views;
using UIKit;

namespace Paneport.IOS;

// The root view controller (ios-app-head.md section 2): a full-screen GameView with its idle
// CAMetalLayer, under one full-screen AvaloniaView that shows the shell of M0.3. Avalonia has no
// API for an app-supplied root view controller, and its maintainers advise hosting AvaloniaView
// in one's own (avalonia_ui_layer.md section 2.1). The shell's content keeps out of the safe area,
// which the controller tracks, while the views themselves fill the screen. When the native host
// did not start, or the shell's view cannot be built, a label shows the message instead of the
// shell, since a device without a Mac shows no console (D1).
internal sealed class RootViewController : UIViewController
{
    private readonly AppComposition _composition;
    private Border? _safeArea;
    private bool _shellViewFailed;

    public RootViewController(AppComposition composition)
    {
        _composition = composition;
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        if (View is not { } view)
        {
            return;
        }

        view.BackgroundColor = UIColor.Black;
        view.AddSubview(new GameView
        {
            Frame = view.Bounds,
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
        });

        if (_composition.Shell is not { } shell)
        {
            AddFailureMessage(view, _composition.FailureMessage ?? "Paneport cannot start.");
            return;
        }

        AvaloniaView? avalonia = null;
        try
        {
            _safeArea = new Border { Child = new MainView { DataContext = shell } };
            avalonia = new AvaloniaView
            {
                Frame = view.Bounds,
                AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
                Content = _safeArea,
            };
            view.AddSubview(avalonia);
        }
        catch (Exception exception)
        {
            // Building the views is the first use of Avalonia's platform without its delegates. A
            // failure shows on screen, as a failed composition does, and nothing starts. Only the
            // type is logged; the message can hold a path.
            avalonia?.RemoveFromSuperview();
            _safeArea = null;
            _shellViewFailed = true;
            var type = exception.GetType().Name;
            _composition.Log.Write(LogLevel.Error, nameof(RootViewController), $"Building the shell's view failed with {type}.");
            AddFailureMessage(view, $"Paneport could not start ({type}).");
        }
    }

    // The run starts once the shell's view is on screen, so that the first load runs on the UI
    // thread with Avalonia's synchronization context in place, and the memory timeline starts with
    // it. AppStart starts it once per process: a controller of a scene that iOS connected again
    // starts nothing.
    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (!_shellViewFailed)
        {
            _composition.Start?.TryStart();
        }
    }

    public override void ViewSafeAreaInsetsDidChange()
    {
        base.ViewSafeAreaInsetsDidChange();
        if (_safeArea is not null && View is { } view)
        {
            var insets = view.SafeAreaInsets;
            _safeArea.Padding = new Thickness(insets.Left, insets.Top, insets.Right, insets.Bottom);
        }
    }

    // The shell fills the screen, as a game's view does.
    public override bool PrefersStatusBarHidden() => true;

    private static void AddFailureMessage(UIView view, string message)
    {
        var label = new UILabel
        {
            Text = message,
            TextColor = UIColor.White,
            Lines = 0,
            TextAlignment = UITextAlignment.Center,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        view.AddSubview(label);
        NSLayoutConstraint.ActivateConstraints(
        [
            label.CenterYAnchor.ConstraintEqualTo(view.CenterYAnchor),
            label.LeadingAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.LeadingAnchor, 24),
            label.TrailingAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.TrailingAnchor, -24),
        ]);
    }
}
