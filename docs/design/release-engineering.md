# Release Engineering

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-19T04:55Z |

Back to the [design index](README.md).

This document describes how Paneport's app bundles and releases carry what C-0003 section 3 and C-0009 section 6 ask for. Stage M9.1 writes it (see the [L9 brief](stages/L9-first-release.md)). Its first part comes from S9.1.1, the notices generator, which recelilious brought forward on 2026-11-17 so that the public `.ipa` files of the `ios` job carry the notices of what they ship: where the notices sit in the bundle, the format of their index, the generator and its rules, the attribution mapping, where the license texts come from, and the steps of the `ios` job. M9.1 adds the `release` workflow and its modes, the source archives, the full scan with its allow-list, and the version rules. [repository-checks.md](repository-checks.md) describes the other tools, and [ios-app-head.md](ios-app-head.md) section 11 the packaging, signing and scan of the `ios` job. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. The notices in the bundle

C-0009 section 4 names no place for the notices in the bundle, so this document fixes one: the folder `licenses/` at the root of the app bundle.

| File | What it holds |
|---|---|
| `licenses/THIRD_PARTY_NOTICES.md` | A table of the third-party components, then a section for each with its license, its sources, its credit if it has one, and the full text of its license and notices. |
| `licenses/index.json` | The list of the components in the same order, for the app's open-source licenses screen (S9.0.1), with the path of each component's text (section 2). |
| `licenses/texts/NN-<name>.txt` | One file per component: its name, license, sources and credit, then each of its texts under a heading that names the file and the package, runtime pack or upstream repository and commit it comes from. `NN` is the component's place in the order, padded to two digits or more. |

- **Order.** The components appear in the order of their names, compared without regard to case and then exactly, and the same order holds in all three outputs. Each component appears once.
- **Paneport itself** is not listed: its code is under the MIT license (C-0003 section 1), which the About section of S9.0.2 shows. The notices file says that each component keeps its own license, so the app as a whole is not under the MIT license.
- **Before signing.** The `ios` job writes the notices after the build and before `eng/mac/sign-adhoc.sh`, so the ad-hoc signature seals them in `_CodeSignature/CodeResources` with the rest of the bundle, and a sideloading tool that re-signs the app keeps them as it keeps every other file of the bundle; that is not yet verified on the device. The `release` job of M9.1 does the same and attaches the notices file and the texts to the release (C-0009 section 6).
- **Never in git.** The generator writes only into the bundle and, with `--out`, into a build output folder: one outside the repository, or one inside it under `build/` or below a `bin/` or `obj/` folder, which C-0004 section 3 reserves for build output and `.gitignore` ignores. It refuses any other folder, and a device or network path such as `\\?\`, `\\.\` or `\\server\share`. Before it compares a path with the repository's, it follows every link among the existing folders of both to its final target, so that a link or junction under `build/` cannot lead into a tracked folder, and it refuses a destination that is itself a link, the bundle's `licenses/` among them. In a destination it replaces only its own files, `THIRD_PARTY_NOTICES.md`, `index.json` and `texts/NN-<name>.txt`, after it has found an index of format 1 there and nothing else; a folder that holds anything else it leaves as it is and refuses. It checks both destinations before it writes either.
- **Plain text.** Every output is UTF-8 without a byte-order mark, with Unix line endings and one line break at its end. A source text may be UTF-8, or UTF-16 with a byte-order mark; one that is empty, not text, or not valid UTF-8 counts as missing. The texts keep every notice as the upstream wrote it, author e-mail addresses included (C-0003 section 5), with one exception: a package that declares MIT and ships no license file gets the standard MIT text with the copyright line of its description (section 4), which can name another holder than the upstream's license file.
- **Texts that no package ships.** On 2026-11-19 recelilious decided that such a text comes from a pinned download in CI. The mapping names it by its URL on `raw.githubusercontent.com` at a full commit and by its SHA-256 (section 5). The generator refuses any other scheme or host, a branch, a tag or a short hash in place of the commit, `..`, `?`, `#`, `%` or a backslash anywhere in the URL, a part that is `.`, and a hash that is not 64 lowercase hexadecimal digits. The step of the `ios` job before the notices step takes the list from the generator, `--list-upstream`, so that the list lives only in the mapping; it fetches each URL with `curl --proto '=https' --proto-redir '=https' -fsSL --retry 3 --max-filesize 1048576` into `build/upstream/<sha256>` and fails on a hash that differs, with an annotation that names the URL, the expected hash and the actual one. The generator never uses the network: it reads each file from `build/upstream/`, checks its SHA-256 again, and fails in both modes on a file that is missing or changed, naming that step. A notice that lives only in the header of a source file takes a range of lines, `"lines": [first, last]`: the hash covers the whole file, the range applies after the check, and a line in the range that is neither a comment nor blank fails, so that no code ships as a notice. Nothing is committed, so no contract changes: C-0003 section 4 and C-0009 section 6 put the texts into the bundle and the release, not into the repository. The `release` workflow of M9.1 needs the same step.

## 2. The index

`licenses/index.json` is a JSON object, format 1. The licenses screen of S9.0.1 reads it with `System.Text.Json` source generation (C-0010 section 2).

```json
{
  "format": 1,
  "components": [
    {
      "name": "SkiaSharp",
      "license": "MIT. Its native-asset packages contain the Skia library (BSD-3-Clause) and ...",
      "category": "Allowed",
      "sources": ["SkiaSharp 3.119.4", "SkiaSharp.NativeAssets.iOS 3.119.4"],
      "text": "texts/08-skiasharp.txt"
    }
  ]
}
```

| Field | Value |
|---|---|
| `format` | 1. A later format that a reader of format 1 cannot read gets another number. |
| `components` | The components in the order of section 1. |
| `name` | The component's row in the register of C-0003 section 6, by its Component text. |
| `license` | The License text of that row, as the register writes it. |
| `category` | The leading word of the row's category: `Allowed` or `Conditional`. |
| `sources` | Each package of the app's lock file that maps to the component, and each package, runtime pack, upstream repository or file of the repository that its texts come from, as `<package ID> <version>`, as `<owner>/<repository> <commit>` or as a path, sorted without regard to case. They name no local folder. |
| `credit` | Only when the component has one: the credit that its license asks for in the documentation, such as the FreeType credit of the FTL (C-0003 section 5). |
| `text` | The path of the component's text, relative to the index. |

## 3. The generator

`tools/GenerateNotices.cs` is a C# file-based tool, run from the repository root (C-0010 section 1).

```bash
dotnet run tools/GenerateNotices.cs -- --mode ios --project src/Paneport.iOS/Paneport.iOS.csproj --app <bundle.app> --out build/notices/main
dotnet run tools/GenerateNotices.cs -- --list-upstream
dotnet run tools/GenerateNotices.cs -- --self-test
```

**Inputs.**

| Input | What the generator takes from it |
|---|---|
| The register of C-0003 section 6 | Each component's license and category, read by `tools/Shared/Register.cs`, the parser that `tools/CheckLicenses.cs` uses too (section 7). |
| `eng/license-map.json` | The component of each package of the lock file, read by `tools/Shared/LicenseMap.cs`. |
| `eng/release/bundle-components.json` | The attribution of the bundle's files and the sources of each component's texts (section 5). |
| `LICENSES/MIT.txt` | The standard MIT text, for packages that declare MIT and ship no license file (section 4). |
| `build/upstream/<sha256>` | The texts that no package ships, which the `ios` job fetched before the notices step (section 1). |
| The bundle's `Info.plist` | `CFBundleExecutable`, which names the main executable, and the display name and the versions for the notices file. The build writes it in Apple's binary format; the generator reads that format and XML, and only as far as the string values at the top. |
| The app's project | Its lock file, `packages.<project>.lock.json`, with each space of the project's name made an underscore as NuGet names it, or `packages.lock.json` beside it, for the packages; a lock file that gives one package two versions is refused, because the texts would come from one of them only. And one query of MSBuild, `dotnet msbuild <project> -t:ResolveFrameworkReferences -getItem:ResolvedRuntimePack -getProperty:NuGetPackageRoot -p:Configuration=Release`, for NuGet's package folder and the runtime packs that the build uses, so that the texts come from the packs that the app was built with. |

**Attribution.** The generator lists every file of the bundle, hidden ones too, without following links and without its own `licenses/`. The main executable maps to the components that the mapping names for it, Paneport, the .NET runtime and .NET for iOS, and to the component of every package in the lock file, because full ahead-of-time compilation puts the code of the packages that the app ships into it (C-0009 section 3). So do the files that the mapping lists beside the executable, which the build makes of the whole app in the same way: `aot-instances.aotdata.arm64`, the ahead-of-time data of the generic instances that the compiler shares between all assemblies. The lock file also holds packages that do not ship, such as the trimmer's `Microsoft.NET.ILLink.Tasks` and the native assets of other platforms, and packages whose assembly the trimmer removes whole, such as `MicroCom.Runtime`; their components are listed all the same, which errs on the side of a notice too many. Every other file takes the components of the first rule of the mapping whose path matches it. The components of all the files, Paneport aside, are the components of the notices.

**Rules.** Every finding names the file, component or package; none prints a license text.

| Finding | `ios` mode, the `ios` job | `release` mode, the `release` job of M9.1 |
|---|---|---|
| A file that no rule attributes | Warning | Failure |
| A text that the mapping lists under `missing`, which a component needs and no source provides yet (section 5) | Warning | Failure |
| A component that the register lacks | Failure | Failure |
| A component whose row is Under review, Forbidden or a Tool row (C-0003 section 5, C-0009 section 4) | Failure | Failure |
| A component without a license text: no entry in the mapping; a file that is missing, empty, not text or not valid UTF-8; a package that the lock file or NuGet's folder lacks; a runtime pack that the build did not resolve; an upstream text that is not in `build/upstream/` or whose SHA-256 differs; a range of lines past the end of its file or with a line of code; or an MIT package without a copyright notice | Failure | Failure |
| A text that names a package which `eng/license-map.json` gives to another component | Failure | Failure |
| A component under LGPL-2.1 or Apache-2.0, by the expression that its register row starts with or by a license that its packages declare, none of whose texts holds that license's full terms: a short notice that points to the license is not the license (C-0003 section 3) | Failure | Failure |
| A package that declares, by its SPDX expression, a license that the register row of its component does not name, as the register then needs a change first (C-0003 section 6), or that declares an empty expression | Failure | Failure |
| A NOTICE file at the top of a package or runtime pack of an Apache-2.0 component that is not among its texts (C-0003 section 5) | Failure | Failure |
| An FTL component without a credit (C-0003 section 5) | Failure | Failure |
| An LGPL component other than SteamKit2 in the main executable, by the expression that its register row starts with or by a license that its packages declare (C-0003 section 3: only SteamKit2 is compiled into the executable; other LGPL code ships as libraries that the user can replace) | Failure | Failure |
| A package of the lock file that `eng/license-map.json` does not map | Failure | Failure |
| An executable that `Info.plist` names and the bundle lacks | Failure | Failure |

A license counts as named by a register row when its identifier stands in the row's License text as a whole name: `MIT` counts in "MIT. Its native-asset packages ..." but not in `MIT-Modern-Variant`. The expression that a row starts with is its License text up to the first period, comma, semicolon or colon that ends a phrase, so a note further on in the row, such as "MIT. Its native framework holds a library under LGPL-2.1-or-later", makes no component LGPL. The license type of a package description is read as NuGet reads it, trimmed and without regard to case. The full terms of a license are recognized by a passage of its text, with each run of white space read as one space: "TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION" for Apache-2.0, and "GNU LESSER GENERAL PUBLIC LICENSE TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION" for LGPL-2.1, in any of its identifiers. After a failure the generator writes nothing. Exit codes: 0 when the notices are written, 1 after a failure, 2 for a wrong argument or an input that cannot be read.

**Report.** It prints the runtime packs, the coverage of every file with its components, the license and notice files anywhere in the packages and runtime packs of the notices that are not among their texts, the packages that declare their license without an SPDX expression, by `licenseUrl` only or without a type, the rules that match no file, each component with its number of files, texts and sources, and every warning and failure; under GitHub Actions each warning and failure is also an annotation. Every line writes CR and LF as `%0D` and `%0A`, and an annotation's data writes `%` as `%25` too, so that a file name with a line break cannot start a line of its own, such as a workflow command. The list of license and notice files is for the coverage review of M9.1: it shows which packages carry notices of their own, for example for the native libraries inside them, and is not a failure. It takes each file whose name holds license or licence, notice, copying, copyright, third party or 3rd party, authors, EULA, patent, credit, acknowledgement or legal.

**Self-test.** `--self-test` builds its fixtures at run time in a temporary folder and deletes them: a register, a license map and a mapping of made-up components, packages with their descriptions and texts, three runtime packs, repository files, two upstream texts in `build/upstream/`, one of them a source file whose header the mapping takes as a range, and a bundle with a binary `Info.plist`. It has 115 checks.

- **The fully registered bundle** passes in both modes, and in release mode again after its notices are in the bundle. Its three outputs list each component once, in the same order in the table, the section headings, the links to the texts, the index and the text files, and the index gives each component the license and category of its row. The main executable maps to SteamKit2 among the packages of the lock file. The texts come from packages, runtime packs, the repository and upstream files, UTF-16 and lone CRs among them, with sorted sources and a fence longer than any run of backticks in the text. The MIT text of a package without a license file carries the package's copyright line, the Apache-2.0 NOTICE file and the FreeType credit are kept, the outputs name no local path and carry no byte-order mark, and the report lists the notice files that the texts leave out.
- **Each failure of the table** is planted on its own, and each must fail and name what fails; an unattributed file, a hidden one too, and a text without a source must warn in `ios` mode and fail in `release` mode.
- **The output folders.** The generator writes only into the bundle's `licenses/` and the output folder; refuses a folder that holds a file it did not write, beside, below or among the texts, or an index of another format, and keeps that file; leaves no text behind after a run with one component fewer; refuses device and network paths; follows a link under `build/` and refuses one that leads into a tracked folder; refuses a destination that is a link; and lists a link in the bundle as a file without following it.
- **The command line** runs against a fixture repository with a stubbed answer of MSBuild: it writes both destinations, exits with 1 and writes nothing after a failure, checks both destinations before it writes either, and escapes line breaks in its report and annotations.
- **The readers** have checks of their own: binary and XML property lists, truncated and damaged ones, the path patterns, the mapping with each refused URL, hash and range, MSBuild's answer, lock files, SPDX expressions and the expression that a row starts with, the full terms, the MIT text and the text decoder.

The `ios` job runs the self-test before it uses the generator, and the self-test runs on Windows as well, where its links are junctions, because a symbolic link needs a right there. The check that hidden files are listed can fail only on macOS and Linux, where a leading dot hides a file.

## 4. Where the texts come from

Each component's texts come from one of five kinds of source, which the mapping names (section 5):

| Source | Text |
|---|---|
| A file of a package of the lock file | The file in NuGet's package folder, such as `LICENSE.txt` or `THIRD-PARTY-NOTICES.txt`. The package must map to the component in `eng/license-map.json`. |
| A package that declares an expression | For MIT only: the standard MIT text of `LICENSES/MIT.txt` with its copyright line replaced by the `copyright` of the package's description (its `.nuspec`), which must declare the expression `MIT` and a copyright notice. |
| A file of a runtime pack | The file in the folder of the runtime pack that MSBuild resolved for the framework, such as `Microsoft.NETCore.App` or `Microsoft.iOS`. |
| A file of this repository | A path below the repository root, such as a license file of a fork under `external/`, which the `ios` job fetches before the notices step. |
| An upstream file | A file on `raw.githubusercontent.com` at a full commit, with its SHA-256, which the `ios` job fetches into `build/upstream/` (section 1); all of it, or the range of lines of a header. |

This answers the M9.1 question of where the full text and the copyright line come from for a package that declares only a license expression: from the package's own files when it has any; for a package that declares MIT and ships none, from the standard text and the copyright of its description; and for a text that no package ships, from the upstream repository at the commit that the package records, as a pinned download. The generator builds no other license from an expression.

The packages that the app ships carry these texts, as the mapping lists them:

| Component | Texts |
|---|---|
| .NET runtime | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` of the runtime pack of `Microsoft.NETCore.App`, Mono's for `ios-arm64`. The notices list the third-party code of the runtime, such as Mono's, zlib-ng, Brotli and the Unicode data, and name parts under Apache-2.0 without its terms, so the full Apache-2.0 text follows; a notice too many is the accepted way to err, and the coverage review of M9.1 confirms which parts ship. |
| .NET for iOS (dotnet/macios) | `LICENSE` of its two runtime packs, `Microsoft.iOS` with the bindings and `Microsoft.iOS.Runtimes` with the native code for `ios-arm64`: the MIT license of dotnet/macios, followed by the notices of the third-party code that it carries. The two files are alike, so the notices hold the text once, under a heading that names both packs. |
| Avalonia | Its packages ship no license or notice file, so its texts come from the repository at the commit `2d79ba55` that their descriptions record for 12.1.4: `licence.md`, Avalonia's MIT license as upstream wrote it, and `NOTICE.md`, the notices of the third-party code inside Avalonia.Base and Avalonia.Controls: WPF, the Silverlight Toolkit (Ms-PL), wayland-protocols, Metsys.Bson (BSD), RichTextKit (Apache-2.0), Mono, Collections.Pooled, EllipticalArc.java (BSD), WinUI, Chromium (BSD), Flutter (BSD) and Reactive Extensions. Lines 1 to 3 of `src/Avalonia.Base/Media/TextFormatting/Unicode/BiDiAlgorithm.cs` give the notice of Six Labors, the other holder of Apache-2.0 code in Avalonia.Base, which `NOTICE.md` does not list. Then the full Apache-2.0 text, and the MIT licenses of foliojs/linebreak and foliojs/unicode-trie, from which `LineBreak.cs` and `UnicodeTrie.cs` say they were ported. |
| Avalonia.BuildServices | Its `LICENSE` at the commit `777f975` that its description records. |
| MicroCom.Runtime | It declares MIT and ships no license file, so its text is the MIT text with the copyright of its description, which names the same holder as its upstream license. |
| CommunityToolkit.Mvvm | `License.md` and `ThirdPartyNotices.txt` of the package. |
| SkiaSharp, HarfBuzzSharp | `LICENSE.txt` of the managed package and `THIRD-PARTY-NOTICES.txt` of the iOS native-asset package, which lists the further components of the native library, as the register rows say. |

The full Apache-2.0 text is the `LICENSE` of kubernetes/kubernetes at `d30db1f9`, which is byte for byte the text at apache.org (checked on 2026-11-19), because RichTextKit's own `license.txt` holds only the short notice.

## 5. The attribution mapping

`eng/release/bundle-components.json` is the reviewed mapping from the files of an app bundle to Paneport or to components of the register, and from each component to its texts. One mapping serves both flavors of the app and the release.

```json
{
  "mainExecutable": { "components": ["Paneport", ".NET runtime", ".NET for iOS (dotnet/macios)"], "note": "..." },
  "files": [
    { "path": "Frameworks/libSkiaSharp.framework/**", "components": ["SkiaSharp"], "note": "..." }
  ],
  "components": {
    "SkiaSharp": {
      "texts": [
        { "package": "SkiaSharp", "file": "LICENSE.txt" },
        { "package": "SkiaSharp.NativeAssets.iOS", "file": "THIRD-PARTY-NOTICES.txt" }
      ],
      "note": "..."
    },
    "Avalonia": {
      "texts": [
        { "upstream": "https://raw.githubusercontent.com/AvaloniaUI/Avalonia/<commit>/NOTICE.md", "sha256": "<hex>" },
        { "upstream": "https://raw.githubusercontent.com/<owner>/<repo>/<commit>/<path>", "sha256": "<hex>", "lines": [1, 3] }
      ],
      "note": "..."
    }
  }
}
```

- **The main executable.** `mainExecutable` names the components of the executable, besides those of the packages of the lock file, a `note`, and in `paths` the other files that the build makes of the whole app and that map in the same way.
- **Rules.** Each rule has a `path` relative to the bundle, its `components` and a `note` that says why. In a path, `*` matches within one part, `?` one character of it, and `**`, which must be a whole part, any number of parts: `Frameworks/x.framework/**` is everything inside that framework, and `**/x` an x in any folder. A path matches a whole file name and never one with a line break after it, and `**` matches line breaks too. The first rule that matches a file attributes it, so `Microsoft.iOS.*`, which belongs to .NET for iOS, comes before `Microsoft.*`, which belongs to the .NET runtime. `Paneport` stands for our own code.
- **Texts.** Each component of the notices needs an entry with at least one text, each written as `{ "package", "file" }`, `{ "package", "license": "MIT" }`, `{ "runtimePack", "file" }`, `{ "repository" }`, or `{ "upstream", "sha256" }` with an optional `"lines": [first, last]`, and may have a `credit` and a `note`. Paths stay below their folder. A text that two sources hold alike is written once, under a heading that names both.
- **Texts without a source.** A component may list under `missing` the texts that it needs and that no source provides yet, each with its `text` and a `note`: each is a warning in `ios` mode and a failure in `release` mode, naming the component and the text, so that the gap shows in every run. The list is empty today.
- **Strict.** The generator rejects an unknown key, a rule without a note or components, a component named twice, a path that leaves its folder, a pattern with a partial `**`, texts for Paneport, an upstream URL or hash of another form (section 1), a range that is not two line numbers from 1 with the first not after the last, one URL with two hashes, and an empty `missing` list or one of its texts without a note.
- **Adding files to the bundle.** A stage that adds files adds rules for them, and for a new component its texts, after the component is registered (C-0003 section 5): a fork's license files, for example, as `{ "repository": "external/..." }`. A new NuGet package of the app needs a row in `eng/license-map.json` and texts, or the `ios` job fails; `licenses/` at the root of the bundle belongs to the generator. The `ios` job warns about an unattributed file and fails on a component that is not registered or not allowed to ship (L9 brief, M9.1 hand-over).

## 6. The ios job

Between building both flavors and signing them, the `ios` job finds both bundles, runs the generator's self-test, fetches the texts that no package ships into `build/upstream/` and checks each by its SHA-256 (section 1), and runs the generator in `ios` mode on each flavor with its own project: `src/Paneport.iOS/Paneport.iOS.csproj` for the main app and `tests/device/Paneport.DeviceTest.csproj` for the device test flavor, with the copies in `build/notices/main` and `build/notices/test`. After packaging and the scan, it checks that each `.ipa` holds `licenses/THIRD_PARTY_NOTICES.md`, `licenses/index.json` and every text that the index names, and goes on with the other flavor when a package cannot be read. A failure in any of these steps keeps the `.ipa` files from being uploaded, as every failed step does. [repository-checks.md](repository-checks.md) section 6 lists every step of the job, and [ios-app-head.md](ios-app-head.md) section 11 describes the packaging, signing and scan.

## 7. Shared code between tools

The M9.1 brief left open how the tools of the stage reuse the register parsing of M0.2's tools. They share source files: the register parser and the parser of `eng/license-map.json` live in `tools/Shared/Register.cs` and `tools/Shared/LicenseMap.cs`, and `tools/CheckLicenses.cs` and `tools/GenerateNotices.cs` include both with `#:include`, a directive of file-based apps that SDK 10.0.403 accepts. A program of several files needs a `#!` line at its top, or the analyzer warns (CA2266), which the build treats as an error; C-0010 section 6 puts the SPDX lines right after it. The SDK applies the `#:` directives of an included file to the tool that includes it, and it expands MSBuild syntax in an `#:include` path. So the license checker fails on an `#:include` of anything but a `.cs` file that git tracks directly in `tools/Shared/`, named by a plain path; on `#:ref`; on a `#:property` other than the ones that the tools set, `PublishAot` today; and on any `#:` directive in a shared file. It reads the lines of a tool as the compiler does, ending them at CR LF, CR, LF, NEL, LS and PS ([repository-checks.md](repository-checks.md) sections 2 and 4). The detectors of `tools/ScanPersonalData.cs`, which the full scan of S9.1.3 reuses, can move into `tools/Shared/` the same way.

## 8. Confirmed in this stage

- SDK 10.0.403 accepts `#:include` without an opt-in; `#:ref` is experimental and works only after `#:property ExperimentalFileBasedProgramEnableRefDirective=true`. It applies an included file's `#:package` directives: a restore with an included file that names an unknown package fails. It expands MSBuild syntax in an `#:include` path, such as `$(Property)`, `%2E` and a semicolon between two paths, which the review of #30 showed. A program of several files gets CA2266 without a `#!` line, and none with it.
- `dotnet msbuild <project> -t:ResolveFrameworkReferences -getItem:ResolvedRuntimePack -getProperty:NuGetPackageRoot` prints the folder, package ID and version of each runtime pack and NuGet's package folder as one JSON object, without building; on the workstation it ran from inside the generator.
- Of the packages that the app ships, Avalonia's, Avalonia.BuildServices and MicroCom.Runtime declare MIT and ship no license file; SkiaSharp and HarfBuzzSharp ship `LICENSE.txt` and their native-asset packages `THIRD-PARTY-NOTICES.txt`; CommunityToolkit.Mvvm ships `License.md` and `ThirdPartyNotices.txt`; the .NET runtime packs ship `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`; and the packs of .NET for iOS ship the `LICENSE` of dotnet/macios, which holds its third-party notices. The facts about the iOS-only packages and packs, `SkiaSharp.NativeAssets.iOS`, `HarfBuzzSharp.NativeAssets.iOS`, `Avalonia.iOS`, Mono's `ios-arm64` pack and the packs of .NET for iOS, come from [run 44424469253](https://github.com/recelilious/Paneport/actions/runs/44424469253); the other packages were read in the workstation's NuGet package folder.
- Avalonia's descriptions record the repository commit `2d79ba55` for 12.1.4, and Avalonia.BuildServices' the commit `777f975`. At those commits, `licence.md` names AvaloniaUI OÜ and Avalonia.BuildServices' `LICENSE` AvaloniaUI OU, where the descriptions say "The AvaloniaUI Project". Of the source files of Avalonia.Base with an Apache-2.0 header, the ones of RichTextKit name Topten Software and the others Six Labors. RichTextKit's `license.txt` holds only the short Apache-2.0 notice. The `LICENSE` of kubernetes/kubernetes at `d30db1f9` and of google/guava at `241d5e40` are byte for byte the text at apache.org. Each hash of the mapping was taken from the GitHub API without saving a file, and the same URLs on `raw.githubusercontent.com` gave the same hashes.
- NuGet trims the license type of a description and reads it without regard to case, and names a project's own lock file `packages.<name>.lock.json` with each space of the name made an underscore.
- The build writes the app's `Info.plist` in the binary format: .NET for iOS saves the compiled manifest as a binary property list.
- In the first run of the `ios` job ([run 44424469253](https://github.com/recelilious/Paneport/actions/runs/44424469253)), each flavor's bundle held 125 files before signing: the executable, 56 assemblies, each with its ahead-of-time data, `aot-instances.aotdata.arm64`, `Info.plist`, `PkgInfo`, `runtimeconfig.bin` and three frameworks, the host's and those of SkiaSharp and HarfBuzzSharp, which arrive with a `_CodeSignature` of their own. Every file but `aot-instances.aotdata.arm64` matched a rule, and the notices list eight components. With the notices, each `.ipa` holds 137 entries, and `tools/ScanPackage.cs` finds nothing in either. MSBuild resolves three runtime packs: `Microsoft.NETCore.App.Runtime.Mono.ios-arm64` 10.0.14, and for .NET for iOS `Microsoft.iOS.Runtime.ios.net10.0_27.1` and `Microsoft.iOS.Runtime.ios-arm64.net10.0_27.1` 27.1.10018. No ICU data ships: the bundle holds no `icudt.dat`. `MicroCom.Runtime.dll` does not ship either, because the trimmer removes it whole, but its package stays in the lock file, so the executable still maps to it.

## 9. Not yet in this document

M9.1 adds the `release` workflow and its modes, with the step that fetches the texts that no package ships, the build from the tag, stripping, the source archives, the full scan of C-0009 section 6 with its allow-list, the checksums, the composed release notes and the version rules.

## 10. Open items

- **Texts that no package ships: decided.** recelilious decided on 2026-11-19 that such a text comes from a pinned download in CI (section 1), with no contract change, since C-0003 section 4 and C-0009 section 6 put the texts into the bundle and the release, not into the repository. Avalonia's and Avalonia.BuildServices' texts now come from their repositories at the commits that their packages record, and shipping `licence.md` and `LICENSE` as upstream wrote them settles the earlier mismatch, where the MIT text carried the descriptions' holder, "The AvaloniaUI Project", and not the company that the license files name. The list of texts without a source is empty: no text of Avalonia's, or of another component, is known to be missing.
- **What M2.3 needs.** The full-terms rule fails each of these until its texts are complete.
  - SteamKit2: its `license.txt` is a 15-line LGPL notice, not the LGPL-2.1 text, so besides `license.txt` and `3rd party.txt` its texts need the LGPL-2.1 text. The contracts already allow `{ "repository": "external/Paneport-wine/COPYING.LIB" }`, the LGPL-2.1 text at the commit that this repository records, which the `ios` job fetches before the notices step.
  - protobuf-net and protobuf-net.Core 3.4.40: they need both the Apache-2.0 terms and their copyright notice. Their description's copyright is only a pointer to the repository, and the notice is in upstream's `Licence.txt` at `3aa672e`, 23 lines with the notice and the Apache-2.0 header. Both come as pinned downloads with M2.3's mapping change: the Apache-2.0 text of section 4, and `Licence.txt` at that commit. They are fetched and not committed because C-0003 section 4, C-0004 section 3 and C-0010 section 6 keep third-party texts out of `LICENSES/` and the source tree, besides C-0009 section 6.
  - ZstdSharp.Port: it is a port of zstd, whose upstream license is BSD-3-Clause or GPL-2.0, but it declares only MIT with the porter's copyright line. Whether zstd's notice must ship is for M2.3's register change; if it must, zstd's `LICENSE` at a pinned commit is the source.
- **Licenses outside C-0003 section 5.** No contract change is proposed, since C-0003's text does not require one now. The coverage review of M9.1 also checks the licenses inside the shipped notices against section 5, and asks recelilious whether C-0003 needs a change: the Unicode data license and a BSD notice with an advertising clause, both in the runtime's `THIRD-PARTY-NOTICES.TXT`; Ms-PL in Avalonia.Controls, if trimming keeps that code; and dotnet/macios' `NOTICE.txt` (1025 lines, mostly build tooling), which its packs do not carry.
- **E-mail addresses in the notices.** Upstream notices keep their authors' e-mail addresses (C-0003 section 5): 7 lines in the runtime's `THIRD-PARTY-NOTICES.TXT`, and 5 each in the native-asset `THIRD-PARTY-NOTICES.txt` of SkiaSharp and HarfBuzzSharp, 12 distinct addresses in all, plus one written as "name at host". Each is written twice per bundle, in the notices file and in the component's text, and M2.3 adds 1 more from SteamKit2's `3rd party.txt`. The texts that this round adds hold none. Once the `ios` job runs the full scan of S9.1.3, its allow-list must accept them, by file and by the SHA-256 of the text (L9 brief, M9.1 risks).
- **Components inside packages.** The notices of SkiaSharp's and HarfBuzzSharp's native-asset packages, and those of the .NET runtime, list further components, which ship as part of their packages. Which of them the iOS libraries contain, such as the libmicrohttpd that the M0.1 note names, stays with the coverage review of M9.1.
- **For later stages.**
  - The MSBuild query passes only `Configuration=Release`, so L10 must keep the choice of the runtime in the project file, or pass the same property to the query, or the query could resolve another pack than the build used.
  - A local run with a stale lock file whose packages are all in NuGet's folder takes the wrong versions; CI's locked restore prevents that.
  - The broad `System.*` and `Microsoft.*` rules would attribute a future NuGet assembly with such a name to the .NET runtime; the mapping of the lock file still lists the package's component.
  - Whether the trimmed iOS app keeps the Ms-PL types of Avalonia.Controls is not verified, for want of a Mac.

## 11. Sources

Public material consulted (C-0001 section 6):

- The packaging of .NET for iOS, whose packs carry the repository's `LICENSE`, and the save of the compiled app manifest as a binary property list: <https://github.com/dotnet/macios/blob/main/dotnet/package/common.csproj>, <https://github.com/dotnet/macios/blob/main/LICENSE> and <https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.MacDev.Tasks/Tasks/CompileAppManifest.cs>
- Apple's open-source binary property list format: <https://github.com/apple-oss-distributions/CF/blob/main/CFBinaryPList.c>
- The package descriptions and file lists of SteamKit2 4.0.0-beta.2, protobuf-net 3.4.40, protobuf-net.Core 3.4.40 and ZstdSharp.Port 0.8.8 in the NuGet catalog: <https://api.nuget.org/v3/catalog0/data/2026.11.05.23.57.36/steamkit2.4.0.0-beta.2.json>, <https://api.nuget.org/v3/catalog0/data/2026.10.21.16.10.29/protobuf-net.3.4.40.json>, <https://api.nuget.org/v3/catalog0/data/2026.10.21.16.08.07/protobuf-net.core.3.4.40.json> and <https://api.nuget.org/v3/catalog0/data/2026.04.29.18.39.05/zstdsharp.port.0.8.8.json>
- The packages of the app's lock file, read in the workstation's NuGet package folder: their `.nuspec` files and license files; the iOS-only packages and packs as [run 44424469253](https://github.com/recelilious/Paneport/actions/runs/44424469253) reported them.
- Avalonia's repository at the commit of 12.1.4, read through the GitHub API: `licence.md`, `NOTICE.md` and the headers of the Apache-2.0 files of Avalonia.Base, <https://github.com/AvaloniaUI/Avalonia/tree/2d79ba55fae1b1e71c69f93a668080c9029e6a31>; and Avalonia.BuildServices' `LICENSE`, <https://github.com/AvaloniaUI/Avalonia.BuildServices/blob/777f975b0a0cecf0311273711d56697212c558c0/LICENSE>
- RichTextKit's `license.txt`, <https://github.com/toptensoftware/RichTextKit/blob/main/license.txt>, and the licenses of foliojs/linebreak and foliojs/unicode-trie, <https://github.com/foliojs/linebreak/blob/c7873ab973ca11576c50e49803b1fecbe531ed96/LICENSE> and <https://github.com/foliojs/unicode-trie/blob/8ab9b14025ff7c26c2aec1bbd1e71d5f83eb3bd1/LICENSE>
- The Apache License 2.0, <https://www.apache.org/licenses/LICENSE-2.0.txt>, the copy at <https://github.com/kubernetes/kubernetes/blob/d30db1f9a915aa95402e1190461469a1889d92be/LICENSE>, and the LGPL-2.1 as Wine's `COPYING.LIB` at the commit that this repository records for `external/Paneport-wine`, the text that M2.3 ships, <https://github.com/recelilious/Paneport-wine/blob/0bec332275e4587e2609c49983d497aa7b104cf3/COPYING.LIB>, for the passages that mark their full terms
- protobuf-net's `Licence.txt` at the commit that M2.3 takes it from: <https://github.com/protobuf-net/protobuf-net/blob/3aa672eaaf7763b86d62bec9ed7d7896b0511a8b/Licence.txt>
- NuGet's reading of a description's license and its naming of a project's lock file: <https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/NuspecReader.cs> and <https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.ProjectModel/ProjectLockFile/PackagesLockFileUtilities.cs>
