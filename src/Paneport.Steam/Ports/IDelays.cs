// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// Waits, for the reconnect backoff and the logoff timeout. The service waits on the system
// clock; the tests release each wait themselves.
internal interface IDelays
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
