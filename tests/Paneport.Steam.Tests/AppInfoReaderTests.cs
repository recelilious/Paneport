// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Apps;
using Paneport.Steam.Tests.Fakes;
using Xunit;
using static Paneport.Steam.Tests.Fakes.SyntheticContent;

namespace Paneport.Steam.Tests;

// S2.3.4, on synthetic data: the Windows depots and their sizes are found, and missing sections
// are reported, not thrown (steam-service.md section 6).
public sealed class AppInfoReaderTests
{
    [Fact]
    public void FindsTheWindowsDepotsWithTheirSizes()
    {
        var info = AppInfoReader.Read(AppId, AppInfo());

        Assert.Equal("Lantern Test App", info.Name);
        Assert.Equal([WindowsDepot, OlderWindowsDepot], info.WindowsDepots.Select(depot => depot.DepotId));
        var windows = info.WindowsDepots[0];
        Assert.Equal("Lantern Test App Content", windows.Name);
        Assert.Equal(1_048_576UL, windows.Size);
        Assert.Equal("64", windows.Architecture);
        Assert.True(windows.HasPublicManifest);
        var older = info.WindowsDepots[1];
        Assert.Null(older.Name);
        Assert.Equal(2048UL, older.Size);
        Assert.Null(older.Architecture);
        Assert.True(older.HasPublicManifest);
        Assert.Empty(info.Problems);
    }

    [Fact]
    public void ReadsTheManifestIdInBothForms()
    {
        var depots = AppInfo()["depots"]!;

        Assert.Equal(WindowsManifest, AppInfoReader.ReadManifestId(depots["4100000111"]!["manifests"]!["public"]));
        Assert.Equal(OlderManifest, AppInfoReader.ReadManifestId(depots["4100000113"]!["manifests"]!["public"]));
        Assert.Null(AppInfoReader.ReadManifestId(null));
    }

    [Fact]
    public void ReportsMissingInformation()
    {
        var info = AppInfoReader.Read(AppId, null);

        Assert.Null(info.Name);
        Assert.Empty(info.WindowsDepots);
        Assert.Equal(["Steam returned no information for the app."], info.Problems);
    }

    [Fact]
    public void ReportsAMissingCommonSection()
    {
        var info = AppInfoReader.Read(AppId, Section("appinfo", AppInfo()["depots"]!));

        Assert.Null(info.Name);
        Assert.Contains("The app's information has no common section.", info.Problems);
        Assert.Equal(2, info.WindowsDepots.Count);
    }

    [Fact]
    public void ReportsAMissingDepotsSection()
    {
        var info = AppInfoReader.Read(AppId, Section("appinfo", Section("common", Value("name", "Lantern Test App"))));

        Assert.Equal("Lantern Test App", info.Name);
        Assert.Empty(info.WindowsDepots);
        Assert.Equal(["The app's information has no depots section."], info.Problems);
    }

    [Fact]
    public void ReportsADepotWithoutAManifestOrASize()
    {
        var info = AppInfoReader.Read(
            AppId,
            Section(
                "appinfo",
                Section("common", Value("name", "Lantern Test App")),
                Section("depots", Section("4100000111", Section("config", Value("oslist", "linux,windows"))))));

        var depot = Assert.Single(info.WindowsDepots);
        Assert.False(depot.HasPublicManifest);
        Assert.Null(depot.Size);
        Assert.Equal(["Depot 4100000111 names no public manifest.", "Depot 4100000111 gives no size."], info.Problems);
    }

    [Fact]
    public void ReportsAnAppWithoutAWindowsDepot()
    {
        var info = AppInfoReader.Read(
            AppId,
            Section(
                "appinfo",
                Section("common", Value("name", "Lantern Test App")),
                Section("depots", Section("4100000112", Section("config", Value("oslist", "macos"))))));

        Assert.Empty(info.WindowsDepots);
        Assert.Equal(["The app's information lists no Windows depot."], info.Problems);
    }

    [Fact]
    public void IgnoresValuesThatAreNoNumbers()
    {
        var info = AppInfoReader.Read(
            AppId,
            Section(
                "appinfo",
                Section("common", Value("name", " ")),
                Section(
                    "depots",
                    Section("4100000111", Value("maxsize", "-5"), Section("manifests", Section("public", Value("gid", "x")))))));

        Assert.Null(info.Name);
        var depot = Assert.Single(info.WindowsDepots);
        Assert.Null(depot.Size);
        Assert.False(depot.HasPublicManifest);
        Assert.Contains("The app's information names no app name.", info.Problems);
    }
}
