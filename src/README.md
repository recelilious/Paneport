<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# src

Paneport's own source code. Everything here is original and released under the MIT License (C-0003 section 4). [C-0004](../docs/contracts/C-0004-architecture-and-layout.md) section 2 fixes the components and the directions in which they may depend on each other. [solution-and-build.md](../docs/design/solution-and-build.md) records how the projects are built.

| Folder | What it holds | State |
|---|---|---|
| [Paneport.Core](Paneport.Core/README.md) | The domain model, view models, settings, and the interfaces that platform code implements | No code yet. M0.3 adds the first. |
| [Paneport.Steam](Paneport.Steam/README.md) | Steam sign-in, licenses, app information and depot downloads, built on SteamKit2 | No code yet. M2.3 adds the first. |
| [Paneport.UI](Paneport.UI/README.md) | The Avalonia application class, and the views and styles that both app heads share | The application class only. M0.3 adds the pages. |
| [Paneport.Interop](Paneport.Interop/README.md) | Source-generated bindings to the native host interface | No code yet. M1.2 adds the first bindings. |
| [Paneport.Desktop](Paneport.Desktop/README.md) | The Avalonia desktop head, for working on the UI from Windows. Never shipped. | Opens an empty window titled Paneport. |

The other folders of the C-0004 layout (`Paneport.iOS`, `native`, `jit-broker` and `guest`) are created by the stages that write their first code.
