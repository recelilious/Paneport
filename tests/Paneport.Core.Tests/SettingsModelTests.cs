// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Settings;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class SettingsModelTests
{
    [Fact]
    public void DefaultLocaleIsEnglishUnitedStates()
    {
        Assert.Equal("en-US", AppSettings.Default.Locale);
    }

    [Fact]
    public void ChangingTheLocaleLeavesTheOriginalAlone()
    {
        var changed = AppSettings.Default with { Locale = "fr-FR" };

        Assert.Equal("fr-FR", changed.Locale);
        Assert.Equal("en-US", AppSettings.Default.Locale);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyLocaleIsRejected(string locale)
    {
        Assert.Throws<ArgumentException>(() => AppSettings.Default with { Locale = locale });
        Assert.Throws<ArgumentException>(() => new AppSettings { Locale = locale });
    }

    [Fact]
    public void SupportedLocalesIncludeTheDefault()
    {
        Assert.NotNull(LocaleOption.Find(AppSettings.Default.Locale));
    }

    [Fact]
    public void SupportedLocalesHaveUniqueTagsAndAreSortedByName()
    {
        var supported = LocaleOption.Supported;

        Assert.Equal(supported.Count, supported.Select(option => option.Tag).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(supported.OrderBy(option => option.DisplayName, StringComparer.Ordinal), supported);
    }

    [Fact]
    public void FindIgnoresCase()
    {
        var option = LocaleOption.Find("DE-de");

        Assert.NotNull(option);
        Assert.Equal("de-DE", option.Tag);
        Assert.Equal("German (Germany)", option.DisplayName);
    }

    [Theory]
    [InlineData("xx-XX")]
    [InlineData("")]
    [InlineData(null)]
    public void FindReturnsNullForAnUnsupportedTag(string? tag)
    {
        Assert.Null(LocaleOption.Find(tag));
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData("en-US", " ")]
    public void EmptyTagOrNameIsRejected(string tag, string displayName)
    {
        Assert.Throws<ArgumentException>(() => new LocaleOption(tag, displayName));
    }
}
