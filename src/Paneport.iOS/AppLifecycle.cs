// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.IOS;

// The app's return to the foreground, which the scene delegate reports. The Steam service logs on
// again then, because iOS may have closed the socket of the suspended app (steam-service.md
// section 4). Raised on the main thread.
internal sealed class AppLifecycle
{
    public event EventHandler? EnteredForeground;

    public void NotifyEnteredForeground() => EnteredForeground?.Invoke(this, EventArgs.Empty);
}
