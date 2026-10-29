<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Interop

Source-generated bindings to the native host interface (C-0004 section 2, C-0005). It is the only project allowed to use unsafe code (C-0010 section 2).

- **Depends on:** nothing else in the solution.
- **Build:** a library with `IsAotCompatible` and an XML documentation file (C-0010 sections 2 and 5). `AllowUnsafeBlocks` and `DisableRuntimeMarshalling` arrive with the first bindings in M1.2.
- **Tests:** `tests/Paneport.Interop.Tests`, which M1.2 creates. Those tests run on macOS against the host's test build, so they stay out of `eng/ci.slnf` (C-0011 section 4).
- **Design:** [solution-and-build.md](../../docs/design/solution-and-build.md) records its role. M1.2 writes its full design together with its first bindings.

It holds no code yet.
