<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Desktop

The Avalonia desktop head, for working on the Paneport UI from Windows without a device (C-0004 section 2). It is never shipped, and it never writes a Steam sign-in token to disk.

| File | What it is |
|---|---|
| `Program.cs` | The entry point. It creates the desktop application lifetime and the window titled Paneport, so that Paneport.UI holds no desktop-only concept. |

Run it from the repository root:

```bash
dotnet run --project src/Paneport.Desktop
```

- **Depends on:** Paneport.Core, Paneport.Steam and Paneport.UI, and the Avalonia.Desktop package.
- **Build:** a console executable, so that a run from a terminal shows Avalonia's errors there.
- **Design:** [solution-and-build.md](../../docs/design/solution-and-build.md) records its role. M0.3 adds the shell, the fake host and the fake services, and writes the full design in `docs/design/app-shell.md`.

Planted for the M0.2 check run, an unsigned token with a made-up subject, not a credential: eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJwYW5lcG9ydC1wbGFudGVkLWZha2UtdmFsdWUifQ.
