// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.DeviceTest;

// An entry of the developer menu: its title, what it does, and how to make its page.
internal sealed record DeveloperEntry(string Title, string Description, Func<DeveloperToolViewModel> Create);
