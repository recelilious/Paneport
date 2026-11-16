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
        Assert.Same(fakes.Memory, services.Diagnostics.Memory);
    }

    [Fact]
    public void RejectsAMissingService()
    {
        var fakes = new FakeServices();
        var diagnostics = fakes.ToDiagnosticsSources();

        AssertRejects("host", () => new AppServices(null!, fakes.Library, fakes.Steam, fakes.Settings, fakes.Tokens, fakes.Log, diagnostics));
        AssertRejects("library", () => new AppServices(fakes.Host, null!, fakes.Steam, fakes.Settings, fakes.Tokens, fakes.Log, diagnostics));
        AssertRejects("steam", () => new AppServices(fakes.Host, fakes.Library, null!, fakes.Settings, fakes.Tokens, fakes.Log, diagnostics));
        AssertRejects("settings", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, null!, fakes.Tokens, fakes.Log, diagnostics));
        AssertRejects("tokens", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, fakes.Settings, null!, fakes.Log, diagnostics));
        AssertRejects("log", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, fakes.Settings, fakes.Tokens, null!, diagnostics));
        AssertRejects("diagnostics", () => new AppServices(fakes.Host, fakes.Library, fakes.Steam, fakes.Settings, fakes.Tokens, fakes.Log, null!));
    }

    private static void AssertRejects(string parameter, Func<AppServices> create)
    {
        Assert.Equal(parameter, Assert.Throws<ArgumentNullException>(create).ParamName);
    }
}
