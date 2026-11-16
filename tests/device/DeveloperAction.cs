// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.DeviceTest;

// A button of a developer page: its label, and the work it runs, which returns the text that the
// page shows as its result.
internal sealed record DeveloperAction(string Label, Func<CancellationToken, Task<string>> Run);
