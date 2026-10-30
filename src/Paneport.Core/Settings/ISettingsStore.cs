// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Settings;

/// <summary>
/// Keeps the app settings.
/// </summary>
/// <remarks>
/// The desktop head keeps them in memory for one run. Saving them to the settings folder of
/// C-0004 section 4 comes with a later stage, starting with the iOS head.
/// </remarks>
public interface ISettingsStore
{
    /// <summary>Loads the settings.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The saved settings, or <see cref="AppSettings.Default"/> when none were saved.</returns>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    /// <summary>Saves the settings, replacing the saved ones.</summary>
    /// <param name="settings">The settings to save.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes when the settings are saved.</returns>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
