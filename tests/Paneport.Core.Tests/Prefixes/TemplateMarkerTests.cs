// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using Paneport.Core.Prefixes;
using Xunit;

namespace Paneport.Core.Tests.Prefixes;

// The marker of a template prefix is read strictly.
public sealed class TemplateMarkerTests
{
    [Fact]
    public void AValidMarkerIsRead()
    {
        var ok = Parse("""{ "schema": 1, "version": 3, "wineRevision": "abc123", "replaceOnUpdate": ["drive_c/windows/"] }""", out var marker);

        Assert.True(ok);
        Assert.Equal(3, marker!.Version);
        Assert.Equal("abc123", marker.WineRevision);
        Assert.Equal(["drive_c/windows/"], marker.ReplaceOnUpdate);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "schema": 2, "version": 1, "wineRevision": "a", "replaceOnUpdate": [] }""")]
    [InlineData("""{ "schema": 1, "version": 0, "wineRevision": "a", "replaceOnUpdate": [] }""")]
    [InlineData("""{ "schema": 1, "version": 1.5, "wineRevision": "a", "replaceOnUpdate": [] }""")]
    [InlineData("""{ "schema": 1, "version": "1", "wineRevision": "a", "replaceOnUpdate": [] }""")]
    [InlineData("""{ "schema": 1, "version": 1, "replaceOnUpdate": [] }""")]
    [InlineData("""{ "schema": 1, "version": 1, "wineRevision": "a" }""")]
    [InlineData("""{ "schema": 1, "version": 1, "wineRevision": "a", "replaceOnUpdate": ["drive_c/windows"] }""")]
    [InlineData("""{ "schema": 1, "version": 1, "wineRevision": "a", "replaceOnUpdate": ["/drive_c/"] }""")]
    [InlineData("""{ "schema": 1, "version": 1, "wineRevision": "a", "replaceOnUpdate": ["../outside/"] }""")]
    [InlineData("""{ "schema": 1, "version": 1, "wineRevision": "a", "replaceOnUpdate": ["a\\b/"] }""")]
    [InlineData("""{ "schema": 1, "version": 1, "wineRevision": "a", "replaceOnUpdate": [3] }""")]
    public void AMarkerWithAProblemIsRefused(string json)
    {
        Assert.False(Parse(json, out var marker));
        Assert.Null(marker);
    }

    private static bool Parse(string json, out TemplateMarker? marker) =>
        TemplateMarker.TryParse(Encoding.UTF8.GetBytes(json), out marker, out _);
}
