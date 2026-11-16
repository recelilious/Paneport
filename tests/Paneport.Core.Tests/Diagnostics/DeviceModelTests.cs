// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

// The model identifier from pp_device_get_model, or "unknown" when the host cannot tell it.
public sealed class DeviceModelTests
{
    [Fact]
    public void TheHostTellsTheModel()
    {
        var log = new RecordingLog();

        Assert.Equal("iPhone18,1", DeviceModel.Read(new FakeNativeHost(), log));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void AHostThatPredatesTheFunctionGivesUnknownAndALogLine()
    {
        var log = new RecordingLog();

        var model = DeviceModel.Read(new FakeNativeHost { Version = new HostAbiVersion(1, 0) }, log);

        Assert.Equal(DeviceModel.Unknown, model);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("Reading the model identifier failed (Unsupported).", line.Message);
    }
}
