// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Compat;
using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// S6.4.3: the launch rule of C-0008 section 5 for Steam and local titles.
public sealed class LaunchRuleTests
{
    private static readonly bool?[] s_flags = [null, false, true];

    private static readonly CegStatus?[] s_cegValues = [null, CegStatus.Unknown, CegStatus.Yes, CegStatus.No];

    [Theory]
    [InlineData(false, false, CegStatus.No, LaunchDecision.Direct, LaunchDecision.Direct)]
    [InlineData(true, false, CegStatus.No, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(false, true, CegStatus.No, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(false, false, CegStatus.Yes, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(false, false, CegStatus.Unknown, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(false, false, null, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(null, false, CegStatus.No, LaunchDecision.Unclassified, LaunchDecision.Unclassified)]
    [InlineData(false, null, CegStatus.No, LaunchDecision.Unclassified, LaunchDecision.Unclassified)]
    [InlineData(null, null, null, LaunchDecision.Unclassified, LaunchDecision.Unclassified)]
    [InlineData(true, null, null, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(null, true, null, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(null, null, CegStatus.Unknown, LaunchDecision.Dock, LaunchDecision.Refused)]
    [InlineData(null, null, CegStatus.Yes, LaunchDecision.Dock, LaunchDecision.Refused)]
    public void ClassificationDecidesThePath(
        bool? steamApi, bool? steamStub, CegStatus? ceg, LaunchDecision steam, LaunchDecision local)
    {
        var drm = new DrmClassification { SteamApi = steamApi, SteamStub = steamStub, Ceg = ceg };

        Assert.Equal(steam, LaunchRule.Decide(TitleSource.Steam, drm));
        Assert.Equal(local, LaunchRule.Decide(TitleSource.Local, drm));
    }

    [Fact]
    public void DirectOnlyWhenSteamApiAndSteamStubAreFalseAndCegIsNo()
    {
        // Over every combination: the Direct path exactly when C-0008 section 5 allows it, never the
        // Dock path for a local title, and never a refusal for a Steam title.
        foreach (var steamApi in s_flags)
        {
            foreach (var steamStub in s_flags)
            {
                foreach (var ceg in s_cegValues)
                {
                    var drm = new DrmClassification { SteamApi = steamApi, SteamStub = steamStub, Ceg = ceg };
                    var allowed = steamApi == false && steamStub == false && ceg == CegStatus.No;
                    var steam = LaunchRule.Decide(TitleSource.Steam, drm);
                    var local = LaunchRule.Decide(TitleSource.Local, drm);

                    Assert.Equal(allowed, steam == LaunchDecision.Direct);
                    Assert.Equal(allowed, local == LaunchDecision.Direct);
                    Assert.NotEqual(LaunchDecision.Refused, steam);
                    Assert.NotEqual(LaunchDecision.Dock, local);
                }
            }
        }
    }

    [Theory]
    [InlineData(TitleSource.Steam)]
    [InlineData(TitleSource.Local)]
    public void TitleWithoutAClassificationIsUnclassified(TitleSource source)
    {
        Assert.Equal(LaunchDecision.Unclassified, LaunchRule.Decide(source, null));
        Assert.Equal(LaunchDecision.Unclassified, LaunchRule.Decide(source, new DrmClassification()));
    }

    [Fact]
    public void SteamAppIdFileDoesNotDecideThePath()
    {
        var withFile = new DrmClassification
        {
            SteamApi = false,
            SteamStub = false,
            SteamAppIdFile = true,
            Ceg = CegStatus.No,
        };

        Assert.Equal(LaunchDecision.Direct, LaunchRule.Decide(TitleSource.Steam, withFile));
        Assert.Equal(LaunchDecision.Direct, LaunchRule.Decide(TitleSource.Local, withFile));
    }

    [Fact]
    public void UndefinedSourceIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LaunchRule.Decide((TitleSource)42, null));
    }
}
