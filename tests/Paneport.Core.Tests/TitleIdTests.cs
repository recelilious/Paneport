// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests;

// C-0008 section 1: a Steam title is steam-<appid>, any other title local-<slug>, and a slug is
// lowercase letters, digits and single hyphens.
public sealed class TitleIdTests
{
    [Fact]
    public void SteamIdentityIsTheCompatFileNameStem()
    {
        var id = TitleId.Steam(480);

        Assert.Equal(TitleSource.Steam, id.Source);
        Assert.Equal(480u, id.SteamAppId);
        Assert.Null(id.Slug);
        Assert.Equal("steam-480", id.ToString());
    }

    [Fact]
    public void SteamAppIdZeroIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TitleId.Steam(0));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("7")]
    [InlineData("pocket-orrery")]
    [InlineData("2d-platformer-3")]
    public void LocalIdentityKeepsAValidSlug(string slug)
    {
        var id = TitleId.Local(slug);

        Assert.True(TitleId.IsValidSlug(slug));
        Assert.Equal(TitleSource.Local, id.Source);
        Assert.Null(id.SteamAppId);
        Assert.Equal(slug, id.Slug);
        Assert.Equal("local-" + slug, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("double--hyphen")]
    [InlineData("Upper")]
    [InlineData("under_score")]
    [InlineData("with space")]
    [InlineData("dot.ted")]
    [InlineData("café")]
    public void BadSlugsAreRejected(string slug)
    {
        Assert.False(TitleId.IsValidSlug(slug));
        Assert.Throws<ArgumentException>(() => TitleId.Local(slug));
    }

    [Fact]
    public void NullIsNoSlug()
    {
        Assert.False(TitleId.IsValidSlug(null));
    }

    [Theory]
    [InlineData("steam-480")]
    [InlineData("steam-4294967295")]
    [InlineData("local-pocket-orrery")]
    public void IdentityTextRoundTrips(string text)
    {
        Assert.True(TitleId.TryParse(text, out var id));
        Assert.Equal(text, id.ToString());
        Assert.Equal(id, TitleId.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("480")]
    [InlineData("steam-")]
    [InlineData("steam-0")]
    [InlineData("steam-0480")]
    [InlineData("steam--1")]
    [InlineData("steam-+1")]
    [InlineData("steam- 1")]
    [InlineData("steam-12a")]
    [InlineData("steam-480\0")]
    [InlineData("steam-1\0\0")]
    [InlineData("steam-4294967296")]
    [InlineData("Steam-480")]
    [InlineData("local-")]
    [InlineData("local-Bad")]
    [InlineData("other-480")]
    public void TextThatIsNoIdentityIsRejected(string text)
    {
        Assert.False(TitleId.TryParse(text, out var id));
        Assert.Null(id);
        Assert.Throws<FormatException>(() => TitleId.Parse(text));
    }

    [Fact]
    public void NullIsNoIdentity()
    {
        Assert.False(TitleId.TryParse(null, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void IdentitiesCompareByValue()
    {
        Assert.Equal(TitleId.Steam(480), TitleId.Parse("steam-480"));
        Assert.Equal(TitleId.Local("a-b"), TitleId.Local("a-b"));
        Assert.NotEqual(TitleId.Steam(480), TitleId.Steam(481));
        Assert.NotEqual(TitleId.Local("480"), TitleId.Steam(480));
    }
}
