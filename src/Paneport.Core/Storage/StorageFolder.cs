// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>A folder of the on-device storage layout (C-0004 section 4).</summary>
/// <param name="RelativePath">The path inside the app container, with forward slashes, such as <c>Library/Caches/Paneport</c>.</param>
/// <param name="Holds">What the folder holds, as C-0004 section 4 says.</param>
/// <param name="Backup">Whether the folder goes into backups.</param>
/// <param name="ExcludedBySystem">True for a folder below <c>Library/Caches</c>, which iOS itself never backs up.</param>
public sealed record StorageFolder(string RelativePath, string Holds, BackupPolicy Backup, bool ExcludedBySystem);
