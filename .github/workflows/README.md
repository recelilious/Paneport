<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Workflows

GitHub Actions workflows (C-0011 section 4). This README lives here and not in `.github/`, because GitHub would show a README in `.github/` on the repository's front page in place of the root README.

## ci.yml

Runs on every push and every pull request. The `ios` job runs for every pull request, on every push to one, and for every push to `main` (D7 in plan section 4.2). A push to another branch skips it, because the `pull_request` run of the same commit builds the merge result.

| Job | Runner | Steps |
|---|---|---|
| `dotnet` | `windows-2025` and `ubuntu-24.04` | Install the SDK that `global.json` pins and check it against the pin; restore `eng/ci.slnf` from the lock files, and fail if a lock file was written, because locked mode writes a missing one instead of failing; build it in Release; run its tests, with a skipped test counted as a failure; check its formatting and the formatting of the tools; publish the trimmed test app, which fails on any trimming warning. |
| `checks` | `ubuntu-24.04` | Install the SDK and the CI tools that `eng/toolchain.json` pins, each download checked against its SHA-256; check the SDK and the tools against their pins; run the tools' self-tests; then REUSE, gitleaks over the whole history (failing on a `.gitleaksignore`, which gitleaks would honor), the personal-data and forbidden-file check, lychee (online outside `docs/research/`, and offline for the relative links of the research notes), and the dependency licenses. |
| `ios` | `xcode-27`, the image that `eng/toolchain.json` pins | Install the SDK that `global.json` pins; select the Xcode that `eng/toolchain.json` pins, through `DEVELOPER_DIR`, and refuse one that is a release candidate or a beta; run `eng/mac/bootstrap.sh`, which checks the SDK and Xcode against their pins, installs the iOS workload from the pinned workload set unless it is there, and checks it; run the bootstrap again, which must skip the install; print the SDK, workload set and Xcode versions; run the bootstrap against another Xcode on the image, which must fail and name both versions; restore Paneport.iOS from its lock file, and fail, printing the file, if a lock file was written; build it for `ios-arm64` in Release without signing, and check its formatting; check that the app names its scene delegate, supports iOS 26.0, holds arm64 code only and carries no signature beyond the linker's and no profile; then, only if every step before passed, upload it as a zip named by the commit. |

- **Pins.** Actions are pinned to a full commit hash, with the release tag in a comment. Runner images are named by version, not `-latest`. The SDK comes from `global.json`, packages from the lock files, and the CI tools, Xcode, the iOS workload set and the macOS image from `eng/toolchain.json` (C-0009 section 2); the toolchain check fails when a job runs on another macOS image than the pinned one, and when it cannot read a job's runner in the spellings that [repository-checks.md](../../docs/design/repository-checks.md) section 5 lists.
- **Every failure in one run.** Each step after the setup runs even when an earlier step failed.
- **Telemetry.** The workflow sets the opt-out variables of the .NET CLI, Microsoft.Testing.Platform and Avalonia's build statistics, so CI runs send none.
- **Permissions.** The workflow can only read the repository, and the checkout keeps no credentials. lychee uses the workflow's token for links to github.com.
- **The ios job.** It runs for every pull request, so its result on the last commit is there before a merge, as C-0011 section 3 requires, and for every push to `main`, which catches two pull requests that pass alone and break together, and commits made directly on `main`. A newer push cancels its earlier run for the same pull request; each push to `main` gets a run of its own. A push to another branch without a pull request is not built for iOS.
- **Still to come.** M1.2 adds `native` and builds the native host in `ios`, M1.3 signs and packages the app in `ios`, M8.3 adds the compatibility data step to `checks`, and M9.1 adds `release`.

To add or update an action, confirm its license and register it under C-0003 first, then map it in `eng/license-map.json` and pin the new release's commit hash. [solution-and-build.md](../../docs/design/solution-and-build.md) describes the build, and [repository-checks.md](../../docs/design/repository-checks.md) every check and step.
