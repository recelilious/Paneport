// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// Waits on a clock, the system's in the app.
internal sealed class SystemDelays(TimeProvider time) : IDelays
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, time, cancellationToken);
}
