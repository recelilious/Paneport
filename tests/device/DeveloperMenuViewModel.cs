// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;

namespace Paneport.DeviceTest;

// The developer menu, the page of the Developer section: one entry per developer page, each of
// which opens on top of the menu.
internal sealed partial class DeveloperMenuViewModel : PageViewModel
{
    private readonly INavigator _navigator;

    public DeveloperMenuViewModel(INavigator navigator, IReadOnlyList<DeveloperEntry> entries, ILog log)
        : base(log)
    {
        _navigator = navigator;
        Entries = entries;
    }

    public override string Header => "Developer";

    public IReadOnlyList<DeveloperEntry> Entries { get; }

    [RelayCommand]
    private Task OpenAsync(DeveloperEntry? entry, CancellationToken cancellationToken) =>
        entry is null ? Task.CompletedTask : _navigator.PushAsync(entry.Create(), cancellationToken);
}
