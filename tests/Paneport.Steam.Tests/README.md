<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Steam.Tests

Tests of [Paneport.Steam](../../src/Paneport.Steam/README.md). They never talk to Steam: they use hand-written fakes of the service's ports and synthetic data made by the tests themselves (C-0011 section 1). Account names, passwords, tokens, keys, codes and Steam IDs are made at run time, so that no test source holds one.

| File | What it tests |
|---|---|
| `DependencyDirectionTests.cs` | Paneport.Steam references no project other than Paneport.Core, and this test project references only Paneport.Steam and its dependencies (C-0004 section 2). |
| `SteamSessionTests.cs` | S2.3.1: the connection reconnects after a drop, honors a sign-off request, logs on with the token after a foreground return, also one while it connects, stops retrying at its limit and starts the count again after an accepted logon, ignores a logon's answer on a connection that it ends, and moves to another server when Steam asks. |
| `SignInFlowTests.cs` | S2.3.2: each sign-in flow reaches Logged on; a wrong code asks again; a declined approval falls back to a code; a QR challenge change updates the code; the password is dropped after use; a sign-in that replaces one waiting to connect again connects after the wait; a cancel once the logon is there logs the session off; a logon that could not reach Steam, or that a sign-out cancelled, says so. |
| `TokenStoreTests.cs` | S2.3.3: the token is stored, reused, dropped on each result that means it is invalid and deleted at sign-out, and nothing else is kept. |
| `AppInfoReaderTests.cs` | S2.3.4: the Windows depots and their sizes are found in synthetic app information, and missing sections are reported, not thrown. |
| `GateStepsTests.cs` | S2.3.5 and S2.3.6: a missing key or a code of 0 gives "not granted"; a synthetic manifest gives the right file count, chunk count and size; a chunk that does not match its manifest entry is rejected and never written, and matching synthetic chunks pass; a replaced key or code, and the server tokens and the last key and code with the test data, leave the log filter; a step wipes its copy of the key. |
| `ChunkVerifierTests.cs` | S2.3.6: a chunk passes only with the length and the SHA-1 hash of its manifest entry. |
| `SteamKitChunkTests.cs` | S2.3.6 on SteamKit2's own processing: a chunk that the test compresses and encrypts as the depot format has it passes `DepotChunk.Process` and the check; a changed byte or a wrong key is a rejection for the content adapter; another chunk's ID under the same checksum fails the SHA-1 check. Over SteamKit2's CDN client and a server on the loopback interface: a response of the wrong length is a download failure, and one of the right length with other bytes a rejected chunk. |
| `SteamKitAdapterTests.cs` | What the SteamKit2 adapters send and how they read Steam's answers, which no fake reaches, with no connection: the details that the sign-in and the logon send, the login ID with a known answer and the machine identity's wiring, the Steam ID's registration with the log filter on an accepted logon, the server pick after a move, and the servers to avoid that the logon forgets. And how a sign-in ends when its connection ends, over SteamKit2's own sessions on a client that never connects: a wait that the drop cancels, a poll or a code that starts after it, a begin without a connection, and a wait that ends at once on a cancel that SteamKit2's poll ignores. |
| `RedactingLogTests.cs` | S2.3.11: the log filter, with its count of each value's registrations, and a full fake run through a capturing log that shows no password, token, depot key, manifest request code, Steam ID or account name; an attempt that ends leaves another's password in the filter; a sign-out, or a token that Steam refuses, takes the saved sign-in out of it. |
| `SteamServicesTests.cs` | The service over SteamKit2 as the heads create it connects to nothing, and the installation's machine identity is created once, kept in the secure item store and replaced when unreadable. |
| `Fakes/` | The fakes of the ports, the clock's waits, the synthetic values and data, and `SteamTestKit`, which wires the service over them. |
