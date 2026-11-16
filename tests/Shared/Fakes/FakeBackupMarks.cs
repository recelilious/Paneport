// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Storage;

namespace Paneport.Tests.Fakes;

// Backup marks in memory, counting how often each folder was marked.
internal sealed class FakeBackupMarks : IBackupMarks
{
    private readonly Dictionary<string, int> _marks = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> Marks => _marks;

    public void SetExcluded(string path)
    {
        var normalized = path.Replace('\\', '/');
        _marks[normalized] = _marks.GetValueOrDefault(normalized) + 1;
    }

    public bool? IsExcluded(string path) => _marks.ContainsKey(path.Replace('\\', '/'));
}
