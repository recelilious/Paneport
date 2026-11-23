// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Xunit;

namespace Paneport.Interop.Tests;

// The JIT stand-in of the session group's test builds (M4.3), against the host test library.
// The session itself needs the Wine runtime, which the CTest suite fakes
// (tests/native/test_session.c) and the macOS harness runs; the layouts of its structs are in
// the conformance test.
public sealed class SessionBindingTests
{
    [Fact]
    public void TheJitStandInCrossesTheInterface()
    {
        HostTestExports.SetJitReady(true);
        HostTestExports.SetJitReady(false);
    }
}
