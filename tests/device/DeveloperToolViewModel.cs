// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;

namespace Paneport.DeviceTest;

// A developer page: a description, a button per action, and the result of the last action. An
// action that fails shows its exception's type and message, which on these pages come from the
// host or the keychain and hold no path or secret.
internal sealed class DeveloperToolViewModel : PageViewModel
{
    private string _result = "No result yet.";

    public DeveloperToolViewModel(string header, string description, IReadOnlyList<DeveloperAction> actions, ILog log)
        : base(log)
    {
        Header = header;
        Description = description;
        Actions = actions;
        RunCommand = new AsyncRelayCommand<DeveloperAction>(RunAsync);
    }

    public override string Header { get; }

    public string Description { get; }

    public IReadOnlyList<DeveloperAction> Actions { get; }

    public IAsyncRelayCommand<DeveloperAction> RunCommand { get; }

    public string Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

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
