// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Threading;
using Paneport.Core.Hosting;

namespace Paneport.IOS;

// Runs work on the UI thread, the main thread on iOS (C-0004 section 1), through Avalonia's
// dispatcher, which posts without waiting, so the host event thread never waits for the UI.
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
