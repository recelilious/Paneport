// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using Paneport.Core.Storage;

namespace Paneport.IOS;

// The backup mark of C-0004 section 4: a folder's NSURLIsExcludedFromBackupKey resource value.
// iOS treats it as guidance. A failure carries no path in its message, because the caller logs
// the exception's type only.
internal sealed class BackupMarks : IBackupMarks
{
    public void SetExcluded(string path)
    {
        using var url = NSUrl.FromFilename(path);
        using var value = NSNumber.FromBoolean(true);
        if (!url.SetResource(NSUrl.IsExcludedFromBackupKey, value, out var error))
        {
            throw new IOException($"iOS refused the backup mark, with error {error?.Code ?? 0}.");
        }
    }

    public bool? IsExcluded(string path)
    {
        using var url = NSUrl.FromFilename(path);
        return url.TryGetResource(NSUrl.IsExcludedFromBackupKey, out var value, out _) && value is NSNumber number
            ? number.BoolValue
            : null;
    }
}
