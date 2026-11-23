# JIT Broker Script

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-23T04:10Z |

Back to the [design index](README.md).

This document describes the broker side of the JIT request protocol of C-0006: the script that StikDebug runs to serve the app's two commands. Stage M3.1 found that StikJIT's `INTEGRATION.md`, the only source that C-0006 section 2 allows for the script, does not document the interface that a script uses, so the script is **not written**: the brief of M3.1 leaves that case to a decision by recelilious, which is open. [jit.md](jit.md) describes the app side, which M3.1 built and which does not depend on the decision. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. What the contracts ask

- **The script** is Paneport's own, under the MIT License, in `src/jit-broker/`, written from `INTEGRATION.md` only, bundled in the app at `jit-broker/` and handed to StikDebug through the URL scheme (C-0006 section 2, C-0004 section 2, C-0009 section 4). It handles commands 0 and 1 only, for Paneport's own process only, and performs no other operation (C-0006 section 7).
- **The boundary.** StikDebug is a separate app under AGPL-3.0. Paneport uses only the convention and the URL scheme that `INTEGRATION.md` documents, and nobody writing Paneport's side reads StikDebug's source or scripts (C-0003 section 9). No code from StikDebug, its bundled or community scripts such as `universal.js`, or any other product's JIT helper is copied, translated or rewritten (C-0001 section 6, C-0006 section 2).
- **The language level** of the script is the one that StikDebug's script interface supports, recorded here (C-0010 section 1).

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

Until recelilious decides, M3.1 follows D: `src/jit-broker/` holds only its README. If recelilious chooses A, the contract changes are drafted on a `contract/` branch of their own, reviewed before they are pushed, and this document then records the URL that the app builds; L3's JIT setup screen builds it either way, from `paneport/jit.h`'s flow (jit.md section 8).

## 5. Sources

Public material consulted (C-0001 section 6):

- StikJIT's `INTEGRATION.md`, commit `a9ccad90c7a8cb547f77ff338890e6633a65ac43` of 2026-11-08, MPL-2.0, read in full through GitHub's API: <https://github.com/StikDebug/StikJIT/blob/a9ccad90c7a8cb547f77ff338890e6633a65ac43/INTEGRATION.md>
- The names of the files of the StikDebug/StikDebug repository, through GitHub's API, without their contents, to see whether any is named for a script interface; none is.
