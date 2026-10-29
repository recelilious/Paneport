<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Core

The domain model, the view models, the settings, the compatibility database reader, and the interfaces that platform code implements (C-0004 section 2). It uses no platform API.

- **Depends on:** nothing else in the solution.
- **Build:** a library with `IsAotCompatible`, so the trimming and AOT analyzers check it, and with an XML documentation file, so a public API without a documentation comment fails the build (C-0010 sections 2 and 5).
- **Tests:** [Paneport.Core.Tests](../../tests/Paneport.Core.Tests/README.md).
- **Design:** [solution-and-build.md](../../docs/design/solution-and-build.md) records its role. M0.3 writes its full design in `docs/design/app-shell.md`.

It holds no code yet.
