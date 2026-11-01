// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using UIKit;

namespace Paneport.IOS;

// The application delegate. An app built with Xcode 27 must use the scene life cycle, so the
// window belongs to SceneDelegate, which Info.plist names.
[Register("PaneportAppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        return true;
    }
}
