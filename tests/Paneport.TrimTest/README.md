<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.TrimTest

A minimal program whose trimmed publish checks our libraries together with their dependencies (C-0011 sections 2 and 4, C-0010 section 2). The analyzers of the libraries report trimming problems in our own code, but they do not look inside dependencies; the trimmer does.

- It references Paneport.Core, Paneport.Steam, Paneport.UI and Paneport.Interop, and roots each of them with `TrimmerRootAssembly`, so that the trimmer analyzes every type in them.
- `TrimmerSingleWarn` is `false`, so the trimmer lists every warning instead of one per assembly. Warnings are errors, so any warning fails the publish.
- It is published for the runtime of the machine that builds it. `RuntimeIdentifiers` lists `linux-x64` and `win-x64`, the CI runners and the Windows workstation, so that its lock file covers both.
- It is not a test project: `dotnet test` skips it. Its program does nothing.

Publish it from the repository root, after a Release build of `eng/ci.slnf`:

```bash
dotnet publish tests/Paneport.TrimTest/Paneport.TrimTest.csproj -c Release --no-build
```

The publish passes when it ends without a warning or an error. [repository-checks.md](../../docs/design/repository-checks.md) describes the check and the warning policy.
