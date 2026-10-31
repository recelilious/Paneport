# Design

This folder holds Paneport's design documents.

| File | What it is |
|---|---|
| [development-plan.md](development-plan.md) | How development is split into Large, Medium and Small stages, how a stage session works, the decisions recelilious makes along the way, and the full stage map with its dependencies. |
| [stages/](stages/README.md) | One brief per Large stage, holding the detailed briefs of its Medium and Small stages. Stage sessions keep each stage's status and session log there. |
| [solution-and-build.md](solution-and-build.md) | How the .NET solution is laid out and built: the projects and their dependency rules, build properties, code style, packages and lock files, tests, license headers and CI. The first design record of each .NET project. |
| [repository-checks.md](repository-checks.md) | How the repository rules are checked: the personal-data scanner, the license checker and the toolchain checker in `tools/`, the trimmed test app, the pinned CI tools, and the `dotnet` and `checks` jobs. |
| [app-shell.md](app-shell.md) | The app shell in Paneport.Core, Paneport.UI and Paneport.Desktop: the domain model, the service interfaces and who implements them, the settings, navigation and view matching, threading, the desktop head's fakes and sample titles, and the tests. |
| [dev-environment.md](dev-environment.md) | Where each part is built and how a machine is set up for it: the Windows workstation, a local Mac with `eng/mac/bootstrap.sh`, the `ios` CI job that stands in for the Mac, and the iPhone. The toolchain pins of the iOS build and why they were chosen. |

Component design documents are added here as stages create them (C-0012 section 1). Each one lists the public sources it consulted (C-0001 section 6).
