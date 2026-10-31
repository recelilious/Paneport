// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using UIKit;

namespace Paneport.IOS;

// The delegate of the app's window scene, which Info.plist names by its Objective-C name. UIKit
// creates it, and it gives the scene a window that shows MainViewController.
[Register("PaneportSceneDelegate")]
internal sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
        {
            return;
        }

        Window = new UIWindow(windowScene)
        {
            RootViewController = new MainViewController(),
        };
        Window.MakeKeyAndVisible();
    }
}
