// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core;

namespace Paneport.IOS;

// What the composition root gives the device test flavor's developer menu: the app's services, the
// app's lifecycle, and the downloads folder of C-0004 section 4, below which the Steam gate's test
// downloads go.
internal sealed record DeveloperContext(AppServices Services, AppLifecycle Lifecycle, string DownloadsFolder);
