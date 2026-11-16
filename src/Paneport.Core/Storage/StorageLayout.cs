// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>
/// The on-device storage layout of C-0004 section 4: every folder inside the app container, with
/// what it holds and whether it goes into backups. The keychain, the table's last row, is not a
/// folder; <see cref="Security.SecureTokenStore"/> uses it.
/// </summary>
public static class StorageLayout
{
    /// <summary>Gets the folder of the Wine prefixes, including the games' save files.</summary>
    public static StorageFolder Prefixes { get; } = new(
        "Library/Application Support/Paneport/prefixes",
        "Wine prefixes, including the games' save files",
        BackupPolicy.Excluded,
        ExcludedBySystem: false);

    /// <summary>Gets the folder of installed game files and Steam app manifests.</summary>
    public static StorageFolder Library { get; } = new(
        "Library/Application Support/Paneport/library",
        "Installed game files and Steam app manifests",
        BackupPolicy.Excluded,
        ExcludedBySystem: false);

    /// <summary>Gets the folder of downloads in progress.</summary>
    public static StorageFolder Downloads { get; } = new(
        "Library/Application Support/Paneport/downloads",
        "Downloads in progress",
        BackupPolicy.Excluded,
        ExcludedBySystem: false);

    /// <summary>Gets the folder of components downloaded on the device.</summary>
    public static StorageFolder Runtime { get; } = new(
        "Library/Application Support/Paneport/runtime",
        "Components downloaded on the device",
        BackupPolicy.Excluded,
        ExcludedBySystem: false);

    /// <summary>Gets the folder of the device's own compatibility layer (C-0008 section 1).</summary>
    public static StorageFolder Compat { get; } = new(
        "Library/Application Support/Paneport/compat",
        "The device's own compatibility layer",
        BackupPolicy.Included,
        ExcludedBySystem: false);

    /// <summary>Gets the folder of the app settings.</summary>
    public static StorageFolder Settings { get; } = new(
        "Library/Application Support/Paneport/settings",
        "App settings",
        BackupPolicy.Included,
        ExcludedBySystem: false);

    /// <summary>Gets the folder of shader caches, logs and crash records, which iOS may purge.</summary>
    public static StorageFolder Caches { get; } = new(
        "Library/Caches/Paneport",
        "Shader caches, logs and crash records that iOS may purge",
        BackupPolicy.Excluded,
        ExcludedBySystem: true);

    /// <summary>Gets the folder of the files that the user brings in through the Files app.</summary>
    public static StorageFolder Import { get; } = new(
        "Documents/Import",
        "Files the user brings in through the Files app",
        BackupPolicy.UserChoice,
        ExcludedBySystem: false);

    /// <summary>Gets every folder, in the order of C-0004 section 4.</summary>
    public static IReadOnlyList<StorageFolder> Folders { get; } =
        [Prefixes, Library, Downloads, Runtime, Compat, Settings, Caches, Import];

    /// <summary>Gets the full path of a folder inside a container.</summary>
    /// <param name="containerRoot">The app container's root folder.</param>
    /// <param name="folder">The folder.</param>
    /// <returns>The folder's full path, in the platform's form.</returns>
    public static string PathOf(string containerRoot, StorageFolder folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerRoot);
        ArgumentNullException.ThrowIfNull(folder);
        return Path.Combine([containerRoot, .. folder.RelativePath.Split('/')]);
    }
}
