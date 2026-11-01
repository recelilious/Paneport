// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Compat;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// The paths of the serializer's problems. Only known names reach the serializer from a file, so
// these paths come straight from the test: whatever a path holds, no name that is not plain shows.
public sealed class CompatPathTests
{
    private const string NotShown = "<name not shown>";

    public static TheoryData<string?, string> SerializerPaths { get; } = new()
    {
        { null, "$" },
        { "$", "$" },
        { "$.jitPoolMiB", "$.jitPoolMiB" },
        { "$.reports[0].fpsMedian", "$.reports[0].fpsMedian" },
        { "$.environment." + new string('a', 64), "$.environment." + new string('a', 64) },
        { "$.environment." + new string('a', 65), $"$.environment.{NotShown}" },
        { "$['a b']", $"$.{NotShown}" },
        { "$.environment['a b']", $"$.environment.{NotShown}" },
        { "$.environment['it's'].x", $"$.environment.{NotShown}" },
        { @"$.environment['it\'s']", $"$.environment.{NotShown}" },
        { @"$.environment['ends\']", $"$.environment.{NotShown}" },
        { "$.reports[0]['a b'][1]", $"$.reports[0].{NotShown}" },
        { "$.environment.user~name", $"$.environment.{NotShown}" },
        { "$.environment.C:name", $"$.environment.{NotShown}" },
        { "$.translator.caf" + (char)0xE9, $"$.translator.{NotShown}" },
        { "$.environment.", $"$.environment.{NotShown}" },
        { "no root", "$" },
    };

    [Theory]
    [MemberData(nameof(SerializerPaths))]
    public void SerializerPathShowsOnlyPlainNames(string? path, string shown)
    {
        Assert.Equal(shown, CompatPath.FromSerializer(path));
    }
}
