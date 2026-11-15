// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Paneport.Core.Logging;
using Paneport.Core.Storage;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Storage;

// S1.3.4: the layout table against C-0004 section 4 itself, and the bootstrap over a fake file
// system, which creates each folder and marks exactly the folders that say "No".
public sealed partial class StorageBootstrapTests
{
    private const string Root = "/container";

    [Fact]
    public void TheLayoutListsEveryPathOfTheContractWithItsBackupMark()
    {
        var contract = ContractRows();

        Assert.Equal(contract.Count, StorageLayout.Folders.Count);
        foreach (var (path, inBackups) in contract)
        {
            var folder = Assert.Single(StorageLayout.Folders, folder => folder.RelativePath + "/" == path);
            var expected = inBackups switch
            {
                "No" => BackupPolicy.Excluded,
                "Yes" => BackupPolicy.Included,
                "User's choice" => BackupPolicy.UserChoice,
                _ => throw new InvalidDataException($"C-0004 names an unknown backup mark for {path}."),
            };
            Assert.Equal(expected, folder.Backup);
            Assert.Equal(path.StartsWith("Library/Caches/", StringComparison.Ordinal), folder.ExcludedBySystem);
        }
    }

    [Fact]
    public void TheBootstrapCreatesEveryFolderAndMarksExactlyTheExcludedOnes()
    {
        var files = new FakeFileSystem();
        var marks = new FakeBackupMarks();
        var bootstrap = new StorageBootstrap(Root, files, marks, new RecordingLog());

        var states = bootstrap.Run();

        Assert.Equal(
            StorageLayout.Folders.Select(folder => $"{Root}/{folder.RelativePath}").Order(StringComparer.Ordinal),
            files.Folders.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                $"{Root}/Library/Application Support/Paneport/downloads",
                $"{Root}/Library/Application Support/Paneport/library",
                $"{Root}/Library/Application Support/Paneport/prefixes",
                $"{Root}/Library/Application Support/Paneport/runtime",
                $"{Root}/Library/Caches/Paneport",
            ],
            marks.Marks.Keys.Order(StringComparer.Ordinal));
        Assert.All(marks.Marks.Values, count => Assert.Equal(1, count));
        Assert.All(states, state => Assert.True(state.Exists));
        Assert.All(states, state => Assert.Equal(state.Folder.Backup == BackupPolicy.Excluded, state.ExcludedFromBackup));
    }

    [Fact]
    public void AFolderThatCannotBeCreatedIsLoggedAndTheOthersAreSetUp()
    {
        var files = new FakeFileSystem();
        files.FoldersThatFail.Add($"{Root}/Library/Application Support/Paneport/library");
        var marks = new FakeBackupMarks();
        var log = new RecordingLog();

        var states = new StorageBootstrap(Root, files, marks, log).Run();

        Assert.Equal(StorageLayout.Folders.Count - 1, files.Folders.Count);
        Assert.False(Assert.Single(states, state => state.Folder == StorageLayout.Library).Exists);
        Assert.DoesNotContain($"{Root}/Library/Application Support/Paneport/library", marks.Marks.Keys);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("Creating Library/Application Support/Paneport/library failed with IOException.", line.Message);
    }

    [Fact]
    public void MarkAgainRenewsTheMarkOfAnExcludedFolderOnly()
    {
        var marks = new FakeBackupMarks();
        var bootstrap = new StorageBootstrap(Root, new FakeFileSystem(), marks, new RecordingLog());
        bootstrap.Run();

        bootstrap.MarkAgain(StorageLayout.Prefixes);
        bootstrap.MarkAgain(StorageLayout.Settings);
        bootstrap.MarkAgain(StorageLayout.Import);

        Assert.Equal(2, marks.Marks[$"{Root}/Library/Application Support/Paneport/prefixes"]);
        Assert.DoesNotContain($"{Root}/Library/Application Support/Paneport/settings", marks.Marks.Keys);
        Assert.DoesNotContain($"{Root}/Documents/Import", marks.Marks.Keys);
    }

    [Fact]
    public void InspectReportsAMissingFolderWithoutItsMark()
    {
        var bootstrap = new StorageBootstrap(Root, new FakeFileSystem(), new FakeBackupMarks(), new RecordingLog());

        var states = bootstrap.Inspect();

        Assert.All(states, state => Assert.False(state.Exists));
        Assert.All(states, state => Assert.Null(state.ExcludedFromBackup));
    }

    // The folders of C-0004 section 4 and their "In backups" column, from the contract's table.
    private static List<(string Path, string InBackups)> ContractRows()
    {
        var contract = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Repository", "docs", "contracts", "C-0004-architecture-and-layout.md"));
        var section = contract[contract.IndexOf("## 4. On-device storage layout", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n## ", 4, StringComparison.Ordinal)];
        return [.. section.Split('\n')
            .Select(line => FolderRow().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups["path"].Value, match.Groups["backup"].Value.Trim()))];
    }

    [GeneratedRegex(@"^\| `(?<path>[^`]+/)` \| [^|]+ \| (?<backup>[^|]+) \|$")]
    private static partial Regex FolderRow();
}
