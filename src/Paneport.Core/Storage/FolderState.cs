// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>What the bootstrap found for one folder of the storage layout.</summary>
/// <param name="Folder">The folder.</param>
/// <param name="Exists">Whether the folder exists.</param>
/// <param name="ExcludedFromBackup">
/// The folder's backup mark: true when marked, false when not, null when it cannot be read.
/// </param>
public sealed record FolderState(StorageFolder Folder, bool Exists, bool? ExcludedFromBackup);
