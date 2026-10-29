// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Settings;

namespace Paneport.Tests.Fakes;

// A settings store in memory that counts saves, and fails them when a test sets a failure.
internal sealed class FakeSettingsStore : ISettingsStore
{
    public AppSettings? Saved { get; set; }

    public int SaveCount { get; private set; }

    public Exception? SaveFailure { get; set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Saved ?? AppSettings.Default);
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SaveFailure is not null)
        {
            return Task.FromException(SaveFailure);
        }

        Saved = settings;
        SaveCount++;
        return Task.CompletedTask;
    }
}
