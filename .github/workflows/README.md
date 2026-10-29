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
| `dotnet` | `windows-2025` and `ubuntu-24.04` | Install the SDK that `global.json` pins and check it against the pin; restore `eng/ci.slnf` from the lock files, and fail if a lock file was written, because locked mode writes a missing one instead of failing; build it in Release; run its tests, with a skipped test counted as a failure; check its formatting and the formatting of the tools; publish the trimmed test app, which fails on any trimming warning. |
| `checks` | `ubuntu-24.04` | Install the SDK and the CI tools that `eng/toolchain.json` pins, each download checked against its SHA-256; check the SDK and the tools against their pins; run the tools' self-tests; then REUSE, gitleaks over the whole history (failing on a `.gitleaksignore`, which gitleaks would honor), the personal-data and forbidden-file check, lychee (online outside `docs/research/`, and offline for the relative links of the research notes), and the dependency licenses. |

- **Pins.** Actions are pinned to a full commit hash, with the release tag in a comment. Runner images are named by version, not `-latest`. The SDK comes from `global.json`, packages from the lock files, and CI tools from `eng/toolchain.json` (C-0009 section 2).
- **Every failure in one run.** Each step after the setup runs even when an earlier step failed.
- **Telemetry.** The workflow sets the opt-out variables of the .NET CLI, Microsoft.Testing.Platform and Avalonia's build statistics, so CI runs send none.
- **Permissions.** The workflow can only read the repository, and the checkout keeps no credentials. lychee uses the workflow's token for links to github.com.
- **Still to come.** M1.2 adds `native`, M1.1 adds `ios`, M8.3 adds the compatibility data step to `checks`, and M9.1 adds `release`.

To add or update an action, confirm its license and register it under C-0003 first, then map it in `eng/license-map.json` and pin the new release's commit hash. [solution-and-build.md](../../docs/design/solution-and-build.md) describes the build, and [repository-checks.md](../../docs/design/repository-checks.md) every check and step.
