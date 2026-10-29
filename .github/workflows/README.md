<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Workflows

GitHub Actions workflows (C-0011 section 4). This README lives here and not in `.github/`, because GitHub would show a README in `.github/` on the repository's front page in place of the root README.

## ci.yml

Runs on every push and every pull request.

| Job | Runner | Steps |
|---|---|---|
| `dotnet` | `windows-2025` and `ubuntu-24.04` | Install the SDK that `global.json` pins; restore `eng/ci.slnf` from the lock files; build it in Release; run its tests, with a skipped test counted as a failure; check its formatting. |
| `checks` | `ubuntu-24.04` | Install the REUSE tool that `eng/toolchain.json` pins, and run `reuse lint` on the whole repository. |

- **Pins.** Actions are pinned to a full commit hash, with the release tag in a comment. Runner images are named by version, not `-latest`. The SDK comes from `global.json`, packages from the lock files, and CI tools from `eng/toolchain.json` (C-0009 section 2).
- **Telemetry.** The workflow sets the opt-out variables of the .NET CLI, Microsoft.Testing.Platform and Avalonia's build statistics, so CI runs send none.
- **Permissions.** The workflow can only read the repository, and the checkout keeps no credentials.
- **Still to come.** M0.2 completes `dotnet` (toolchain check, trimmed publish) and `checks` (gitleaks, personal data and forbidden files, lychee, dependency licenses). M1.2 adds `native`, M1.1 adds `ios`, and M9.1 adds `release`.

To add or update an action, confirm its license and register it under C-0003 first, then pin the new release's commit hash. [solution-and-build.md](../../docs/design/solution-and-build.md) describes the jobs and the commands they run.
