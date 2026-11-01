<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Contributing

Start with the [contracts index](docs/contracts/README.md).

- **Commits:** follow [C-0002](docs/contracts/C-0002-repository-and-commits.md).
- **Licensing:** follow [C-0003](docs/contracts/C-0003-licensing.md). Register every new dependency before adding it.
- **Originality:** follow section 6 of [C-0001](docs/contracts/C-0001-project-charter.md). Never copy code from existing products.

The commit template is `.gitmessage` in the repository root. Enable it with:

```bash
git config commit.template .gitmessage
```

## Before a push

CI runs only after a push, so run the personal-data, secrets and format checks locally first (C-0011 section 2). Run them from the repository root, with the SDK that `global.json` names.

The personal-data and forbidden-file check:

```bash
dotnet run tools/ScanPersonalData.cs
```

The secrets check, with the gitleaks release that `eng/toolchain.json` pins, once it is installed:

```bash
gitleaks git --config eng/gitleaks.toml --redact --ignore-gitleaks-allow .
```

The format checks, of the solution and of the tools:

```bash
dotnet format eng/ci.slnf --verify-no-changes
```

```bash
dotnet format whitespace tools --folder --verify-no-changes
```

Paneport.iOS needs the iOS workload for the full format check, which the `ios` job runs. Without the workload, check its whitespace:

```bash
dotnet format whitespace src/Paneport.iOS --folder --verify-no-changes
```

Build and test as the [quick start](README.md#quick-start) shows. [repository-checks.md](docs/design/repository-checks.md) section 8 lists every check with its local command, among them the dependency licenses, the toolchain pins and the trimmed publish. REUSE and lychee run in the `checks` job of CI. The `ios` job, which builds the iOS app on a macOS runner, runs only for a pull request with the `ios-ci` label: add it once the pull request is otherwise ready to merge ([dev-environment.md](docs/design/dev-environment.md) section 5).

## Tests

- Tests use hand-written fakes, never a mocking library (C-0011 section 1).
- `tests/` is scanned for personal data. A test therefore never holds a literal value that the personal-data check or gitleaks looks for, such as an e-mail address, an IP address, a token, a Steam ID or a local path; it builds such values in memory.

## Packages

Register a new package and everything it brings in under C-0003 first. Then add its version to `Directory.Packages.props`, map it in `eng/license-map.json`, and update the lock files with `dotnet restore eng/ci.slnf --force-evaluate`, because a plain restore keeps stale entries. [solution-and-build.md](docs/design/solution-and-build.md) section 5 describes the steps.
