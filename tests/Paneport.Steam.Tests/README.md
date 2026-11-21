<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Steam.Tests

Tests of [Paneport.Steam](../../src/Paneport.Steam/README.md). They never talk to Steam: they use hand-written fakes of the service's ports and synthetic data made by the tests themselves (C-0011 section 1). Account names, passwords, tokens, keys, codes and Steam IDs are made at run time, so that no test source holds one.

| File | What it tests |
|---|---|
| `DependencyDirectionTests.cs` | Paneport.Steam references no project other than Paneport.Core, and this test project references only Paneport.Steam and its dependencies (C-0004 section 2). |
| `SteamSessionTests.cs` | S2.3.1: the connection reconnects after a drop, honors a sign-off request, logs on with the token after a foreground return, stops retrying at its limit, and moves to another server when Steam asks. |
| `SignInFlowTests.cs` | S2.3.2: each sign-in flow reaches Logged on; a wrong code asks again; a declined approval falls back to a code; a QR challenge change updates the code; the password is dropped after use. |
| `TokenStoreTests.cs` | S2.3.3: the token is stored, reused, dropped on each result that means it is invalid and deleted at sign-out, and nothing else is kept. |
| `AppInfoReaderTests.cs` | S2.3.4: the Windows depots and their sizes are found in synthetic app information, and missing sections are reported, not thrown. |
| `GateStepsTests.cs` | S2.3.5 and S2.3.6: a missing key or a code of 0 gives "not granted"; a synthetic manifest gives the right file count, chunk count and size; a chunk that does not match its manifest entry is rejected and never written, and matching synthetic chunks pass. |
| `ChunkVerifierTests.cs` | S2.3.6: a chunk passes only with the length and the SHA-1 hash of its manifest entry. |
| `SteamKitChunkTests.cs` | S2.3.6 on SteamKit2's own processing: a chunk that the test compresses and encrypts as the depot format has it passes `DepotChunk.Process` and the check; a changed byte or a wrong key is a rejection for the content adapter; another chunk's ID under the same checksum fails the SHA-1 check. |
| `RedactingLogTests.cs` | S2.3.11: the log filter, and a full fake run through a capturing log that shows no password, token, depot key, manifest request code, Steam ID or account name. |
| `SteamServicesTests.cs` | The service over SteamKit2 as the heads create it connects to nothing, and the installation's machine identity is created once, kept in the secure item store and replaced when unreadable. |
| `Fakes/` | The fakes of the ports, the clock's waits, the synthetic values and data, and `SteamTestKit`, which wires the service over them. |
