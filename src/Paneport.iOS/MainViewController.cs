// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using UIKit;

namespace Paneport.IOS;

// An empty view controller that shows the app's name. M1.3 replaces it with the root view
// controller that hosts the shell.
internal sealed class MainViewController : UIViewController
{
    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        if (View is not { } view)
        {
            return;
        }

        view.BackgroundColor = UIColor.SystemBackground;
        var name = new UILabel
        {
            Text = "Paneport",
            TextColor = UIColor.Label,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        view.AddSubview(name);
        NSLayoutConstraint.ActivateConstraints(
        [
            name.CenterXAnchor.ConstraintEqualTo(view.CenterXAnchor),
            name.CenterYAnchor.ConstraintEqualTo(view.CenterYAnchor),
        ]);
    }
}
