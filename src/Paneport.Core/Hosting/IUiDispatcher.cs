// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// Runs work on the UI thread, where the view models live (C-0004 section 1). The iOS head
/// implements it with Avalonia's dispatcher; tests run the queued work when they choose.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues an action to run on the UI thread, and returns at once. Any thread.</summary>
    /// <param name="action">The action.</param>
    void Post(Action action);
}
