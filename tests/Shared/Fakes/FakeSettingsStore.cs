// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Settings;

namespace Paneport.Tests.Fakes;

// A settings store in memory that counts saves, and fails them when a test sets a failure. A
// test can hold a load or a save open with a gate, as a store on disk would take its time; a
// held load or save honors its cancellation token.
internal sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings? Saved { get; set; }

    public int SaveCount { get; private set; }

    public Exception? SaveFailure { get; set; }

    public TaskCompletionSource? LoadGate { get; set; }

    public TaskCompletionSource? SaveGate { get; set; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (LoadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Saved ?? AppSettings.Default;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (SaveGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (SaveFailure is not null)
        {
            throw SaveFailure;
        }

        Saved = settings;
        SaveCount++;
    }
}
