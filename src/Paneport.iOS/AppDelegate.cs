// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using UIKit;

namespace Paneport.IOS;

// The application delegate. It composes the app at launch, on the main thread, before any scene
// connects: the storage folders, the log, the previous run's crash record, Avalonia's platform
// and the native host (ios-app-head.md section 3). An app built with Xcode 27 must use the scene
// life cycle, so the window belongs to SceneDelegate, which Info.plist names.
[Register("PaneportAppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    // The composed app, which the scene delegate shows. Set once, on the main thread.
    public static AppComposition? Composition { get; private set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Composition = CompositionRoot.Compose();
        return true;
    }
}
