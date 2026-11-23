<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# src/jit-broker

The JIT broker script of C-0006 section 2: the script that StikDebug runs to serve Paneport's two JIT commands, written by Paneport under the MIT License and bundled in the app at `jit-broker/` (C-0009 section 4).

The folder holds no script yet. StikJIT's `INTEGRATION.md`, the only source that C-0006 allows for it, does not document the interface that a script uses, so the script waits for a decision by recelilious. [jit-broker.md](../../docs/design/jit-broker.md) gives what `INTEGRATION.md` documents, what it leaves out, and the options.

Whatever the decision, nothing here may come from StikDebug, its bundled or community scripts such as `universal.js`, or any other product's JIT helper (C-0001 section 6, C-0003 section 9).
