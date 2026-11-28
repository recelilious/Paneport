// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Prefixes;
using Xunit;

namespace Paneport.Core.Tests.Prefixes;

// S4.4.6: the prefix installer against real temporary folders. Each test ends by comparing the
// whole tree of the prefix with the tree that the case should leave.
public sealed class PrefixInstallerTests
{
    private const string Windows = PrefixWorkspace.Windows;

    private static readonly Dictionary<string, string> s_version1 = new()
    {
        ["drive_c/windows/system32/a.dll"] = "a1",
        ["drive_c/windows/system32/old.dll"] = "old",
        ["drive_c/windows/win.ini"] = "ini1",
        ["drive_c/users/paneport/Desktop/readme.txt"] = "template",
        ["system.reg"] = "system1",
        ["user.reg"] = "user1",
        ["dosdevices/placeholder"] = string.Empty,
    };

    private static readonly Dictionary<string, string> s_version2 = new()
    {
        ["drive_c/windows/system32/a.dll"] = "a2",
        ["drive_c/windows/system32/new.dll"] = "new",
        ["drive_c/windows/win.ini"] = "ini2",
        ["drive_c/users/paneport/Desktop/readme.txt"] = "template2",
        ["system.reg"] = "system2",
        ["user.reg"] = "user2",
        ["dosdevices/placeholder"] = string.Empty,
    };

    [Fact]
    public async Task AFirstInstallCopiesTheTemplateAndMarksThePrefixesFolder()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Installed, result.Outcome);
        Assert.Equal(1, result.InstalledVersion);
        Assert.Equal("Library/Application Support/Paneport/prefixes/shared", result.PrefixFolder);
        Assert.Equal(PrefixWorkspace.Tree(workspace.Template), PrefixWorkspace.Tree(workspace.Prefix));
        Assert.Equal([PrefixNames.Shared], Directory.EnumerateDirectories(workspace.PrefixesFolder).Select(Path.GetFileName));
        Assert.Equal(1, Assert.Single(workspace.Marks.Marks).Value);
        Assert.DoesNotContain(workspace.Log.Lines, line => line.Message.Contains(workspace.Container, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(workspace.Log.Lines, line => line.Message.Contains(workspace.Container.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ACurrentInstallIsLeftAloneAndCopiesNothing()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        await workspace.Install();
        PrefixWorkspace.Write(workspace.Prefix, "drive_c/users/paneport/save.dat", "progress");
        File.WriteAllText(Path.Combine(workspace.Prefix, "user.reg"), "edited");
        var before = PrefixWorkspace.Tree(workspace.Prefix);
        var files = new FlakyPrefixFiles();

        var result = await workspace.Install(files);

        Assert.Equal(PrefixInstallOutcome.AlreadyCurrent, result.Outcome);
        Assert.Equal(0, files.Copies);
        Assert.Equal(before, PrefixWorkspace.Tree(workspace.Prefix));
    }

    [Fact]
    public async Task ALeftoverStagingFolderIsDeletedAndTheCopyRestarts()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        var staging = Path.Combine(workspace.PrefixesFolder, ".staging-shared");
        PrefixWorkspace.Write(staging, "stale/file.txt", "left by an earlier run");
        PrefixWorkspace.Write(staging, "drive_c/windows/system32/a.dll", "partial");

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Installed, result.Outcome);
        Assert.False(Directory.Exists(staging));
        Assert.Equal(PrefixWorkspace.Tree(workspace.Template), PrefixWorkspace.Tree(workspace.Prefix));
    }

    [Fact]
    public async Task ACopyThatStopsHalfWayLeavesNoPrefixAndTheNextRunCompletesIt()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);

        var failed = await workspace.Install(new FlakyPrefixFiles(failAfterCopies: 3));

        Assert.Equal(PrefixInstallOutcome.Failed, failed.Outcome);
        Assert.Equal("IOException", failed.FailureReason);
        Assert.False(failed.PrefixExists);
        Assert.False(Directory.Exists(workspace.Prefix));
        var staging = Path.Combine(workspace.PrefixesFolder, ".staging-shared");
        Assert.True(Directory.Exists(staging));
        Assert.False(File.Exists(Path.Combine(staging, TemplateMarker.FileName)));

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Installed, result.Outcome);
        Assert.False(Directory.Exists(staging));
        Assert.Equal(PrefixWorkspace.Tree(workspace.Template), PrefixWorkspace.Tree(workspace.Prefix));
    }

    [Fact]
    public async Task ANewerTemplateReplacesTheNamedFoldersAndKeepsTheRegistryAndTheUsersFiles()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        await workspace.Install();
        PrefixWorkspace.Write(workspace.Prefix, "drive_c/users/paneport/save.dat", "progress");
        PrefixWorkspace.Write(workspace.Prefix, "drive_c/Program Files/game/game.exe", "game");
        File.WriteAllText(Path.Combine(workspace.Prefix, "user.reg"), "edited user");
        File.WriteAllText(Path.Combine(workspace.Prefix, "system.reg"), "edited system");
        workspace.MakeTemplate(2, s_version2, Windows);

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Updated, result.Outcome);
        Assert.Equal(2, result.InstalledVersion);
        Assert.Equal(
            [
                "dosdevices/",
                "dosdevices/placeholder=",
                "drive_c/",
                "drive_c/Program Files/",
                "drive_c/Program Files/game/",
                "drive_c/Program Files/game/game.exe=game",
                "drive_c/users/",
                "drive_c/users/paneport/",
                "drive_c/users/paneport/Desktop/",
                "drive_c/users/paneport/Desktop/readme.txt=template",
                "drive_c/users/paneport/save.dat=progress",
                "drive_c/windows/",
                "drive_c/windows/system32/",
                "drive_c/windows/system32/a.dll=a2",
                "drive_c/windows/system32/new.dll=new",
                "drive_c/windows/win.ini=ini2",
                "paneport-template.json=" + File.ReadAllText(Path.Combine(workspace.Template, TemplateMarker.FileName)),
                "system.reg=edited system",
                "user.reg=edited user",
            ],
            PrefixWorkspace.Tree(workspace.Prefix));
        Assert.Equal(2, Assert.Single(workspace.Marks.Marks).Value);
    }

    [Fact]
    public async Task AnUpdateThatStopsBeforeTheMarkerIsRewrittenCompletesOnTheNextRun()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        await workspace.Install();
        PrefixWorkspace.Write(workspace.Prefix, "drive_c/users/paneport/save.dat", "progress");
        workspace.MakeTemplate(2, s_version2, Windows);

        var failed = await workspace.Install(new FlakyPrefixFiles(failAfterCopies: 2));

        Assert.Equal(PrefixInstallOutcome.Failed, failed.Outcome);
        Assert.True(TemplateMarker.TryParse(File.ReadAllBytes(Path.Combine(workspace.Prefix, TemplateMarker.FileName)), out var stillOld, out _));
        Assert.Equal(1, stillOld!.Version);

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Updated, result.Outcome);
        var expected = PrefixWorkspace.Tree(workspace.Template);
        var actual = PrefixWorkspace.Tree(workspace.Prefix);
        Assert.Equal(expected.Where(entry => entry.StartsWith(Windows, StringComparison.Ordinal)), actual.Where(entry => entry.StartsWith(Windows, StringComparison.Ordinal)));
        Assert.Contains("drive_c/users/paneport/save.dat=progress", actual);
        Assert.Contains(expected.Single(entry => entry.StartsWith("paneport-template.json", StringComparison.Ordinal)), actual);
        Assert.DoesNotContain(actual, entry => entry.Contains(".paneport-tmp", StringComparison.Ordinal));
        Assert.DoesNotContain(actual, entry => entry.Contains("old.dll", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOlderBundledTemplateLeavesThePrefixAloneAndWarns()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(2, s_version2, Windows);
        await workspace.Install();
        var before = PrefixWorkspace.Tree(workspace.Prefix);
        workspace.MakeTemplate(1, s_version1, Windows);

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.TemplateOlder, result.Outcome);
        Assert.Equal(2, result.InstalledVersion);
        Assert.Equal(before, PrefixWorkspace.Tree(workspace.Prefix));
        Assert.Contains(workspace.Log.Lines, line => line.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APrefixWithoutAValidMarkerIsLeftAloneAndReported(bool unreadableMarker)
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        PrefixWorkspace.Write(workspace.Prefix, "system.reg", "made by hand");
        PrefixWorkspace.Write(workspace.Prefix, "drive_c/windows/win.ini", "mine");
        if (unreadableMarker)
        {
            PrefixWorkspace.Write(workspace.Prefix, TemplateMarker.FileName, "not json");
        }

        var before = PrefixWorkspace.Tree(workspace.Prefix);

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Unmanaged, result.Outcome);
        Assert.Null(result.InstalledVersion);
        Assert.True(result.PrefixExists);
        Assert.Equal(before, PrefixWorkspace.Tree(workspace.Prefix));
        Assert.Contains(workspace.Log.Lines, line => line.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task ABundleWithoutATemplateDoesNothing()
    {
        using var workspace = new PrefixWorkspace();

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.NoTemplate, result.Outcome);
        Assert.False(result.PrefixExists);
        Assert.False(Directory.Exists(workspace.PrefixesFolder));
        Assert.Empty(workspace.Marks.Marks);
    }

    [Fact]
    public async Task ATemplateWithAnInvalidMarkerFailsWithoutCopying()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        PrefixWorkspace.Write(workspace.Template, TemplateMarker.FileName, "{ \"schema\": 2 }");

        var result = await workspace.Install();

        Assert.Equal(PrefixInstallOutcome.Failed, result.Outcome);
        Assert.False(Directory.Exists(workspace.Prefix));
    }

    [Fact]
    public async Task TwoCallsAtOnceInstallOnceAndTheSecondFindsThePrefixCurrent()
    {
        using var workspace = new PrefixWorkspace();
        workspace.MakeTemplate(1, s_version1, Windows);
        var installer = workspace.Installer();

        var first = installer.InstallAsync(PrefixNames.Shared, CancellationToken.None);
        var second = installer.InstallAsync(PrefixNames.Shared, CancellationToken.None);

        var outcomes = (await Task.WhenAll(first, second)).Select(result => result.Outcome).Order().ToList();
        Assert.Equal([PrefixInstallOutcome.Installed, PrefixInstallOutcome.AlreadyCurrent], outcomes.Order().ToList());
        Assert.Equal(PrefixWorkspace.Tree(workspace.Template), PrefixWorkspace.Tree(workspace.Prefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../shared")]
    [InlineData("a/b")]
    [InlineData(".staging-x")]
    public async Task ANameThatCouldLeaveThePrefixesFolderIsRefused(string name)
    {
        using var workspace = new PrefixWorkspace();
        var installer = workspace.Installer();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => installer.InstallAsync(name, CancellationToken.None));
    }
}
