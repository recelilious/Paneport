// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Threading;
using Paneport.Core.Hosting;

namespace Paneport.Desktop;

// Runs the services' updates on Avalonia's UI thread, where the view models live, as the iOS head's
// dispatcher does.
internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
