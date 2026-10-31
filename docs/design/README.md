# Design

This folder holds Paneport's design documents.

| File | What it is |
|---|---|
| [development-plan.md](development-plan.md) | How development is split into Large, Medium and Small stages, how a stage session works, the decisions recelilious makes along the way, and the full stage map with its dependencies. |
| [stages/](stages/README.md) | One brief per Large stage, holding the detailed briefs of its Medium and Small stages. Stage sessions keep each stage's status and session log there. |
| [solution-and-build.md](solution-and-build.md) | How the .NET solution is laid out and built: the projects and their dependency rules, build properties, code style, packages and lock files, tests, license headers and CI. The first design record of each .NET project. |
| [repository-checks.md](repository-checks.md) | How the repository rules are checked: the personal-data scanner, the license checker and the toolchain checker in `tools/`, the trimmed test app, the pinned CI tools, and the `dotnet` and `checks` jobs. |
| [app-shell.md](app-shell.md) | The app shell in Paneport.Core, Paneport.UI and Paneport.Desktop: the domain model, the service interfaces and who implements them, the settings, navigation and view matching, threading, the desktop head's fakes and sample titles, and the tests. |
| [compat-db.md](compat-db.md) | The compatibility database core: the title schema mapped to C-0008, the bundled and device layers and their rules, how the layers merge, the launch rule, reading and writing title files, the device layer store, and which rules the code enforces with their tests. |

Component design documents are added here as stages create them (C-0012 section 1). Each one lists the public sources it consulted (C-0001 section 6).
