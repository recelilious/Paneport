# Solution and Build

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T03:21Z |

Back to the [design index](README.md).

This document records how Paneport's .NET solution is laid out and built: the projects and their dependency rules, the build properties, code style, packages and lock files, tests, license headers and CI. Stage M0.1 wrote it (see the [L0 brief](stages/L0-foundation.md)). It is also the first design record of each project; the stage named in section 2 writes each project's full design. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Toolchain

- **SDK.** `global.json` pins the .NET SDK to exactly 10.0.403 and sets `rollForward` to `disable`, so no other SDK stands in for it (C-0009 section 2). 10.0.403 was the newest .NET 10 SDK on 2026-10-29: it carries runtime 10.0.14 and C# 14, and Visual Studio 2026 18.11.2 ships it. At the repository root, `dotnet --version` prints the pin, or reports that the SDK is missing.
- **iOS workload.** Microsoft publishes the iOS workload manifests only for the 10.0.100 feature band. The workload sets of the 10.0.400 band, up to 10.403.0, exist as well and should resolve those manifests, which this stage did not try. M1.1 confirms this against the Xcode version it pins, and may move the SDK pin in a reviewed change.
- **Test runner.** `global.json` also makes `dotnet test` use Microsoft.Testing.Platform (section 6).
- **IDE.** Visual Studio 2026, or Rider 2026.1 or later. Visual Studio 2022 cannot target `net10.0`.

## 2. Projects

`Paneport.slnx` lists every project. `eng/ci.slnf` lists the projects that CI builds and tests: all of them except Paneport.iOS, `tests/device` and `tests/Paneport.Interop.Tests` (C-0011 section 4), none of which exists yet.

| Project | Kind | Role (C-0004 section 2) | May reference in the solution | Holds now | Full design |
|---|---|---|---|---|---|
| Paneport.Core | Library | Domain model, view models, settings, compatibility database reader, and the interfaces that platform code implements. No platform API. | Nothing | Nothing | M0.3, in `app-shell.md` |
| Paneport.Steam | Library | Steam sign-in, licenses, app information and depot downloads, built on SteamKit2. No UI code. | Paneport.Core | Nothing | M2.3, with its first code |
| Paneport.UI | Library | Avalonia views, styles and overlay controls shared by both app heads. | Paneport.Core | The application class with the Fluent theme | M0.3, in `app-shell.md` |
| Paneport.Interop | Library | Source-generated bindings to the host interface. The only project allowed to use unsafe code. | Nothing | Nothing | M1.2, with its first bindings |
| Paneport.Desktop | Executable | Desktop head with a fake host, for UI work on Windows. Never shipped. Never writes a Steam sign-in token to disk. | Paneport.Core, Paneport.Steam, Paneport.UI | A window titled Paneport | M0.3, in `app-shell.md` |
| Paneport.Core.Tests | Test executable | Tests of Paneport.Core | Paneport.Core | Dependency direction tests | M0.3 adds tests |
| Paneport.Steam.Tests | Test executable | Tests of Paneport.Steam | Paneport.Steam, Paneport.Core | Dependency direction tests | M2.3 adds tests |
| Paneport.UI.Tests | Test executable | Tests of Paneport.UI, headless | Paneport.UI, Paneport.Core | Dependency direction tests, one headless test | M0.3 adds tests |
| Paneport.TrimTest | Executable | Trimmed publish of the four libraries with their dependencies (C-0011 section 4). Not a test project. | Paneport.Core, Paneport.Steam, Paneport.UI, Paneport.Interop | A program that does nothing | M0.2, in `repository-checks.md` |

Nothing references Paneport.Desktop or Paneport.iOS. A test project references only the project it tests and that project's dependencies, plus registered test libraries. Paneport.TrimTest checks the four libraries together, so it references each of them. Later stages add Paneport.iOS (M1.1 and M1.3; it may reference Core, Steam, UI and Interop), `tests/Paneport.Interop.Tests` (M1.2) and `tests/device` (M1.3; it may also reference Interop).

Paneport.Desktop creates the desktop application lifetime and its window in `Program.cs`, so Paneport.UI holds no desktop-only concept: the iOS head shows a single main view instead of a window. Paneport.Desktop is a console executable, so that `dotnet run` shows Avalonia's errors in the terminal; a Windows-subsystem executable would detach from it.

**Adding a project.**

1. Create its folder where C-0004 section 3 places it, with a `README.md`, and start its project file with the header comment of section 8.
2. Add it to `Paneport.slnx`. Add it to `eng/ci.slnf` too, unless C-0011 section 4 leaves it out.
3. A library sets `IsAotCompatible` and `GenerateDocumentationFile` in its project file. A test project sets `OutputType` to `Exe` and `UseMicrosoftTestingPlatformRunner` to `true`, references `xunit.v3`, links `tests/Shared/ProjectReferences.cs` and gets a dependency direction test (section 6).
4. Run `dotnet restore eng/ci.slnf --force-evaluate` and commit the project's `packages.lock.json`.
5. The stage that creates a component writes its design document (C-0012 section 1).

## 3. Build properties

`Directory.Build.props` applies to every project:

| Property | Value | Why |
|---|---|---|
| `LangVersion` | `14.0` | C# 14 (C-0010 section 1). |
| `Nullable` | `enable` | C-0010 section 2. |
| `ImplicitUsings` | `enable` | The SDK's implicit global usings keep files short. No contract requires them. |
| `TreatWarningsAsErrors` | `true` | Compiler and NuGet warnings fail the build (C-0010 section 2). |
| `MSBuildTreatWarningsAsErrors` | `true` | MSBuild's own warnings fail it too, for example a project reference to a project that does not exist, which MSBuild otherwise only reports as a warning. |
| `AnalysisLevel` | `latest-recommended` | C-0010 section 2. |
| `EnforceCodeStyleInBuild` | `true` | The code-style rules of `.editorconfig` run in the build (section 4). |
| `RestoreConfigFile` | `eng/nuget.config` | One package source for every machine (section 5). |
| `RestorePackagesWithLockFile` | `true` | Lock files (C-0009 section 2). |
| `RestoreLockedMode` | `true` in CI | No restore in CI, explicit or implicit, can rewrite a lock file. |
| `NuGetAudit`, `NuGetAuditMode` | `true`, `all` | Every package, direct or transitive, is checked against the advisories that nuget.org publishes (section 5). |
| `Deterministic` | `true` | C-0009 section 6. |
| `ContinuousIntegrationBuild` | `true` when `GITHUB_ACTIONS` is `true` | Maps source paths in compiler output in CI builds (C-0009 section 6). |

Each library's project file sets `TargetFramework` to `net10.0`, `IsAotCompatible` to `true` and `GenerateDocumentationFile` to `true`. `IsAotCompatible` turns on trimming and the trimming, AOT and single-file analyzers, so they report problems in our own code as build errors (C-0010 section 2). With the documentation file on, a public API without an XML documentation comment raises CS1591, which fails the build (C-0010 section 5). These properties live in the project files, because C-0004 section 3 has no root `Directory.Build.targets`, and setting them by project name in `Directory.Build.props` would hide them from each project file.

The SDK adds `Microsoft.NET.ILLink.Tasks` to every project with `IsAotCompatible`, at its own runtime version (10.0.14). A change of the SDK pin therefore changes the lock files as well.

Paneport.UI's XAML is compiled at build time. `AvaloniaXamlLoader.Load(this)` in `App.Initialize` is the entry point that the XAML compiler wires to the compiled code; nothing loads XAML at run time, and the build reports no trimming or AOT warning for it.

## 4. Code style

`.editorconfig` at the root has a `[*]` section for every file (UTF-8, LF, a final newline, no trailing whitespace) and a `[*.cs]` section with the C# formatting, code-style and naming rules (C-0010 section 2). The `[*]` section also reaches files under `external/` once forks are added, unless each fork carries its own root `.editorconfig` (C-0010 section 4).

Naming follows the C# identifier naming conventions on Microsoft Learn: `PascalCase` for types, namespaces, members and constants; `camelCase` for parameters and locals; `_camelCase` for private and internal instance fields; `s_camelCase` for private and internal static fields; an `I` prefix for interfaces and a `T` prefix for type parameters.

Enforcement, as confirmed with planted violations in this stage:

- `dotnet format eng/ci.slnf --verify-no-changes` fails on whitespace formatting and on every rule set to `warning`.
- The build also fails on IDE0011 (braces), IDE0040 (accessibility modifiers), IDE0044 (`readonly` fields), IDE0065 (using placement), IDE0161 (file-scoped namespaces) and IDE1006 (naming). The build ignores the severity suffix of the naming rules, so every enforced rule also has a `dotnet_diagnostic` line.
- The build skips IDE0003 (`this.` qualification) and IDE0049 (predefined type names). The format check catches both.
- No analyzer checks "one public type per file, named after the type" (C-0010 section 2). Review does.

No rule needed relaxing for the tests so far. If one does, it is relaxed only for `tests/`, in `.editorconfig`, with the rule and the reason named.

## 5. Packages and lock files

- **Central package management.** `Directory.Packages.props` holds every package version. A `Version` on a `PackageReference` fails restore with NU1008.
- **Lock files.** Every project has a committed `packages.lock.json`. CI restores with `--locked-mode`, which fails when a lock file does not match the projects. NuGet writes lock files with CRLF line endings on Windows; `.gitattributes` stores them with LF, and NuGet compares their content, not their bytes.
- **Stale entries.** A restore without `--force-evaluate` keeps an existing lock file as long as it still satisfies the projects, even after a project reference is removed (seen in this stage). Lock files are therefore always updated with `dotnet restore eng/ci.slnf --force-evaluate`.
- **Package source.** `eng/nuget.config` clears every package source, adds only nuget.org, and maps every package to it. `RestoreConfigFile` in `Directory.Build.props` points restores at that file, so the machine's own NuGet configuration plays no part; the workstation's own configuration had no package source at all. Restores from the .NET CLI honor this. An IDE that restores through its own NuGet integration may still read the machine's configuration; on a machine without a package source, restore from the command line first.
- **Audit.** With `NuGetAuditMode` set to `all` and warnings as errors, a new advisory for any package, direct or transitive, fails a restore that did not change. That is a real finding: the package is updated in a reviewed change. The packages below had no advisory on 2026-10-29.

**Adding or updating a package.**

1. Check its license and the license of every package it brings in. A restore on a scratch branch lists them in the lock files.
2. Register every component that the register in C-0003 section 6 does not cover yet, through a contract change that recelilious approves (C-0003 section 5, C-0012 section 2). A dependency never merges without its row.
3. Add or change its `PackageVersion` in `Directory.Packages.props`, and reference it without a version.
4. Run `dotnet restore eng/ci.slnf --force-evaluate`, and commit every changed lock file.
5. In the pull request, list every new package ID with the register row that covers it.

**Packages in the lock files after M0.1.** "1.2" marks a row that C-0003 version 1.2 added for this stage; recelilious accepted it on 2026-10-29.

| Packages | Brought in by | Register row |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.FreeDesktop, Avalonia.FreeDesktop.AtSpi, Avalonia.HarfBuzz, Avalonia.Headless, Avalonia.Native, Avalonia.Remote.Protocol, Avalonia.Skia, Avalonia.Themes.Fluent, Avalonia.Win32, Avalonia.X11 (12.1.4) | Paneport.UI, Paneport.Desktop, Paneport.UI.Tests | Avalonia |
| Avalonia.Headless.XUnit (12.1.4) | Paneport.UI.Tests | Avalonia.Headless.XUnit |
| Avalonia.BuildServices (11.3.2) | Avalonia | Avalonia.BuildServices (1.2) |
| Avalonia.Angle.Windows.Natives (2.1.27548.20260419) | Avalonia.Win32 | ANGLE (1.2) |
| Avalonia.Fonts.Inter (12.1.4) | Avalonia.Headless | Inter font (1.2) |
| SkiaSharp and SkiaSharp.NativeAssets.Linux, .macOS, .WebAssembly and .Win32 (3.119.4) | Avalonia.Skia | SkiaSharp (license text from 1.2) |
| HarfBuzzSharp and HarfBuzzSharp.NativeAssets.Linux, .macOS, .WebAssembly and .Win32 (8.3.1.3) | Avalonia.HarfBuzz | HarfBuzzSharp (1.2) |
| MicroCom.Runtime (0.11.6) | Avalonia | MicroCom.Runtime (1.2) |
| Tmds.DBus.Protocol (0.94.1) | Avalonia.FreeDesktop | Tmds.DBus.Protocol (1.2) |
| Microsoft.NET.ILLink.Tasks (10.0.14), Microsoft.Bcl.AsyncInterfaces (6.0.0), Microsoft.Win32.Registry (5.0.0) | The SDK, for `IsAotCompatible`; xunit.v3 | .NET runtime |
| xunit.v3, xunit.v3.assert, xunit.v3.common, xunit.v3.core.mtp-v1, xunit.v3.extensibility.core, xunit.v3.mtp-v1, xunit.v3.runner.common, xunit.v3.runner.inproc.console (3.2.2), xunit.analyzers (1.27.0) | The test projects | xunit.v3 |
| Microsoft.Testing.Platform, Microsoft.Testing.Platform.MSBuild, Microsoft.Testing.Extensions.TrxReport.Abstractions, Microsoft.Testing.Extensions.Telemetry (1.9.1) | xunit.v3 | Microsoft.Testing.Platform (1.2) |
| Microsoft.ApplicationInsights (2.23.0) | Microsoft.Testing.Extensions.Telemetry | Application Insights SDK (1.2) |

Avalonia's usual templates add `Avalonia.Fonts.Inter` to the app itself. Paneport.UI does not, so the Inter font reaches only the headless tests, through Avalonia.Headless.

## 6. Tests

- **Framework.** xunit.v3 3.2.2. The `xunit.v3` package runs on Microsoft.Testing.Platform 1.9.1. It stays at 3.2.2 because Avalonia.Headless.XUnit 12.1.4 is built against `xunit.v3.extensibility.core` 3.2.2, and xunit.v3 4.0 changed that extensibility API; the two move together.
- **Runner.** `global.json` sets `"test": { "runner": "Microsoft.Testing.Platform" }`, and each test project sets `UseMicrosoftTestingPlatformRunner`. The `global.json` setting matters: without it, `dotnet test` on the .NET 10 SDK uses VSTest, finds no VSTest adapter, runs nothing and still exits with 0 (seen in this stage).
- **Commands.** Locally: `dotnet test eng/ci.slnf -c Release`. In CI, after the build: `dotnet test eng/ci.slnf -c Release --no-build --fail-skips on`. The solution filter works as a positional argument; `--solution eng/ci.slnf` works as well.
- **Pass criteria.** A test project that runs no test exits with code 8, which fails `dotnet test` (Microsoft.Testing.Platform's default). `--fail-skips on`, an xunit.v3 option, turns every skipped test into a failure. A stage that needs a skipped test changes the CI command deliberately.
- **Dependency direction tests.** Each test project has `DependencyDirectionTests`, which reads the project references that the build recorded in the test assembly's `deps.json` file, through `tests/Shared/ProjectReferences.cs`. It checks its own project and the test project itself against the rules of section 2. `deps.json` keeps every project reference, while the compiler drops a reference that no code uses from the assembly's metadata, so reflection would miss exactly the references that need catching. A planted reference from Paneport.Core to Paneport.Interop failed Paneport.Core.Tests. Paneport.Interop and Paneport.Desktop have no test project in `eng/ci.slnf`. Interop's rule gets its test with `tests/Paneport.Interop.Tests` in M1.2. Desktop's own references have no test; a reference to Desktop from Core, Steam or UI fails that project's test.
- **Headless test.** `TestAppBuilder` starts the real Paneport application on Avalonia's headless platform for every `[AvaloniaFact]` test, and `HeadlessApplicationTests` shows a window there. Avalonia.Headless.XUnit 12.1.4 runs on xunit.v3 3.2.2, so the fallback of driving Avalonia.Headless directly was not needed.

## 7. Telemetry

Three components send usage data from builds and test runs unless an environment variable turns them off. The CI workflow sets all three. On a workstation, setting them is the developer's choice.

| Component | What it sends | Turned off by |
|---|---|---|
| .NET CLI | SDK usage data | `DOTNET_CLI_TELEMETRY_OPTOUT=1` |
| Microsoft.Testing.Platform, through Microsoft.Testing.Extensions.Telemetry and Microsoft.ApplicationInsights | Usage data of each test run, including a hashed executable name and a three-octet IP address | `TESTINGPLATFORM_TELEMETRY_OPTOUT=1`, or the CLI variable above |
| Avalonia.BuildServices | Build statistics before each compile of a project that uses Avalonia, including a hashed project name, a hashed machine name and an anonymous machine identifier | `AVALONIA_TELEMETRY_OPTOUT=1` |

## 8. License headers

- Every original file that can hold a comment starts with the two SPDX lines of C-0010 section 6, with the year the file was created: as `//` comments in C#; as `#` comments in YAML, TOML, `.editorconfig`, `.gitattributes`, `.gitignore` and `.gitmessage`; and inside one `<!-- -->` comment in project files, props files, `Paneport.slnx`, AXAML, `eng/nuget.config`, and Markdown outside `docs/`.
- `REUSE.toml` covers the files that hold no header. Under `docs/` it overrides what the files say, because the contracts quote SPDX tags as examples and REUSE would read those quotes as the files' own license information. It also covers JSON files and the solution filter, which cannot hold a comment.
- `LICENSES/MIT.txt` holds the MIT License text (C-0003 section 4). REUSE ignores the root `LICENSE` file by design.
- `dotnet sln add` and `dotnet add package` keep the header comment of the files they rewrite (seen with SDK 10.0.403). Another tool might not; `reuse lint` in CI catches a lost header.
- The REUSE tool is a Python program, so it runs in CI, not on the Windows workstation (C-0010 section 1 allows Python only as a tool).

## 9. Continuous integration

`.github/workflows/ci.yml` runs on every push and every pull request. Its [README](../../.github/workflows/README.md) lists the jobs. The `dotnet` job runs on `windows-2025` and `ubuntu-24.04`: locked restore, Release build, tests and format check of `eng/ci.slnf`. The `checks` job runs `reuse lint` with the REUSE tool version that `eng/toolchain.json` pins. Actions are pinned to full commit hashes, runner images to explicit versions, and the workflow's token can only read the repository. M0.2 completed both jobs; [repository-checks.md](repository-checks.md) describes every step.

`dotnet build`, `dotnet test` and `dotnet format` all accept `eng/ci.slnf`, whose solution is `Paneport.slnx`, so the per-project fallback for the format check was not needed.

## 10. Open items

- **SkiaSharp's native libraries.** The third-party notices in SkiaSharp's and HarfBuzzSharp's native-asset packages list about twenty components, among them libmicrohttpd under LGPL-2.1-or-later. Before the first iOS release, M9.1 confirms which of them the shipped native libraries contain, so that the generated notices and the register match them.
- **IDE restores.** Whether Visual Studio's own NuGet restore honors `RestoreConfigFile` was not tested; see section 5.

## 11. Sources

Public material consulted (C-0001 section 6):

- global.json: <https://learn.microsoft.com/en-us/dotnet/core/tools/global-json>
- MSBuild properties of the .NET SDK: <https://learn.microsoft.com/en-us/dotnet/core/project-sdk/msbuild-props>
- Preparing libraries for trimming: <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/prepare-libraries-for-trimming>
- Code analysis and `EnforceCodeStyleInBuild`: <https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview>
- Naming rules in `.editorconfig`: <https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules>
- C# identifier naming conventions: <https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names>
- MSBuild command-line reference, warnings as errors: <https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference>
- Central package management: <https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management>
- Lock files: <https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files>
- Restore properties, including `RestoreConfigFile`: <https://learn.microsoft.com/en-us/nuget/reference/msbuild-targets>
- Package source mapping: <https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping>
- Auditing packages: <https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages>
- `dotnet test` and Microsoft.Testing.Platform: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test>
- Microsoft.Testing.Platform exit codes: <https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-exit-codes>
- Microsoft.Testing.Platform telemetry: <https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-telemetry>
- .NET SDK telemetry: <https://learn.microsoft.com/en-us/dotnet/core/tools/telemetry>
- xunit.v3 on Microsoft.Testing.Platform: <https://xunit.net/docs/getting-started/v3/microsoft-testing-platform>
- Avalonia headless testing with xUnit: <https://docs.avaloniaui.net/docs/concepts/headless/headless-xunit>
- Avalonia.BuildServices and its opt-out: <https://www.nuget.org/packages/Avalonia.BuildServices>
- .NET 10 release metadata: <https://github.com/dotnet/core/blob/main/release-notes/10.0/releases.json>
- REUSE specification 3.3: <https://reuse.software/spec-3.3/>
- Where GitHub looks for a README: <https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-readmes>
- Secure use of GitHub Actions, including pinning to commit hashes: <https://docs.github.com/en/actions/reference/security/secure-use>
- GitHub-hosted runner images: <https://github.com/actions/runner-images>
- actions/checkout: <https://github.com/actions/checkout>
- actions/setup-dotnet: <https://github.com/actions/setup-dotnet>
- Research note `avalonia_ui_layer.md`, sections 2.1, 2.2 and 3.2 to 3.4: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
