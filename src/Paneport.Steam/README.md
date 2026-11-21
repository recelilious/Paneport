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
| `SteamKit/` | The adapters of the ports over SteamKit2: the client and its callback thread, authentication, app information, content and the machine identity. |
| `SteamServices.cs`, `SteamServiceOptions.cs`, `MachineIdentity.cs` | What the heads create: the service and the gate's steps over one SteamKit2 client, its options, and the installation's random identity in the keychain. |
