# Repository Checks

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T18:00Z |

Back to the [design index](README.md).

This document records how the repository rules of C-0011 section 2 are checked: the three file-based tools in `tools/`, the trimmed test app, the pinned CI tools and their configuration, and the `dotnet` and `checks` jobs. Stage M0.2 wrote it (see the [L0 brief](stages/L0-foundation.md)). It builds on [solution-and-build.md](solution-and-build.md). Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Checks and where they run

| Check (C-0011 section 2) | Tool | CI job and step | Configuration |
|---|---|---|---|
| C# formatting | `dotnet format` | `dotnet`: Check formatting; Check the formatting of the tools | `.editorconfig` |
| C# analysis | The .NET analyzers, with warnings as errors | `dotnet`: Build | `Directory.Build.props`, `.editorconfig` |
| Trimming of dependencies | `tests/Paneport.TrimTest` | `dotnet`: Publish the trimmed test app | Its project file |
| License headers and license texts | REUSE | `checks`: REUSE | `REUSE.toml` |
| Secrets | gitleaks | `checks`: gitleaks | `eng/gitleaks.toml` |
| Personal data and forbidden files | `tools/ScanPersonalData.cs` | `checks`: personal data and forbidden files | `eng/personal-data-allow-list.json` |
| Links in Markdown | lychee | `checks`: lychee | `eng/lychee.toml` |
| Dependency licenses | `tools/CheckLicenses.cs` | `checks`: dependency licenses | `eng/license-map.json` |
| Toolchain pins (C-0009 section 2) | `tools/CheckToolchain.cs` | `dotnet`: the SDK; `checks`: the SDK and the CI tools | `global.json`, `eng/toolchain.json` |

The native checks (`clang-format`, `clang-tidy`) come with the `native` job in M1.2, and the compatibility data check with M8.3.

## 2. The file-based tools

The three tools are C# file-based apps, run from the repository root with `dotnet run tools/<Name>.cs` (C-0010 section 1). They share these rules:

- **One file each.** Every type a tool needs lives in its file and is `internal`. The tools use only the base class library and depend on nothing under `src/` (C-0004 section 2).
- **Self-test.** `-- --self-test` runs the tool's parsers and rules against synthetic fixtures and prints one line per check. Fixtures use addresses at `example.com`, IP addresses from the documentation ranges of RFC 5737 and RFC 3849, and made-up identifiers (C-0011 section 1). Values shaped like credentials, such as tokens and keys, are built at run time, so that the source file holds nothing a secret scanner would report.
- **Exit codes.** 0 when everything passes, 1 when the check fails, 2 for a wrong argument or a file the tool cannot read: one that is missing, is not valid JSON, has the wrong shape, or holds a line in a form the tool does not read.
- **Output.** Each problem names its file and, where there is one, its line. Under GitHub Actions (`GITHUB_ACTIONS=true`) the tools also print a workflow command that annotates the file in the pull request. No tool prints a value it matched.
- **Files.** The personal-data scan and the license check read the files that `git ls-files --cached --others --exclude-standard` lists: the tracked files, and new files that git does not ignore, so that a local run before a commit sees them too.

**Confirmed in this stage.**

- File-based apps get `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` and `eng/nuget.config` like any project under the root. Warnings fail their build, and the code-style rules of `.editorconfig` run while they build.
- A file-based app sets `PublishAot` to `true` by default. That adds the AOT compiler package and a runtime-specific section to its lock file, so the lock file written on Windows would fail the locked restore on Ubuntu. Each tool therefore starts with `#:property PublishAot=false`; the tools are never published.
- The three tools share one lock file, `tools/packages.lock.json`, because NuGet keeps a project's lock file in its folder, and every file-based app in `tools/` is a project in that folder. It lists no package. In locked mode, a missing lock file is created rather than reported, so the committed one keeps every run the same. NuGet names the project of `tools/<Tool>.cs` `<Tool>.cs`, and uses `tools/packages.<Tool>.cs.lock.json` instead of the shared file once that file exists. A tool that ever needs a package gets such a lock file of its own; the license checker requires it and rejects `NuGetLockFilePath` (section 4).
- `dotnet format` does not accept a file-based app. CI runs `dotnet format whitespace tools --folder --verify-no-changes` for the tools, and their build enforces the code-style rules. IDE0003 and IDE0049, which the build skips (see section 4 of solution-and-build.md), are not checked for the tools.
- The compiler accepts `#:` directives before the first token of a file: after blank lines, comments and preprocessor lines such as `#nullable` or `#pragma`, but not after `#if`. The license checker reads them the same way, so text further down, such as its own fixtures, is not mistaken for a directive.

## 3. Personal data and forbidden files

`tools/ScanPersonalData.cs` implements the personal-data and forbidden-file check of C-0011 section 2.

- **Forbidden files.** Every file in the repository is checked by name. `forbidden-extension` fails on the file types of signing material, pairing files and packaged apps that C-0002 section 6 and `.gitignore` list: `.p12`, `.cer`, `.mobileprovision`, `.provisionprofile`, `.mobiledevicepairing` and `.ipa`, and `.pfx`, which holds the same PKCS #12 data as `.p12`, in any letter case. `forbidden-build-output` fails on a file inside a `bin/`, `obj/` or `DerivedData/` folder, the build output that the same section lists. Neither can be allowed. The rule is checked by its self-test only, so that no forbidden file is ever committed, not even to a scratch branch.
- **Personal data.** In `docs/evidence/`, `compat/` and `tests/`, the name of every file and folder goes through the rules, and so does every text file, line by line. A folder that does not exist yet is skipped and named in the output.
- **Encodings.** Text is read as UTF-8, as UTF-16 or UTF-32 when a byte-order mark says so, as Windows PowerShell 5.1 writes its logs, and as UTF-16 without a mark when one byte of most byte pairs is NUL. Any other file with a NUL byte in its first 8,000 bytes counts as binary: it is listed for a review by eye and not scanned, which matters for screenshots in evidence records.
- **Names.** A finding in a file or folder name never prints the name: it names the folder above it, followed by `<name not shown>`. So do the findings in a file below such a name, and a forbidden file with such a name. These findings cannot be excused; the file or folder is renamed instead.
- **Property lists.** A property list puts a value on the line after its key, or in an array below it, as `TeamIdentifier` and `ApplicationIdentifierPrefix` are in a provisioning profile. The scanner also reads each key with its value, or with each value of its array, as one `key: value` line, and a key and its value on one line the same way, so that labeled rules see the value under its key.
- **Labels.** A label counts at the start of a word, after an underscore or a digit, and at a capital after a lowercase letter or a digit, so that `STEAM_API_KEY`, `steamApiKey`, `IOPlatformSerialNumber` and `deviceUDID` hold labels. A short note in parentheses may stand between a label and its value, as in `Serial Number (system):`.

### 3.1 Rules

Exact formats were checked against public documentation (section 11). Device model identifiers, such as `iPhone18,1`, are allowed and match no rule (C-0011 section 2).

| Rule | Matches | Known false positives, and how they are kept out |
|---|---|---|
| `udid` | A UDID of a device from 2018 on: 8 hex digits (the chip ID), a hyphen and 16 hex digits (the ECID), also when a hyphen joins it to other text, as in `syslog-<UDID>.txt`. An older 40-digit UDID only after a label such as `UDID`, `UniqueDeviceID` or `deviceUDID`. | Commit hashes have the 40-digit shape; without a label they pass. GUIDs have a different grouping. |
| `ecid` | An ECID (a 64-bit chip identifier) after the label `ECID`, `ApECID`, `UniqueChipID` or `unique chip ID`, in hex or decimal. | Prose that names ECIDs: the label must be followed by a value. |
| `serial-number` | 8 to 14 capital letters and digits, with at least one of each, after a label such as `serial`, `serial number`, `SerialNumber`, `IOPlatformSerialNumber` or `serial no.`. Older serial numbers have 11 or 12 characters, randomized ones 8 to 14. | Prose and placeholders such as `REDACTED`, which hold no digit, and names such as `JsonSerializer`, where `serial` ends no word. |
| `team-identifier` | 10 capital letters and digits, with at least one of each, after `team ID`, `TeamIdentifier`, `DEVELOPMENT_TEAM` or `com.apple.developer.team-identifier`, also in a property list, as a value or in an array. | Placeholders. A team identifier without any digit is not caught. |
| `app-identifier-prefix` | A 10-character team identifier in front of a bundle identifier (`ABCDE12345.com.example.app`, as in `application-identifier`), or appended to one (`com.example.app.ABCDE12345`), as sideloading tools re-sign apps; also after the label `application-identifier`, `AppIdentifierPrefix` or `ApplicationIdentifierPrefix`. | Plain bundle identifiers and C# namespaces, which have no 10-character part of capitals and digits. |
| `email` | Any e-mail address, `example.com` included. | SSH remotes such as `git@host:owner/repo`, image names such as `screen@2x.png`, and `package@1.2.3`, whose last part is not a domain. |
| `steam-id` | A SteamID64 of an individual account: 76561197960265728 plus the 32-bit account number, so from 76561197960265728 to 76561202255233023. A Steam2 ID (`STEAM_0:1:23`), a Steam3 ID (`[U:1:23]`), and 5 to 20 digits after a label such as `SteamID`, `steam_id`, `SteamID64`, `SteamID32` or `Steam account ID`. | Steam app IDs and build IDs, whose labels differ (`steamAppId`, `gameBuild`), and 17-digit numbers outside the individual range. |
| `ip-address` | An IPv4 address, with every part from 0 to 255, and anything that parses as an IPv6 address. Every range counts, the documentation ranges and loopback included; tests build such values in memory. | Four-part version numbers: they pass after a version label (`version`, `Version=`, `build`, `"resolved":`, `"requested":`, `Version.Parse("`), on a dependency line of a NuGet lock file, after a `v`, or as part of a longer dotted number. A four-part version without such a label, such as `(8.3.1.3)` after a package name, is still flagged; write it with a label. Times of day and other colon-separated hex do not parse as IPv6. |
| `token` | A JSON Web Token, whose first two parts are base64url JSON that starts with a brace: `eyJ` for `{"`, and `eyA` or `ew` when a space or a line break follows the brace, as in Steam's access and refresh tokens. `Bearer` followed by 16 or more token characters. 16 or more token characters, with letters and digits, after a colon, an equals sign or a space that follow a label such as `access_token`, `refresh_token`, `--refresh-token`, `auth_token`, `session_key`, `id_token`, `api_key`, `STEAM_API_KEY`, `client_secret`, `guard_data` or `NewGuardData`. `access_token=`, `token=` or `key=` with such a value in a web address. | Placeholders (`<your token>`, `REDACTED`) and content hashes in lock files, which carry no such label, and a method call after a label, as in `authKey = Convert.ToBase64String(bytes)`. |
| `depot-key` | 64 hex digits (a 32-byte key) after the label `depot key` or `decryption key`, and the `depotId;key` lines of a depot key file. | SHA-256 values, such as the `exeSha256` values of compat files (C-0008 section 2), which carry neither label. |
| `manifest-request-code` | 5 to 20 digits (the code is a 64-bit number) after `manifest request code` or `request code`. | Prose: the label must be followed by the number. |
| `local-path` | A Windows user profile on any drive (`C:\Users\name`, also with the backslashes doubled or doubled twice, as in a C# string or JSON, or with forward slashes), and as Git Bash, WSL and Cygwin show it (`/c/Users/name`, `/mnt/c/Users/name`, `/cygdrive/c/Users/name`). `/Users/name` on macOS, also below `/System/Volumes/Data` or `/Volumes/<disk>`. `/home/name` on Linux, also below `/var`. `/var/mobile/` and `/var/folders/` on iOS and macOS, with or without `/private`. A network share (`\\host\share`, `\\wsl$\Ubuntu`). Any of these inside a `file:` address, or with escaped slashes (`\/Users\/name`) as JSON writers may write them. | The shared profiles that identify nobody (`Public`, `Default`, `/Users/Shared`), placeholders for the user name (`%USERNAME%`, `$USER`, `{user}`, `<name>`), relative paths, paths inside web addresses, and a drive path or a regular expression in a C# string (`"C:\\Windows"`, `"\\bword\\b"`), which is no share. The profiles of CI runners (`/home/runner`) and of a Wine prefix (`C:\users\steamuser`) are local paths too and are flagged; a test builds such a path in memory. |

The rules cannot find a device name, a real name, a Steam account name or an Apple ID that is not an e-mail address, because none of them has a shape. C-0012 section 4 still forbids them in evidence records, so review catches them. The same holds for the contents of binary files.

### 3.2 Allow list

`eng/personal-data-allow-list.json` holds the exceptions, in an `entries` array. Each entry has a `path`, a `rule` and a `reason`, and excuses that one content rule in that one file; there is no blanket exception. The scan fails on an entry without a reason, on a rule that does not exist, on a forbidden-file rule, and on an entry that no longer excuses anything, so that the list cannot rot. A finding whose name is not shown (section 3) is never excused. The list is empty.

### 3.3 Leaks into logs

While the repository is public, CI logs are public too. The scanner prints the file, the line, the rule and its description, never the value or the line's text, and never a file or folder name that holds such a value. Its self-test checks that no report line contains a matched value or such a name.

## 4. Dependency licenses

`tools/CheckLicenses.cs` implements the dependency license check of C-0011 section 2. It fails on anything that the register in C-0003 section 6 does not cover.

**Inputs.**

| Input | What counts | Named by |
|---|---|---|
| Every `packages.lock.json`, and every `packages.<project>.lock.json`, which NuGet uses in its place when it exists | Direct, transitive and centrally pinned packages of every target and runtime; the solution's own projects are skipped | Package ID |
| The file-based tools in `tools/` | `#:package` and `#:sdk` directives before the file's first token, where the compiler accepts them (section 2) | Package ID |
| `.gitmodules`, once it exists (M4.1) | Each submodule, whatever the letter case of its keys | Its path, for example `external/Paneport-wine` |
| `eng/toolchain.json` | Each pinned tool | Its key, for example `gitleaks` |
| The workflows in `.github/workflows/`, every other YAML file under `.github/`, and every `action.yml` or `action.yaml`, which is where a local action keeps its steps | Each `uses:` line, because actions are CI tools under C-0003 section 5, and the image of each job container and service (`container:`, `image:`) | `owner/repo` for `owner/repo/path@ref`; `docker://image` without its tag, for `uses: docker://` and a container or service image alike. A local action (`./path`) is our own code; its `action.yml` is read like a workflow. A `Dockerfile` or `./path` image is built from our own files. |

**Lines it cannot read.** A `uses:`, `container:` or `image:` line in any other form, such as a step written as a flow mapping (`- { uses: ... }`), a value on the next line, or an image named by an expression (`${{ }}`), stops the checker with exit code 2, naming the file and line, so that no step escapes it.

**Packages it could not see.** The checker reads packages from lock files, so it fails on every way to use a package that it would not see there:

- a project (`*.csproj`) without a lock file next to it, `packages.lock.json` or `packages.<project>.lock.json`; in locked mode NuGet writes a missing lock file instead of failing, so `dotnet` also fails when the restore writes one (section 6);
- a tool in `tools/` with `#:package` or `#:sdk` directives and no lock file of its own (section 2);
- `NuGetLockFilePath`, in a project file or a `#:property` directive, because the checker does not follow it;
- MSBuild SDKs with a version (`Sdk="Name/1.0.0"`, or `<Sdk>` or `<Import>` with a `Version`), `PackageDownload` items and the `msbuild-sdks` of `global.json`, which NuGet resolves without a lock file;
- a local tool manifest (`dotnet-tools.json`).

Each needs the checker taught to read it, in a reviewed change, before it is used.

**The register.** The checker finds the heading `## 6. Component register` in C-0003 and reads its table, which must have exactly the columns Component, Purpose, License and Category. A category cell starts with one of Allowed, Conditional, Forbidden, Under review or Tool, and may go on after a comma, semicolon or colon ("Allowed, under FTL"). If the heading, the columns or a category change, the checker fails with a message that names the change, rather than guessing.

**Mapping policy.** `eng/license-map.json` is the reviewed mapping from each package ID, tool, action and submodule to the register row that covers it, by the row's Component text.

- Every name is listed on its own; there are no wildcards, so a new package always needs a reviewed entry, even when its name starts like a registered one.
- Package IDs compare without regard to case, as NuGet compares them. Versions are not part of the mapping, because the register does not record them.
- Which rows cover what follows C-0003 section 5. Packages and submodules need an Allowed or Conditional row. Tools and actions need an Allowed, Conditional or Tool row. A Tool row never covers a package in a lock file or a submodule. A Forbidden or Under review row covers nothing.
- An entry that matches nothing in the repository fails the check, so the map always describes the repository as it is.
- The packages from M0.1 are mapped as section 5 of solution-and-build.md lists them.

**Adding a dependency.** Register it under C-0003 first (a contract change that recelilious approves), then add the package, tool, action or submodule, then map it in `eng/license-map.json`, then run `dotnet run tools/CheckLicenses.cs`. The pull request lists every new name with its row.

**Not covered.** The tools that the runner image provides (Bash, curl, jq, pipx, tar) are part of the pinned image, not of the repository. The Python packages that pipx installs together with the REUSE tool are not pinned (section 10). A submodule commit without a `.gitmodules` entry is not read; git cannot clone such a submodule. A line of a `run:` script that looks like a `uses:` line is read as one.

## 5. Toolchain pins

`tools/CheckToolchain.cs` compares the installed toolchain with its pins (C-0009 section 2).

- **SDK.** It reads `sdk.version` and `sdk.rollForward` from `global.json`, runs `dotnet --version` at the repository root, and fails unless the two versions are equal and `rollForward` is `disable`.
- **CI tools.** It reads `eng/toolchain.json` on every run, so that a local run finds a broken pin file too. With `--ci-tools` it also asks each pinned tool for its version and compares the answer with the pin: `reuse --version`, `gitleaks version` and `lychee --version`. A tool that is pinned but has no version check in the tool fails the check, so no pin goes unchecked. M1.1 adds the Xcode check against `xcodebuild -version`.
- **Pin file.** The file holds only `tools`. Each tool under it has `version` and `source`, an https address. A tool that CI downloads also has `downloads`, keyed by runtime identifier, each with exactly the download's `url` and its `sha256`. The checker rejects any other key or field, a short or upper-case SHA-256 and a plain http address, because both checkers read only `tools`, and a pin elsewhere would escape them. The Xcode and runner-image pins of C-0009 section 2 come with M1.1, which teaches both checkers to read them.

| Tool | Version | Pinned in | Installed from | Downloads pinned |
|---|---|---|---|---|
| .NET SDK | 10.0.403 | `global.json` | `actions/setup-dotnet` in CI | None |
| REUSE tool | 6.2.0 | `eng/toolchain.json` | PyPI, through pipx | None |
| gitleaks | 8.30.1 | `eng/toolchain.json` | Its GitHub release | `linux-x64` (CI) and `win-x64` (workstation) |
| lychee | 0.24.2 | `eng/toolchain.json` | Its GitHub release `lychee-v0.24.2` | `linux-x64` (CI) and `win-x64` (workstation) |

gitleaks 8.30.1, released on 2026-03-21, and lychee 0.24.2, released on 2026-05-01, were the newest releases on 2026-10-29. Their licenses (MIT, and MIT OR Apache-2.0) match their register rows.

**Changing a pin.** A pin changes in a reviewed change of its own (C-0009 section 2).

1. A new tool is registered under C-0003 first and mapped in `eng/license-map.json`.
2. Update `version`, `source` and every download's `url` and `sha256`. Take each SHA-256 from two independent places and check that they agree: GitHub's digest of the release asset (`gh api repos/<owner>/<repo>/releases/tags/<tag>`, field `digest`) and the checksum file that the project publishes with the release (`gitleaks_<version>_checksums.txt`; lychee's `<asset>.sha256`). The values above were taken that way.
3. If the tool's version output changed, update its version check in `tools/CheckToolchain.cs` and its self-test.
4. Push, and confirm that `checks` installs the tool and that the toolchain step passes.

## 6. The CI jobs

`.github/workflows/ci.yml` runs on every push and every pull request (C-0011 section 4). Actions are pinned to full commit hashes, runner images to explicit versions, and the workflow's token can only read the repository. In both jobs, every step after the setup runs even when an earlier step failed (`if: ${{ !cancelled() }}`), so that one run shows every failure.

**`dotnet`**, on `windows-2025` and `ubuntu-24.04`:

1. Check out, and install the SDK that `global.json` pins.
2. Check the SDK against `global.json` (`dotnet run tools/CheckToolchain.cs`).
3. Restore `eng/ci.slnf` from the lock files, as M0.1 set it up.
4. Check that no lock file was written: in locked mode, NuGet writes a missing lock file instead of failing, so `git status` must show no new or changed `*.lock.json` after the restore and the tool's run.
5. Build `eng/ci.slnf` in Release, run its tests, and check its formatting, as M0.1 set them up.
6. Check the formatting of the tools.
7. Publish the trimmed test app (section 7).

**`checks`**, on `ubuntu-24.04`:

1. Check out the whole history, which gitleaks scans, and install the SDK that `global.json` pins, which runs the tools.
2. Install the CI tools that `eng/toolchain.json` pins: REUSE with pipx; gitleaks and lychee from their GitHub releases, each download checked against its pinned SHA-256 before it is unpacked.
3. Self-test the toolchain check, then check the SDK and the CI tools against their pins.
4. Self-test the personal-data scanner and the license checker.
5. REUSE: `reuse lint`.
6. gitleaks over the whole history: `gitleaks git --config eng/gitleaks.toml --redact --ignore-gitleaks-allow`, with a JSON report. The step fails first if a `.gitleaksignore` exists.
7. The personal-data and forbidden-file check.
8. lychee, online, on every Markdown file outside `docs/research/`: `lychee --config eng/lychee.toml --no-progress .`.
9. lychee, offline, on the relative links of `docs/research/`: `lychee --offline --no-progress --extensions md docs/research`.
10. The dependency license check.

**gitleaks.** It runs from the pinned release, not as the gitleaks GitHub Action (C-0011 section 2). `--redact` replaces every secret in its output and report, which is the option the L0 brief asked to confirm. `--ignore-gitleaks-allow` ignores inline `gitleaks:allow` comments, so that every exception lives in `eng/gitleaks.toml`, which extends gitleaks' default rules and adds nothing yet. gitleaks also skips every finding that a `.gitleaksignore` file lists: version 8.30.1 loads that file from the scanned folder whatever its options say, as its `cmd/root.go` shows (section 11). The step therefore fails when the repository has one, which would be a new root file under C-0004 section 3 anyway. The step prints each finding's file, line, rule and commit from the JSON report, and an annotation, because gitleaks' own verbose output would also print the author and e-mail address of each commit. A false positive goes into `eng/gitleaks.toml` with its reason, as narrowly as possible; a real secret is reported to recelilious at once and is never quoted.

**lychee.** The online run checks every Markdown file that git does not ignore outside `docs/research/`, including `.github/workflows/README.md` in a hidden folder, but not the `.git` folder. It retries a failed request up to three times, and reports a request that still times out without failing on it. It uses the workflow's token, through `GITHUB_TOKEN`, for links to github.com, which GitHub rate-limits for anonymous requests; the token can only read this repository and expires with the job.

**Link-check policy.** The first full run, on 2026-10-29, checked 1,169 unique links. Every file outside `docs/research/` passed. All 70 failures were external sources of the research notes: 38 sites that turn automated clients away (403), 14 pages that are gone (404 and 410), 3 rate limits (429), and 15 server errors, network errors and timeouts. The research notes are a published snapshot (`docs/research/README.md`), so such failures come and go without any change to the repository. Following the fallback of the L0 brief, CI therefore checks the research notes narrowly: their relative links are checked offline, and their external links are not checked. Everything else, the contracts, the design documents and the READMEs, is checked online. The second run then failed only on one timeout, from a slow server behind a link in C-0003 that had passed the first run; a timeout is therefore reported but does not fail the check, while a page that is gone or a failed connection still does. recelilious agrees this policy before the stage merges. The other fallback of the brief, accepting more status codes, would still fail on the 14 dead pages and on every page that dies later.

## 7. The trimmed test app

`tests/Paneport.TrimTest` is a program that does nothing. It references Paneport.Core, Paneport.Steam, Paneport.UI and Paneport.Interop, and roots each with `TrimmerRootAssembly`, so that the trimmer analyzes every type in them together with the dependencies they use; the libraries' own analyzers do not look inside dependencies (C-0010 section 2). `TrimmerSingleWarn` is `false`, so every warning is listed instead of one per assembly, and the trimmer is called with `--singlewarn-` for every assembly, as a diagnostic build log shows.

- **Runtime identifiers.** A trimmed publish is self-contained and needs runtime-specific assets. A runtime identifier given on the command line, as `dotnet publish -r` or `--use-current-runtime` would give it, also reaches the libraries; their lock files hold no runtime, so their locked restore fails (NU1004). The app therefore sets `RuntimeIdentifier` itself, to the SDK's own runtime identifier, and lists `linux-x64` and `win-x64` in `RuntimeIdentifiers`, so that its lock file covers both CI runners and the workstation and is the same everywhere. A machine with another runtime adds its identifier there in a reviewed change.
- **Build and test.** The app is part of `eng/ci.slnf` and `Paneport.slnx`. `dotnet test` skips it, because it is not a test project. CI publishes it with `--no-build` after the Release build.
- **Warnings.** Warnings are errors, so any trimming warning fails the publish. On 2026-10-29 the publish reported none, on the workstation and on both CI runners, with Avalonia 12.1.4 and the libraries as M0.1 left them. Only the Avalonia code that the libraries reach went through the trimmer: Avalonia and Avalonia.Themes.Fluent, as Paneport.UI references them, with their own dependencies. Avalonia.Skia, SkiaSharp, HarfBuzzSharp and the platform backends are first trimmed together with an app head, in M1.1. If a dependency ever brings a warning, for example when M0.3 adds CommunityToolkit.Mvvm, the session lists the warning in its pull request and asks recelilious whether the publish fails on it or an approved list under `eng/` excuses it. Code suppressions stay limited to Paneport.Steam (C-0010 section 2).

## 8. Running the checks locally

From the repository root, with the SDK that `global.json` pins. Setting `DOTNET_CLI_TELEMETRY_OPTOUT`, `TESTINGPLATFORM_TELEMETRY_OPTOUT` and `AVALONIA_TELEMETRY_OPTOUT` to `1` keeps builds from sending usage data (solution-and-build.md section 7).

| Check | Command |
|---|---|
| Self-tests | `dotnet run tools/ScanPersonalData.cs -- --self-test`, and the same for `CheckLicenses.cs` and `CheckToolchain.cs` |
| Personal data and forbidden files | `dotnet run tools/ScanPersonalData.cs` |
| Dependency licenses | `dotnet run tools/CheckLicenses.cs` |
| SDK pin | `dotnet run tools/CheckToolchain.cs` |
| CI tool pins | `dotnet run tools/CheckToolchain.cs -- --ci-tools`, where the CI tools are installed |
| Formatting | `dotnet format eng/ci.slnf --verify-no-changes` and `dotnet format whitespace tools --folder --verify-no-changes` |
| Trimmed publish | `dotnet build eng/ci.slnf -c Release`, then `dotnet publish tests/Paneport.TrimTest/Paneport.TrimTest.csproj -c Release --no-build` |
| Secrets | `gitleaks git --config eng/gitleaks.toml --redact --ignore-gitleaks-allow .`, once the pinned release is installed. The repository has no `.gitleaksignore`, which gitleaks would honor. |
| Links | `lychee --config eng/lychee.toml --no-progress .` and `lychee --offline --no-progress --extensions md docs/research`, once the pinned release is installed. Before a research note is published, `lychee --no-progress <note>` also checks its external sources. |

Before every push, Claude runs the personal-data scan, gitleaks if it is installed, and the format check (C-0011 section 2). gitleaks and lychee are not installed on the workstation yet; a session asks recelilious before downloading either. The REUSE tool is a Python program and runs in CI only (solution-and-build.md section 8).

## 9. Planted failures

Each check is shown to fail once, on a draft pull request from a scratch branch that is closed without merging and whose branch is then deleted. The planted values are synthetic, because the pull request stays visible on GitHub after it is closed.

| Planted change | Step that must fail |
|---|---|
| A line with an e-mail address at `example.com` in `tests/README.md` | Personal data and forbidden files |
| An address from an RFC 5737 range in a test source file | Personal data and forbidden files |
| An entry for an unregistered tool in `eng/toolchain.json` | Dependency licenses; the toolchain step fails as well, because the tool has no version check |
| A `.cs` file without the SPDX lines | REUSE |
| A broken relative link in a README | lychee, online run |
| An unsigned JSON Web Token with `"alg":"none"` and a made-up subject, a format that gitleaks' default rules detect | gitleaks |
| A misformatted line in a `.cs` file | Format check in `dotnet` |
| A reflection call in Paneport.Core that the trimming analyzers reject | Build in `dotnet` |

A run passes when each planted change fails its step and that step's output names the file. The planted build error also fails the test step and the trimmed publish, which need the build. The forbidden-file rule is checked by its self-test only. The pull request of the stage links the run.

## 10. Open items

- **Link-check policy.** The narrower check of `docs/research/` (section 6) needs recelilious's agreement before the stage merges. The L0 brief's session log records the outcome.
- **Unpinned Python packages.** pipx installs the REUSE tool at its pinned version, but the Python packages it depends on are resolved at install time.
- **Third-party binaries.** C-0002 section 6 forbids any file from Microsoft, Valve or a game, but names no file type for them, so the scanner cannot tell such a file from our own. A rule for `.dll` and `.exe` files would change the list of checks in C-0011 and goes through the contract process.
- **Credential and pairing files by name.** `.gitignore` also keeps out `pairing*.plist`, `.env` and `.env.*`. They are names, not file types, so the forbidden-file rule, which C-0011 section 2 defines by extension, does not check them, and a file added with `git add -f` passes it; gitleaks still scans its content. A rule for such names also goes through the contract process.
- **Style rules of the tools.** IDE0003 and IDE0049 are not checked for the tools (section 2).

## 11. Sources

Public material consulted (C-0001 section 6):

- File-based apps: <https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps>
- `dotnet run`: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-run>
- `dotnet format`: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-format>
- Trimming options, including `TrimmerSingleWarn` and `TrimmerRootAssembly`: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options>
- Fixing trim warnings: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/fixing-warnings>
- Runtime identifiers: <https://learn.microsoft.com/en-us/dotnet/core/rid-catalog>
- NuGet lock files and locked mode: <https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files>
- Regular expression source generators: <https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-source-generators>
- gitleaks, its usage and configuration: <https://github.com/gitleaks/gitleaks>
- gitleaks 8.30.1, how it loads `.gitleaksignore`: <https://github.com/gitleaks/gitleaks/blob/v8.30.1/cmd/root.go>
- gitleaks' default rules: <https://github.com/gitleaks/gitleaks/blob/master/config/gitleaks.toml>
- gitleaks releases: <https://github.com/gitleaks/gitleaks/releases>
- lychee: <https://github.com/lycheeverse/lychee>
- lychee's example configuration: <https://github.com/lycheeverse/lychee/blob/master/lychee.example.toml>
- lychee releases: <https://github.com/lycheeverse/lychee/releases>
- REUSE tool, its version output: <https://github.com/fsfe/reuse-tool>
- GitHub Actions workflow commands, for annotations: <https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands>
- GitHub Actions status functions, such as `cancelled()`: <https://docs.github.com/en/actions/reference/workflows-and-actions/expressions>
- The `GITHUB_TOKEN` and its permissions: <https://docs.github.com/en/actions/concepts/security/github_token>
- Release assets and their digests in GitHub's REST API: <https://docs.github.com/en/rest/releases/assets>
- RFC 5737, IPv4 address blocks for documentation: <https://www.rfc-editor.org/rfc/rfc5737>
- RFC 3849, the IPv6 documentation prefix: <https://www.rfc-editor.org/rfc/rfc3849>
- RFC 2606, reserved domain names such as `example.com`: <https://www.rfc-editor.org/rfc/rfc2606>
- RFC 7519, JSON Web Tokens, including unsecured tokens with `"alg":"none"`: <https://www.rfc-editor.org/rfc/rfc7519>
- The UDID format of devices from 2018 on: <https://developer.apple.com/forums/thread/108953> and <https://blog.diawi.com/2018/10/15/2018-apple-devices-and-their-new-udid-format/>
- The ECID, in Apple's Platform Security guide: <https://support.apple.com/guide/security/aside/sec21774ef95/web>
- Team identifiers and App ID prefixes: <https://developer.apple.com/library/archive/technotes/tn2311/_index.html>
- Randomized serial numbers: <https://www.macrumors.com/2021/03/09/apple-randomized-serial-numbers-early-2021/>
- Steam ID formats, in SteamKit2: <https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Types/SteamID.cs>
- Depot keys, in SteamKit2: <https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Steam/Handlers/SteamApps/Callbacks.cs>
- Manifest request codes, in SteamKit2: <https://github.com/SteamRE/SteamKit/blob/master/SteamKit2/SteamKit2/Steam/Handlers/SteamContent/SteamContent.cs>
- The `depotId;depotKey` lines of a depot key file: <https://github.com/jagotu/DepotDumper>
- Research note `avalonia_ui_layer.md`, section 2.2 part 2 and section 3.4: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
