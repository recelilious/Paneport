// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Logging;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

// S2.2.5: what becomes of the call that turns off Mono's crash chaining. A call that returns
// turned it off; one that does not resolve, or fails, is recorded and logged, and never ends
// the startup.
public sealed class CrashChainingStateTests
{
    [Fact]
    public void ACallThatReturnsTurnsItOff()
    {
        var log = new RecordingLog();
        var calls = 0;

        var state = CrashChainingState.Run(() => calls++, log);

        Assert.Equal(1, calls);
        Assert.Equal(CrashChainingOutcome.TurnedOff, state.Outcome);
        Assert.Null(state.Failure);
        Assert.Equal("Off: mono_set_crash_chaining(FALSE) resolved and returned", state.Description);
        Assert.Equal(LogLevel.Information, Assert.Single(log.Lines).Level);
    }

    [Theory]
    [InlineData(typeof(EntryPointNotFoundException))]
    [InlineData(typeof(DllNotFoundException))]
    public void ACallThatDoesNotResolveIsRecorded(Type exceptionType)
    {
        var log = new RecordingLog();

        var state = CrashChainingState.Run(() => throw (Exception)Activator.CreateInstance(exceptionType)!, log);

        Assert.Equal(CrashChainingOutcome.NotResolved, state.Outcome);
        Assert.Equal(exceptionType.Name, state.Failure);
        Assert.StartsWith("Not turned off: mono_set_crash_chaining did not resolve", state.Description, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Lines).Level);
    }

    [Fact]
    public void ACallThatFailsOtherwiseIsRecorded()
    {
        var state = CrashChainingState.Run(() => throw new InvalidOperationException("no"), new RecordingLog());

        Assert.Equal(CrashChainingOutcome.Failed, state.Outcome);
        Assert.Equal("Not turned off: the call failed (InvalidOperationException)", state.Description);
    }

    [Fact]
    public void AHeadWithoutTheCallSaysSo()
    {
        var fakes = new FakeServices();

        var sources = fakes.ToDiagnosticsSources();

        Assert.Same(CrashChainingState.NotCalled, sources.CrashChaining);
        Assert.Equal("Not called: this head does not run on Mono", sources.CrashChaining.Description);
    }
}
