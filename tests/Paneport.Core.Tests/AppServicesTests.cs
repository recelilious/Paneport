// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class AppServicesTests
{
    [Fact]
    public void KeepsEveryService()
    {
        var fakes = new FakeServices();

        var services = fakes.ToAppServices();

        Assert.Same(fakes.Host, services.Host);
        Assert.Same(fakes.Library, services.Library);
        Assert.Same(fakes.Steam, services.Steam);
        Assert.Same(fakes.Settings, services.Settings);
        Assert.Same(fakes.Tokens, services.Tokens);
        Assert.Same(fakes.Log, services.Log);
    }

    [Fact]
    public void RejectsAMissingService()
    {
        var fakes = new FakeServices();

        AssertRejects("host", () => new AppServices(null!, fakes.Library, fakes.Steam, fakes.Settings, fakes.Tokens, fakes.Log));
        AssertRejects("library", () => new AppServices(fakes.Host, null!, fakes.Steam, fakes.Settings, fakes.Tokens, fakes.Log));
        AssertRejects("steam", () => new AppServices(fakes.Host, fakes.Library, null!, fakes.Settings, fakes.Tokens, fakes.Log));
        AssertRejects("settings", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, null!, fakes.Tokens, fakes.Log));
        AssertRejects("tokens", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, fakes.Settings, null!, fakes.Log));
        AssertRejects("log", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, fakes.Settings, fakes.Tokens, null!));
    }

    private static void AssertRejects(string parameter, Func<AppServices> create)
    {
        Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(create).ParamName);
    }
}
