// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;

namespace Paneport.DeviceTest;

// A developer page: a description, a button per action, and the result of the last action. An
// action that fails shows its exception's type and message, which on these pages come from the
// host or the keychain and hold no path or secret. Made with the CommunityToolkit.Mvvm
// generators, as every view model is (C-0010 section 2).
internal sealed partial class DeveloperToolViewModel : PageViewModel
{
    public DeveloperToolViewModel(string header, string description, IReadOnlyList<DeveloperAction> actions, ILog log)
        : base(log)
    {
        Header = header;
        Description = description;
        Actions = actions;
    }

    public override string Header { get; }

    public string Description { get; }

    public IReadOnlyList<DeveloperAction> Actions { get; }

    [ObservableProperty]
    public partial string Result { get; private set; } = "No result yet.";

    [RelayCommand]
    private async Task RunAsync(DeveloperAction? action, CancellationToken cancellationToken)
    {
        if (action is null)
        {
            return;
        }

        try
        {
            Result = await action.Run(cancellationToken);
            Log.Write(LogLevel.Information, Header, $"{action.Label}: done.");
        }
        catch (Exception exception)
        {
            Result = $"FAIL: {exception.GetType().Name}: {exception.Message}";
            Log.Write(LogLevel.Warning, Header, $"{action.Label} failed with {exception.GetType().Name}.");
        }
    }
}
