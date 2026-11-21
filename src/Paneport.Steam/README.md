<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Steam

Steam sign-in, licenses, app information and depot downloads, built on SteamKit2 (C-0004 section 2, C-0007). It holds no UI code.

- **Depends on:** Paneport.Core only.
- **Build:** a library with `IsAotCompatible` and an XML documentation file, like the other libraries (C-0010 sections 2 and 5).
- **Tests:** [Paneport.Steam.Tests](../../tests/Paneport.Steam.Tests/README.md).
- **Design:** `docs/design/steam-service.md`, which stage M2.3 writes.

| Folder or file | What it holds |
|---|---|
| `SteamService.cs` | The service of `ISteamService`: the session, the sign-in flows and the saved sign-in. |
| `Ports/` | The small internal interfaces that SteamKit2's adapters implement and the tests fake: the connection, authentication, app information and content. |
| `Session/` | The session's state machine: connection, logon, reconnecting with backoff, the return to the foreground and sign-off; and the keeper of the saved sign-in. |
| `SignIn/` | One sign-in: by approval in the Steam mobile app, by code, or by QR code. |
| `Apps/` | Reading the Windows depots out of an app's information. |
| `Content/` | The manifest's summary, the check of a chunk against its manifest entry, and the test download folder. |
| `Gate/` | The steps of the SteamKit2 gate after sign-in. |
| `Logging/` | The filter that keeps secrets out of the log. |

SteamKit2 is added with its adapters in stage M2.3, after the C-0003 change that registers its dependencies.
