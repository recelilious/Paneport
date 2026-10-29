// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.ViewModels;

namespace Paneport.Core.Tests;

// A navigator that records the pages it was asked to show, and loads them as the shell does.
internal sealed class FakeNavigator : INavigator
{
    public List<PageViewModel> Pushed { get; } = [];

    public Task PushAsync(PageViewModel page, CancellationToken cancellationToken)
    {
        Pushed.Add(page);
        return page.LoadAsync(cancellationToken);
    }
}
