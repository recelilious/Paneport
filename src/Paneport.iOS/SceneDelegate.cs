// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using UIKit;

namespace Paneport.IOS;

// The delegate of the app's window scene, which Info.plist names by its Objective-C name. UIKit
// creates it, and it gives the scene a window that shows the root view controller over the app
// that AppDelegate composed. Avalonia's own scene delegate is never used (ios-app-head.md
// section 2).
[Register("PaneportSceneDelegate")]
internal sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene || AppDelegate.Composition is not { } composition)
        {
            return;
        }

        Window = new UIWindow(windowScene)
        {
            RootViewController = new RootViewController(composition),
        };
        Window.MakeKeyAndVisible();
    }
}
