// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CoreAnimation;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Paneport.IOS;

// The game view: a full-screen view backed by a CAMetalLayer, under the Avalonia view of the shell
// (avalonia_ui_layer.md sections 2.1 and 3.1). It stays opaque and idle: nothing draws into it
// until the graphics layers do (M2.1, M5.1), which receive the layer through pp_display_attach
// (C-0005 section 2).
internal sealed class GameView : UIView
{
    public GameView()
    {
        Opaque = true;
        BackgroundColor = UIColor.Black;
        Layer.Opaque = true;
    }

    // UIKit asks the class for its layer's class, so the view's layer is a CAMetalLayer.
    [Export("layerClass")]
    public static Class LayerClass() => new(typeof(CAMetalLayer));

    public CAMetalLayer MetalLayer => (CAMetalLayer)Layer;
}
