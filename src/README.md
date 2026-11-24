<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# src

Paneport's own source code. Everything here is original and released under the MIT License (C-0003 section 4). [C-0004](../docs/contracts/C-0004-architecture-and-layout.md) section 2 fixes the components and the directions in which they may depend on each other. [solution-and-build.md](../docs/design/solution-and-build.md) records how the projects are built.

| Folder | What it holds | State |
|---|---|---|
| [Paneport.Core](Paneport.Core/README.md) | The domain model, view models, settings, and the interfaces that platform code implements | The shell's domain model, service interfaces and view models (M0.3). |
| [Paneport.Steam](Paneport.Steam/README.md) | Steam sign-in, licenses, app information and depot downloads, built on SteamKit2 | No code yet. M2.3 adds the first. |
| [Paneport.UI](Paneport.UI/README.md) | The Avalonia application class, and the views and styles that both app heads share | The shell's main view and its four pages (M0.3). |
| [Paneport.Interop](Paneport.Interop/README.md) | Source-generated bindings to the native host interface | The bindings of the life cycle, diagnostics and memory groups, and the layout conformance test (M1.2). |
| [Paneport.iOS](Paneport.iOS/README.md) | The iOS app head: the entry point, the scene and its views, the game view, the keychain token store, and later input and background tasks | The app head of M1.3: the shell over an idle game view, the native host, the device's storage and keychain, and Diagnostics |
| [Paneport.Desktop](Paneport.Desktop/README.md) | The Avalonia desktop head, for working on the UI from Windows. Never shipped. | Shows the shell over a fake host and fictional sample titles (M0.3). |
| [native](native/README.md) | The native host, in C17: its public headers in `include/paneport/` and its code in `host/` | The life cycle, diagnostics and memory groups, the event thread, the log pipeline and the crash record writer, built as `paneport_host.framework` (M1.2); the signal dispatcher (M2.2); the JIT group and its provider (M3.1). |

The other folder of the C-0004 layout, `guest`, is created by the stage that writes its first code.
