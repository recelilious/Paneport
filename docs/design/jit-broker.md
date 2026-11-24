# JIT Broker Script and Request

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-24T18:22Z |

Back to the [design index](README.md).

This document describes the broker side of the JIT request protocol of C-0006: the script that StikDebug runs to serve the app's two commands, and the request with which the app names it. Stage M3.1 found that StikJIT's `INTEGRATION.md`, the only source that C-0006 section 2 allows for the script, does not document the interface that a script uses, so Paneport writes **no script**: the brief of M3.1 left that case to a decision by recelilious, who chose on 2026-11-23 that Paneport names StikDebug's built-in script instead (section 4), which C-0006 1.1 records. S3.1.6 built the request that names it (section 5). [jit.md](jit.md) describes the app side, which does not depend on the decision. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. What the contracts ask

- **The script** is StikDebug's built-in `universal.js`, which the app's request names (C-0006 1.1 sections 1 to 3). Paneport does not write, bundle, copy or read it, and relies only on the behavior that `INTEGRATION.md` documents for it: it serves commands 0 and 1 (C-0006 section 2). So the repository has no folder for a script and the app bundles none (C-0004 1.2 sections 2 and 3, C-0009 1.3 section 4). Paneport writes no JavaScript either: C-0010 1.2 section 1 dropped the row of the broker script, and writing the language again would need a change to that contract.
- **What Paneport no longer guarantees.** The app sends only commands 0 and 1, only in the `requesting` state and only while a debugger is attached now. The script that serves them is StikDebug's, so Paneport cannot guarantee that it performs no other operation or that it acts only on Paneport's process; the device acceptance checks the result, and every evidence record names StikDebug's version, because an update of StikDebug can change the script (C-0006 section 7, C-0012 section 4).
- **The boundary.** StikDebug is a separate app under AGPL-3.0. Paneport uses only the convention and the URL scheme that `INTEGRATION.md` documents, and nobody writing Paneport's side reads StikDebug's source or scripts (C-0003 section 9). Naming the script in the URL links, embeds and copies nothing: StikDebug runs its own script in its own app (C-0003 1.9 section 9). No code from StikDebug, its bundled or community scripts such as `universal.js`, or any other product's JIT helper is copied, translated or rewritten (C-0001 section 6, C-0006 section 2).

## 2. What `INTEGRATION.md` documents

M3.1 read `INTEGRATION.md` of StikDebug/StikJIT at commit `a9ccad9` of 2026-11-08 (MPL-2.0). In summary, it documents:

- **The app's side of the convention**, which C-0006 section 1 already states: `brk #0xf00d` with the command in `x16`, command 1 (`JIT26PrepareRegion`, address in `x0` and length in `x1`, the prepared region returned in `x0`, a NULL address asking the debug server to allocate it) and command 0 (`JIT26Detach`). The broker moves the program counter past the breakpoint. The app requests every region before it detaches, because a region added later cannot be prepared by a script that has detached.
- **The order of the app's steps**: start the acquisition, wait until `CS_DEBUGGED` appears before any breakpoint, request and prepare every region, make the writable aliases, detach, and call JIT ready only when the commands returned. jit.md sections 5 and 6 follow it.
- **The URL.** `stikdebug://enable-jit` with the query items `bundle-id` (the app's bundle identifier, with which StikDebug returns to the app) and `pid` (the process identifier, with which it targets the process), and, on a device where TXM or SPTM is present, as on every iPhone 17 and 18, either `script-name` with the file name of one of StikDebug's built-in scripts, such as `universal.js`, or `script-data` with the base64 of the app's own script. One script per app, chosen by the developer and never by the user. Opening the URL only means that iOS accepted the request; the app then waits for the commands to complete.
- **The built-in "universal" script**, which recognizes the two commands of the convention and which the document recommends for an app that implements them.

## 3. What it does not document

`INTEGRATION.md` says nothing about the interface that StikDebug gives a script: which functions or objects a script calls, how it learns that the process stopped at a breakpoint, how it reads `x16`, `x0` and `x1` and writes `x0` and the program counter, how it asks the debug server to allocate a region and to write each of its pages, how it lets the process go on, and how it detaches; nor which JavaScript language level the engine supports. Those parts may exist only in StikDebug's source, which Paneport must not read. So the script cannot be written from `INTEGRATION.md` alone, which is the case that the brief of M3.1 names among its decisions.

## 4. The decision

The coordinating session put these options to recelilious on 2026-11-23, recommending A:

| Option | What it means | What changes |
|---|---|---|
| A | The app names StikDebug's built-in script, `script-name=universal.js`, which `INTEGRATION.md` recommends for exactly this convention. Paneport ships no script; its app side stays its own and unchanged. | C-0006 sections 1, 2, 3 step 2 and 7; C-0004 sections 2 and 3, without the JIT broker script component and `src/jit-broker/`; C-0009 section 4, without `jit-broker/`. C-0006 section 7's guarantee that the script does nothing else is then not Paneport's to make. |
| B | Read StikDebug's public documentation, such as its README, `info.md` or wiki, for a script interface, but not its source. It may document none. | C-0006 section 2's "written from `INTEGRATION.md` only" |
| C | Read the interface's definition in StikDebug's source. | C-0003 section 9 and the AGPL boundary. Not proposed. |
| D | Keep the script out of M3.1, and let L3 wait for the decision. | Nothing |

recelilious chose A on 2026-11-23, with the words "Yes, option A", and approved the contract changes as drafted: C-0003 1.9, C-0004 1.2, C-0006 1.1, C-0009 1.3 and C-0010 1.2, in pull request #41, which merged on 2026-11-23. Pull request #40, the app side, then dropped `src/jit-broker/`, which held only its README. The pull request of S3.1.6 followed #40 and built the request, which names `script-name=universal.js` with `bundle-id` and `pid` and which L3's JIT setup screen opens (section 5, jit.md section 8).

## 5. The request

`StikDebugRequest.Build` in `src/Paneport.Core/Jit/` builds the URL with which the app asks StikDebug for JIT (S3.1.6, C-0006 section 3 step 2):

```text
stikdebug://enable-jit?bundle-id=<bundle identifier>&pid=<process identifier>&script-name=universal.js
```

- **The query items** come in that order, so that one app and one process give one text. `INTEGRATION.md` does not ask for an order.
  - `bundle-id` is the app's bundle identifier, with which StikDebug returns to the app.
  - `pid` is its process identifier, in decimal digits.
  - `script-name` is the file name of StikDebug's built-in script.
  - The Trusted Execution Monitor is active on every target device, so the request always names the script and never carries `script-data` (C-0006 section 3 step 2).
- **Escaping.** The bundle identifier is percent-encoded as a query value (RFC 3986). Its letters, digits, hyphens and periods, which are all that a bundle identifier holds (Apple's `CFBundleIdentifier` reference), stay as they are, as do `_` and `~`, which RFC 3986 also leaves unreserved. Any other character, such as one that a re-signing tool might add, is encoded as UTF-8, so it cannot end the value or start another item. A lone surrogate, which a real bundle identifier cannot hold, becomes U+FFFD, `%EF%BF%BD`, as `Uri.EscapeDataString` writes it. The process identifier and the script's name need no escaping.
- **Refusals.** An empty bundle identifier, or a process identifier that is not above 0, is refused with an argument exception. The head passes its own `CFBundleIdentifier` and process identifier, so either means a bug in the head.
- **No platform API.** The builder is pure and, as C-0004 section 2 asks of Paneport.Core, uses no platform API; it returns the URL's text, which the head opens as it is. It is not a `System.Uri`, whose text adds a `/` before the query, which the URL of `INTEGRATION.md` does not have.
- **Its use** (L3, S3.0.1): the JIT setup screen calls `pp_jit_set_enabler_opened(1)` on the main thread right before the URL opener of the iOS head opens the URL, and `pp_jit_set_enabler_opened(0)` when iOS refuses it (jit.md sections 5 and 8). Opening the URL only means that iOS accepted it; the provider then waits for the broker.
- **Tests.** `tests/Paneport.Core.Tests/Jit/StikDebugRequestTests.cs` checks these:
  - the exact URL for the app's bundle identifier;
  - a bundle identifier's letters, digits, hyphens and periods kept as they are;
  - every character outside RFC 3986's unreserved set escaped as UTF-8, with `_` and `~` kept: those that could end the value (`&`, `=`, `#`, `;` and others), `"`, a line feed, a letter beyond ASCII and one beyond the Basic Multilingual Plane;
  - spaces around the identifier kept as `%20`, so the builder does not trim it;
  - the largest process identifier written in full;
  - both refusals.
- **Not yet verified on device:** whether StikDebug accepts the URL. Step 3 of L3's acceptance shows it, and its evidence record names StikDebug's version (C-0006 section 7).

## 6. Sources

Public material consulted (C-0001 section 6):

- StikJIT's `INTEGRATION.md`, commit `a9ccad90c7a8cb547f77ff338890e6633a65ac43` of 2026-11-08, MPL-2.0, read in full through GitHub's API: <https://github.com/StikDebug/StikJIT/blob/a9ccad90c7a8cb547f77ff338890e6633a65ac43/INTEGRATION.md>
- The names of the files of the StikDebug/StikDebug repository, through GitHub's API, without their contents, to see whether any is named for a script interface; none is.
- RFC 3986, *Uniform Resource Identifier (URI): Generic Syntax*, sections 2.3 (the unreserved characters) and 3.4 (the query), read on 2026-11-24: <https://www.rfc-editor.org/rfc/rfc3986>
- Apple's `CFBundleIdentifier` reference, which allows only letters, digits, hyphens and periods in a bundle identifier, read on 2026-11-24: <https://developer.apple.com/documentation/bundleresources/information-property-list/cfbundleidentifier>
