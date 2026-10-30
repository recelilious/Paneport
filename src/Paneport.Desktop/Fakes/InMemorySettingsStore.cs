// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Settings;

namespace Paneport.Desktop.Fakes;

// Keeps the settings for one run of the desktop head. Saving them to disk comes with a later stage.
internal sealed class InMemorySettingsStore : ISettingsStore
{
    // Settings are immutable records, so swapping the reference is the whole save.
    private AppSettings _settings = AppSettings.Default;

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _settings));
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _settings, settings);
        return Task.CompletedTask;
    }
}
