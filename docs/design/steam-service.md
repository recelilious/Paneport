# Steam Service

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-21T17:45Z |

Back to the [design index](README.md).

This document describes the Steam service of C-0007 sections 2 to 4 as stage M2.3 built it (see the [L2 brief](stages/L2-phase-0-gates.md)): its components and the ports that keep SteamKit2 at the edge, the session with its reconnect policy, sign-in with the platform type, the device name and the machine identity, the saved sign-in, the steps of the SteamKit2 gate, chunk verification, the log filter, the tests, packages, AOT, trimming and the interpreter, iOS networking, threading, the gate page, and what later stages extend. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Scope

M2.3 builds what the SteamKit2 gate of C-0012 section 3 needs: sign-in by approval in the Steam mobile app, by a Steam Guard code and by QR code; the refresh token in the keychain; the license list and app information; depot keys and manifest request codes; a manifest and verified chunks. The Steam gate page in the device test flavor runs it on the iPhone (L2 steps 16 to 28), and a developer entry of the desktop head runs it on Windows.

Out of scope, as the brief says: the library manager, downloads, resume and background work (M7.1); classification and launch (M7.2); the token hand-off to the Dock host (M8.2); the sign-in pages for users (M7.1).

**How the code was written.** Against SteamKit2's public API, its documentation and the source of the library itself, which Paneport integrates (C-0001 section 6 item 1), and this design. DepotDownloader's source was not opened (C-0007 section 2). Where [avalonia_ui_layer.md](../research/avalonia_ui_layer.md) section 2.3 summarizes DepotDownloader, only its observable behavior was used, never as an outline.

## 2. Components

| Where | What | Depends on |
|---|---|---|
| `Paneport.Core/Steam` | `ISteamService`: the session's status, resume, the three sign-ins and sign-out. `ISignInAttempt` with `SignInProgress` and `SignInResult`. `ISteamGateSteps`: the gate's steps after sign-in. `SignedOutSteamService`, the stub of a build without a Steam service. | Nothing new |
| `Paneport.Core/ViewModels` | `SteamGateViewModel`, the gate page (section 13) | The interfaces above |
| `Paneport.UI` | `Views/SteamGatePage.axaml`, and `Controls/QrCodeView`, which draws the QR code (D13) | Net.Codecrete.QrCodeGenerator |
| `Paneport.Steam` | `SteamService` over the state machines of sections 4 to 8, the ports of section 3, the SteamKit2 adapters, and `SteamServices`, which wires them for a head | SteamKit2 and the packages it brings in |
| `Paneport.Desktop` | The Steam service over the in-memory token store, and a Developer section that opens the gate page | |
| `Paneport.iOS`, `tests/device` | `AppLifecycle`, which carries the scene's returns to the foreground; the device test flavor's Steam gate entry, which creates the service at the page's first opening | |

The main app's launch creates no Steam service until M7.1 brings its sign-in pages; its Settings page keeps `SignedOutSteamService`. The test flavor creates the service lazily, so a fault in SteamKit2's setup on the device cannot stop the app from starting.

## 3. Ports and adapters

The state machines talk to SteamKit2 only through four small internal interfaces, so that their tests run against fakes with synthetic data (C-0011 section 1), and SteamKit2's types never leave the adapters:

| Port | Adapter over SteamKit2 | What it does |
|---|---|---|
| `ISteamConnection` with `IConnectionListener` | `SteamKitConnection` | Connect, disconnect, avoid the current server, log on with an account name and a refresh token, log off; reports connected, disconnected, logged on, logged off and the license list. |
| `ISteamAuthenticator`, `IAuthSession`, `IQrAuthSession`, `ICodeSource` | `SteamKitAuthenticator` | Starts a sign-in with credentials or with a QR code and waits for its tokens; asks the flow whether to wait for an approval and which code to send. |
| `ISteamApps` | `SteamKitApps` | PICS access tokens, app information as a key-value tree, depot keys. |
| `ISteamContent` | `SteamKitContent` | Manifest request codes, content servers and their tokens, the manifest, and a processed chunk. |

- **Results.** `SteamResult` repeats the numbers of SteamKit2's `EResult` that the state machines act on, so an adapter converts with a cast. `SteamResults` says which results mean a refused token (section 5) and which ask for another server (section 4).
- **Failures.** Authentication refusals arrive as `SteamAuthException` with Steam's result. A content download that fails at the server or on the way arrives as `ContentDownloadException`, with the HTTP status when there is one; a chunk that fails SteamKit2's processing arrives as `ChunkRejectedException`. Neither carries SteamKit2's message, which can hold a URL with a server token.
- **Data.** The adapters copy SteamKit2's `KeyValue` into `KeyValueNode` and its `DepotManifest` into `DepotManifestData`, `ManifestFile` and `ManifestChunk`.
- **Servers.** SteamKit2 marks a server bad only through an internal method, so `SteamKitConnection` picks each server itself, from `SmartCMServerList.GetNextServerCandidateAsync`, and keeps the servers that Steam asked it to leave out of its next picks, from `GetAllEndPoints`. Picking the server also tells it the transport (section 11).

## 4. The session

`SteamSession` keeps the connection and the logon. Every change of its state runs on a serial queue (section 12), and its status is published for any thread.

- **Goal.** The session works towards nothing, a connection for a running sign-in, or a logon with a saved sign-in, which it keeps up until sign-off.
- **Reconnect.** After a connection ends on its own, or an attempt fails, it waits and connects again: 1, 2, 4, 8 and 16 seconds before attempts 1 to 5 (`ReconnectPolicy.Default`, doubling from 1 s, capped at 30 s). After five failed attempts in a row it gives up: the state is Offline, the saved sign-in stays, and a resume or a return to the foreground tries again with a fresh count.
- **Another server.** A logon result of `TryAnotherCM` or `ServiceUnavailable` makes the session leave the current server out and connect to another one at once, at most three times per logon.
- **Foreground.** iOS suspends a backgrounded app and may close its sockets (`avalonia_ui_layer.md` section 2.3). When the scene returns to the foreground, `AppLifecycle` tells the service, and a session that should be logged on ends its connection, connects again and logs on with the saved token. A session without that goal does nothing.
- **Sign-off.** On request the session logs off, waits up to 2 seconds for Steam to confirm, disconnects, and does not connect again. The service's sign-out also deletes the saved sign-in first (section 5); signing off for a Dock session (M8.2) keeps it.
- **Logged off elsewhere.** A logoff with `LoggedInElsewhere` means that another client took the account's session, as Valve's client in a Dock session will. The session does not take it back (C-0007 section 4). Any other logoff that Steam sends logs on again.
- **Licenses.** Steam sends the license list after each logon; the gate counts it.

## 5. Sign-in and the saved sign-in

**Flows** (C-0007 section 3). `SignInFlow` runs one sign-in; starting another cancels the running one.

| Flow | How | Falls back |
|---|---|---|
| Approval, the default | Account name and password; Steam asks whether to wait for an approval in the Steam mobile app, which works on the same phone, and the flow says yes. | A wait that ends without an approval, such as a denial, and the user's own choice while waiting ("Enter a code instead") start a new sign-in that asks for a code. |
| Code | Account name and password; the flow tells Steam not to wait for an approval, and Steam asks for a code from the authenticator, or by email for an account without one. | A code that Steam refuses is asked for again. |
| QR code | Steam gives a challenge that the Steam mobile app on another device scans; the page draws it and draws it again when Steam replaces it. | |

After the tokens arrive, the flow logs the session on with them and saves the sign-in only when the logon succeeds. It measures the total time and the time spent waiting for the user: for the approval, the code or the scan. Errors read in plain words, such as "The account name or the password is wrong.", and name no secret. SteamKit2's hint about the email address that the code went to is not passed on.

**The password** goes only to Valve's servers (C-0007 section 1). The page empties its box when the sign-in starts. The flow keeps its reference for the sign-in's length, because a fallback to a code starts a new sign-in with it, and drops it when the attempt ends, whatever the end. It is never logged or stored.

**Platform type.** Every sign-in asks for a persistent refresh token of the platform type `k_EAuthTokenPlatformType_SteamClient`, the type that Valve's client uses, so that the Dock host can use the same token (C-0007 section 3). That Valve's client accepts such a token from Paneport is confirmed in use by M8.2.

**Device name and machine identity.** SteamKit2 fills the sign-in's device name and the logon's machine name with the machine's own name by default, which on a phone can be the owner's name for it. The service sets both to fixed text instead: "Paneport (iPhone)" in the device test flavor, "Paneport (desktop head)" on Windows. SteamKit2 also sends a machine identity with each logon, hashed from values that its provider reads; on iOS its provider would hash the device's name. The device test flavor gives SteamKit2 a provider of its own that derives the three values from a random identity of the installation (`MachineIdentity`, 32 bytes), kept in the keychain under its own key, `steam-machine-identity`, beside the sign-in but not part of it. Signing out keeps it, so the next sign-in from this installation looks like the same machine to Steam. The desktop head leaves the machine identity to SteamKit2's provider for Windows. The operating system type stays SteamKit2's default: on iOS that is `Unknown`, which the device check records (section 15).

**The saved sign-in** (C-0007 section 3, S2.3.3). `TokenKeeper` is the service's only way to the token store, M1.3's `ITokenStore`: the keychain item `steam-sign-in` on the device, memory for one run on the desktop.

- After a sign-in's logon succeeds, it saves the account name and the refresh token as a `SavedSignIn`, and nothing else: no access token, Steam Guard data, Steam ID or password.
- A resume reads it and logs on with it, without asking for a password (L2 step 21).
- A logon result that says the token itself is no good drops it: `InvalidPassword`, `InvalidSignature`, `AccessDenied`, `Expired` or `Revoked`, also when it comes on an automatic reconnect. Any other refusal, such as a rate limit, keeps it.
- Sign-out deletes it before it disconnects.
- Its operations run one at a time in the order of the calls, so a drop that comes before a new sign-in's save can never delete the new token.

**The token in managed memory.** [app-shell.md](app-shell.md) section 10 asked whether the managed side keeps the refresh token as bytes that it can clear. It does not: SteamKit2 takes the token as a string in its logon details and returns it as a string from the authentication, so a byte copy of ours would not keep it out of the managed heap. The token stays a `Secret`, whose text form is redacted.

## 6. The gate's steps

`GateSteps` runs the steps of L2 steps 22 to 28. It keeps the depot key, the manifest request code and the manifest to itself and tells the page only whether it got them; each needs a logon, and the later steps need the earlier ones.

| Step | How |
|---|---|
| Licenses | The count of the license list that Steam sent after the logon. |
| App information | A PICS access token for the app, then its product information. `AppInfoReader` reads the name from `common/name` and the Windows depots from `depots`: an entry with a numeric name whose `config/oslist` names `windows`, or has no list, which means every platform. For each it reads the name, `config/osarch`, the public manifest from `manifests/public`, either the manifest ID itself or a section with `gid`, `size` and `download`, and the size from that section or from `maxsize`. A missing section or an unreadable value becomes a problem in the result, never an exception (S2.3.4). The page picks the smallest Windows depot with a public manifest first (L2 step 24). |
| Depot key | Steam's answer; a result other than OK, or an empty key, is "not granted". The key's bytes are wiped when the steps forget it. |
| Manifest request code | For the depot's public manifest; a code of 0 is "not granted" (S2.3.5). |
| Manifest | From the first of at most four content servers that gives it, with the key and the code, timed from the request to the parsed manifest. The summary counts files without folders, distinct chunks, since files can share one, and the files' sizes. |
| Chunks | The ten smallest distinct chunks of the manifest, each from the first server that gives it, each checked (section 7); only a chunk that passes is written. |
| Delete test data | Deletes the test folder and forgets the app, the key, the code and the manifest. |

- **Content servers.** `GetServersForSteamPipe` gives the servers; the service keeps those of type `CDN` or `SteamCache` that serve every app, the least loaded first.
- **Server tokens.** A server that refuses a request with HTTP 403 gets a token from Steam for it, once, and the request is made again. The token is never logged.
- **Test folder.** `steam-gate-test` below the downloads folder of C-0004 section 4, which iOS does not back up; on the desktop, below the head's own local folder. Each verified chunk is written as one file named by its chunk ID.

## 7. Chunk verification

Every chunk is checked against its manifest entry before anything uses it (C-0007 section 4):

1. SteamKit2's `DepotChunk.Process`, which its CDN client runs, decrypts the chunk with the depot key, decompresses it from VZip, Zstandard or PKZip, and checks its length and Adler-32 checksum. A chunk that fails is rejected.
2. `ChunkVerifier` then checks the processed content's length against the entry's uncompressed length, and its SHA-1 hash against the entry's chunk ID. SteamKit2's own tests show that a chunk's ID is the SHA-1 hash of its processed content (`DepotChunkFacts.cs`).
3. Only a chunk that passes both is written. A rejected chunk is counted and never written (S2.3.6).

The SHA-1 check adds a cryptographic match to the Adler-32 one. Its suppression of CA5350 says why: the format chooses SHA-1, and it protects no secret.

SteamKit2 downloads and processes a chunk in one call, so the content adapter sorts its failures: a failed length or Adler-32 check or bad compressed data (`InvalidDataException`), a padding that does not decrypt, as with a wrong key (`CryptographicException`), and a zip entry that claims more than the entry's length (`ArgumentException`) reject the chunk, and no other server is asked for it; a failure at the server or on the way is a download failure, and the next server is tried.

**How the tests build a chunk** (the brief left it to this stage). The tests make a chunk the way the depot format has it: content of their own, compressed into a zip with one entry, then encrypted with a random AES-256 key, CBC with PKCS7 behind a random IV that is itself encrypted with ECB, with the entry's ID as the content's SHA-1 hash and its checksum from SteamKit2's public `DepotChunk.AdlerHash`. SteamKit2's `DepotChunk.Process` accepts it and the service's check matches it; a changed byte or a wrong key fails the processing in a way that the adapter counts as a rejection; and an entry with another ID but the same checksum fails the SHA-1 check.

## 8. Logging and secrets

No log line may hold a password, token, depot key, manifest request code, Steam ID or account name (C-0004 section 4, C-0010 section 2, S2.3.11). Every line of the service passes `RedactingLog` on its way to the app's log. It removes:

- each value that the service registered as it learned it, in any case: the account name and the password when a sign-in starts, the refresh token when it arrives or is read from the store, the Steam ID after the logon, as a 64-bit number, in its `[U:1:Z]` form and as its account number, the depot key in hexadecimal and base64, the manifest request code, and each server token;
- whatever has the shape of a secret, whoever wrote it: a JSON web token, a Steam ID as `STEAM_X:Y:Z` or `[U:1:Z]`, a run of 32 or more hexadecimal characters, a run of 40 or more base64 characters, and a number of 15 or more digits. IDs of apps and depots have at most 10 digits and stay.

Exceptions are logged by type only, never by message. SteamKit2's own debug output stays off; a head can turn it on for a developer, and it then passes the same filter. A line that SteamKit2 wrote about a value before the service registered it would rely on the shape rules alone, which is why its output is off by default.

## 9. Tests

| Project | What it covers |
|---|---|
| Paneport.Steam.Tests | S2.3.1: the session over a fake connection and manual waits: logon, reconnect with the backoff of section 4 and its limit, moving servers, the foreground return, sign-off with and without the logoff's confirmation, the logoff of another logon, licenses. S2.3.2: every flow, a wrong code, a declined approval and the user's switch to a code, a changed QR challenge, the password dropped on success, cancellation and failure, a new sign-in that cancels the running one. S2.3.3: the store's calls for save, resume, each refused result and sign-out. S2.3.4: the reader on synthetic app information. S2.3.5 and S2.3.6: the steps over fake content, with synthetic chunks whose IDs are their SHA-1 hashes, a changed byte, a wrong length, a chunk that SteamKit2 rejects and a server token after HTTP 403; and real encrypted chunks of the tests' own through SteamKit2's processing (section 7). S2.3.11: the filter's rules, and a full fake run through a capturing log that shows none of the values. The factory and the machine identity. |
| Paneport.Core.Tests | S2.3.9: the gate page's view model over a fake service and fake steps, through the UI dispatcher; the stub. |
| Paneport.UI.Tests | S2.3.9 and S2.3.10: the page, opened on the shell over the fakes, through every state of L2 steps 16 to 28, with no password or challenge in its text; the QR code has the modules of its text and is drawn again when the challenge changes. |

- **Fakes, not mocks**, written for the ports (C-0011 section 1). Waits end only when a test releases them, so the reconnect tests need no clock.
- **Synthetic values.** Account names, passwords, tokens shaped like Steam's, depot keys, manifest codes, server tokens and Steam IDs are made at run time; no test source holds one, and the personal-data check finds nothing in `tests/`. App and depot IDs start at 4,100,000,100, far above any that Steam has assigned.
- **No network.** No test connects to Steam; the factory's test only creates the client.

## 10. Packages, AOT, trimming and the interpreter

| Package | Version | Register row (C-0003 1.7) |
|---|---|---|
| SteamKit2 | 4.0.0-Beta.2 | SteamKit2 |
| protobuf-net, protobuf-net.Core | 3.4.40, protobuf-net pinned explicitly | protobuf-net |
| ZstdSharp.Port | 0.8.8 | ZstdSharp (ZstdSharp.Port) |
| System.IO.Hashing | 10.0.13 | .NET runtime |
| Net.Codecrete.QrCodeGenerator | 3.2.1 | QR code generator (Net.Codecrete.QrCodeGenerator) |

- **Why a beta.** SteamKit2 4.0.0-Beta.1 is the first release that serializes Steam's messages through a compile-time protobuf-net model instead of protobuf-net's runtime model, which reflects over types and emits code: the path that fails under full ahead-of-time compilation without the interpreter (SteamKit2 issue 1466, `avalonia_ui_layer.md` section 2.3). Beta.2 fixes WebSocket teardown and encrypted-connection failures. recelilious approved it on 2026-11-21. Later fixes on SteamKit2's main branch move the pin in a reviewed change once they ship.
- **The interpreter** stays off first in Release, as C-0009 section 3 says. The device check (S2.3.8) runs every step; if one fails, the smallest assembly list that makes it pass, such as `-all,protobuf-net`, goes into `eng/toolchain.json` and this section.
- **Trimming.** Our own code reports no trimming or AOT warning. protobuf-net and protobuf-net.Core keep their runtime model's code for callers without a model, and the trimmer reports it: 11 warnings in protobuf-net.Core in the trimmed test app, and one IL2104 each for protobuf-net and protobuf-net.Core in the iOS build, which keeps both whole. On 2026-11-21 recelilious approved an allow list under `eng/` for them. [`eng/trimming/`](../../eng/trimming/README.md) excuses each of the 125 warnings that the two assemblies give when they are kept whole, by code and method, each with a justification that cites C-0009 section 3, and the trimmed test app and the iOS heads pass its files to the trimmer ([repository-checks.md](repository-checks.md) section 7, [ios-app-head.md](ios-app-head.md) section 12). The SDK reads them through `_ILLinkSuppressions`, an internal item of its own, so a rename would bring the warnings back and fail the builds rather than hide them. The list goes once protobuf-net is clean under trimming. The iOS build trims SteamKit2, which is marked trimmable, and keeps protobuf-net whole. SteamKit2, protobuf-net and protobuf-net.Core are rooted for trimming only if the device needs it (experiment E10).
- **Suppressions.** Paneport.Steam needs none for SteamKit2's API (C-0010 section 2).

## 11. Networking on iOS

- **HTTP.** The default HTTP handler of .NET for iOS, `NSUrlSessionHandler`, cannot upgrade a connection to WebSocket, which SteamKit2's default transport needs (`avalonia_ui_layer.md` sections 2.3 and 3.4, experiment E11). `SteamServices` gives SteamKit2 a `SocketsHttpHandler` client for every purpose it asks for: the WebSocket connection, Web API calls and content downloads, each its own client, with a 10-second connect timeout.
- **Transports.** SteamKit2 may use WebSocket or TCP; a head can choose TCP only. The gate page shows the transport of the server that the connection picked, and the tester ticks whether a system permission prompt, such as Local Network, appeared, because an app cannot see that prompt itself.

## 12. Threading

| Thread | What runs there |
|---|---|
| SteamKit2's callback thread, a managed worker thread of the connection (C-0004 section 1) | SteamKit2's callbacks, which the adapter passes to the session |
| The session's serial queue, run by whichever thread posts to an idle queue | Every change of the session's state, one at a time, never one inside another |
| Thread pool | The sign-in flows and the gate's steps after their first await |
| UI thread | The gate page; the services' events reach it through `IUiDispatcher` |

The session never blocks a caller: a connection request picks its server on the thread pool, because the first pick may fetch Steam's server list. Its tasks run their continuations asynchronously, so no caller's code runs on the queue.

## 13. The gate page

`SteamGateViewModel` shows what L2 steps 16 to 28 name: the build line; the session's state, whether a sign-in is saved and the transport; the three sign-ins with their stage, the code box, the QR code and the times; the steps with their results; and a summary to copy into the evidence record with the sign-in times by method, the license count, the app's depot count, whether the key and the code were obtained, the manifest and the chunks, the transport, the permission prompt, and the run's peak memory footprint and thermal state (C-0011 section 1). It never shows a token, key, code or Steam ID; the QR challenge is drawn only. One page lives as long as the app, so its results survive a visit to another page.

- **Desktop run** (L2 brief, M2.3 verification): `dotnet run --project src/Paneport.Desktop`, open Developer, sign in by approval, then Licenses, App info with an app ID, Depot key, Manifest code, Manifest and Chunks. The sign-in stays in memory for the run (C-0007 section 3).
- **Device check** (S2.3.8): the device test flavor's developer menu opens it as "Steam gate"; recelilious runs L2 steps 16 to 28.

## 14. What later stages extend

- **M7.1** builds the library and downloads on the session, the sign-in flows, the saved sign-in, the content port and chunk verification; adds the user's sign-in pages with the QR control; moves the main app to the real service; and decides whether leaving a page cancels its steps.
- **M8.2** uses the session's sign-off without deleting the token and its resume around a Dock session, confirms that Valve's client accepts the SteamClient token, and may need a logon ID of its own, since Steam allows one session per logon ID and public address.
- **M10.2** revisits the interpreter list, the trimming list and the WebSocket setup on .NET 11.

## 15. Open questions and risks

- **The trimming warnings** of protobuf-net: recelilious's decision (section 10).
- **The interpreter setting** on the device: S2.3.8.
- **An approval while Paneport is in the background.** Approving in the Steam mobile app on the same phone puts Paneport in the background for a while; if iOS closes its socket, the wait for the approval fails and the flow falls back to a code. The device run shows how often that happens.
- **The operating system type** that SteamKit2 reports on iOS is `Unknown`; the device run shows whether Steam accepts it.
- **App information over HTTP.** Steam can answer a product information request with an address to fetch it from instead; the steps do not fetch it yet and report the app's information as missing.
- **The app information keys** of section 6 follow the layout that `avalonia_ui_layer.md` section 2.3 describes and that SteamKit2's PICS types carry; the desktop run confirms the detailed keys on a real app.
- **SteamKit2's source offer** for releases and the `ios` job's public artifacts waits for recelilious's decision; M2.3 does not merge before it.
- **Repeated sign-ins** may bring Steam Guard checks or new-device notices (E14), so the tests use fakes and only recelilious signs in.

## 16. Sources

Public material consulted (C-0001 section 6):

- SteamKit2 releases, 4.0.0-Beta.1 and 4.0.0-Beta.2: <https://github.com/SteamRE/SteamKit/releases>
- SteamKit2 source at 4.0.0-Beta.2, read for its API and behavior: authentication (`AuthSession.cs`, `AuthSessionDetails.cs`, `IAuthenticator.cs`, `QrAuthSession.cs`), `SteamUser.cs` with the logon's machine name, `HardwareUtils.cs` with the machine identity, `Utils.cs` with the operating system type, `SmartCMServerList.cs`, the CDN client and `DepotChunk.cs`, and `DepotChunkFacts.cs` with chunk IDs as SHA-1 hashes: <https://github.com/SteamRE/SteamKit/tree/4.0.0-Beta.2>
- SteamKit2 issue 1466, ahead-of-time compilation: <https://github.com/SteamRE/SteamKit/issues/1466>
- SteamKit2 on NuGet: <https://www.nuget.org/packages/SteamKit2>
- protobuf-net: <https://github.com/protobuf-net/protobuf-net>
- Net.Codecrete.QrCodeGenerator: <https://github.com/manuelbl/QrCodeGenerator>
- `SocketsHttpHandler`: <https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler>
- Trimming options and warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options>
- UIKit's `sceneWillEnterForeground(_:)`: <https://developer.apple.com/documentation/uikit/uiscenedelegate/scenewillenterforeground(_:)>
- Research notes: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md) sections 2.3, 3.4 and 4, and [steam_integration.md](../research/steam_integration.md) on confirming sign-ins.
- Contracts C-0003, C-0004, C-0007, C-0009, C-0010, C-0011 and C-0012, and the [L2 brief](stages/L2-phase-0-gates.md).
