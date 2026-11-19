# Release Engineering

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-19T00:12Z |

Back to the [design index](README.md).

This document describes how Paneport's app bundles and releases carry what C-0003 section 3 and C-0009 section 6 ask for. Stage M9.1 writes it (see the [L9 brief](stages/L9-first-release.md)). Its first part comes from S9.1.1, the notices generator, which recelilious brought forward on 2026-11-17 so that the public `.ipa` files of the `ios` job carry the notices of what they ship: where the notices sit in the bundle, the format of their index, the generator and its rules, the attribution mapping, where the license texts come from, and the step of the `ios` job. M9.1 adds the `release` workflow and its modes, the source archives, the full scan with its allow-list, and the version rules. [repository-checks.md](repository-checks.md) describes the other tools, and [ios-app-head.md](ios-app-head.md) section 11 the packaging, signing and scan of the `ios` job. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. The notices in the bundle

C-0009 section 4 names no place for the notices in the bundle, so this document fixes one: the folder `licenses/` at the root of the app bundle.

| File | What it holds |
|---|---|
| `licenses/THIRD_PARTY_NOTICES.md` | A table of the third-party components, then a section for each with its license, its sources, its credit if it has one, and the full text of its license and notices. |
| `licenses/index.json` | The list of the components in the same order, for the app's open-source licenses screen (S9.0.1), with the path of each component's text (section 2). |
| `licenses/texts/NN-<name>.txt` | One file per component: its name, license, sources and credit, then each of its texts under a heading that names the file and the package or runtime pack it comes from. `NN` is the component's place in the order, padded to two digits or more. |

- **Order.** The components appear in the order of their names, compared without regard to case and then exactly, and the same order holds in all three outputs. Each component appears once.
- **Paneport itself** is not listed: its code is under the MIT license (C-0003 section 1), which the About section of S9.0.2 shows. The notices file says that each component keeps its own license, so the app as a whole is not under the MIT license.
- **Before signing.** The `ios` job writes the notices after the build and before `eng/mac/sign-adhoc.sh`, so the ad-hoc signature seals them in `_CodeSignature/CodeResources` with the rest of the bundle, and a sideloading tool that re-signs the app keeps them as it keeps every other file of the bundle; that is not yet verified on the device. The `release` job of M9.1 does the same and attaches the notices file and the texts to the release (C-0009 section 6).
- **Never in git.** The generator writes only into the bundle and, with `--out`, into a build output folder: one outside the repository, or one inside it under `build/` or below a `bin/` or `obj/` folder, which C-0004 section 3 reserves for build output and `.gitignore` ignores. It refuses any other folder. It replaces its own earlier output, which it recognizes by its index, and refuses to touch a `licenses/` folder that it did not write.
- **Plain text.** Every output is UTF-8 without a byte-order mark, with Unix line endings and one line break at its end. A source text may be UTF-8, or UTF-16 with a byte-order mark; one that is empty, not text, or not valid UTF-8 counts as missing. The texts keep every notice as the upstream wrote it, author e-mail addresses included (C-0003 section 5).

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
| `sources` | Each package of the app's lock file that maps to the component, and each package, runtime pack or file of the repository that its texts come from, as `<package ID> <version>` or as a path, sorted. They name no local folder. |
| `credit` | Only when the component has one: the credit that its license asks for in the documentation, such as the FreeType credit of the FTL (C-0003 section 5). |
| `text` | The path of the component's text, relative to the index. |

## 3. The generator

`tools/GenerateNotices.cs` is a C# file-based tool, run from the repository root (C-0010 section 1).

```bash
dotnet run tools/GenerateNotices.cs -- --mode ios --project src/Paneport.iOS/Paneport.iOS.csproj --app <bundle.app> --out build/notices/main
dotnet run tools/GenerateNotices.cs -- --self-test
```

**Inputs.**

| Input | What the generator takes from it |
|---|---|
| The register of C-0003 section 6 | Each component's license and category, read by `tools/Shared/Register.cs`, the parser that `tools/CheckLicenses.cs` uses too (section 7). |
| `eng/license-map.json` | The component of each package of the lock file, read by `tools/Shared/LicenseMap.cs`. |
| `eng/release/bundle-components.json` | The attribution of the bundle's files and the sources of each component's texts (section 5). |
| `LICENSES/MIT.txt` | The standard MIT text, for packages that declare MIT and ship no license file (section 4). |
| The bundle's `Info.plist` | `CFBundleExecutable`, which names the main executable, and the display name and the versions for the notices file. The build writes it in Apple's binary format; the generator reads that format and XML, and only as far as the string values at the top. |
| The app's project | Its lock file, `packages.<project>.lock.json` or `packages.lock.json` beside it, for the packages; and one query of MSBuild, `dotnet msbuild <project> -t:ResolveFrameworkReferences -getItem:ResolvedRuntimePack -getProperty:NuGetPackageRoot -p:Configuration=Release`, for NuGet's package folder and the runtime packs that the build uses, so that the texts come from the packs that the app was built with. |

**Attribution.** The generator lists every file of the bundle, hidden ones too, without following links and without its own `licenses/`. The main executable maps to the components that the mapping names for it, Paneport, the .NET runtime and .NET for iOS, and to the component of every package in the lock file, because full ahead-of-time compilation puts the code of every package into it (C-0009 section 3). So do the files that the mapping lists beside the executable, which the build makes of the whole app in the same way: `aot-instances.aotdata.arm64`, the ahead-of-time data of the generic instances that the compiler shares between all assemblies. The lock file also holds packages that do not ship, such as the trimmer's `Microsoft.NET.ILLink.Tasks` and the native assets of other platforms, and packages whose assembly the trimmer removes whole, such as `MicroCom.Runtime`; their components are listed all the same, which errs on the side of a notice too many. Every other file takes the components of the first rule of the mapping whose path matches it. The components of all the files, Paneport aside, are the components of the notices.

**Rules.** Every finding names the file, component or package; none prints a license text.

| Finding | `ios` mode, the `ios` job | `release` mode, the `release` job of M9.1 |
|---|---|---|
| A file that no rule attributes | Warning | Failure |
| A component that the register lacks | Failure | Failure |
| A component whose row is Under review, Forbidden or a Tool row (C-0003 section 5, C-0009 section 4) | Failure | Failure |
| A component without a license text: no entry in the mapping, a file that is missing or not text, a package that the lock file or NuGet's folder lacks, a runtime pack that the build did not resolve, or an MIT package without a copyright notice | Failure | Failure |
| A package that declares, by its SPDX expression, a license that the register row of its component does not name; the register then needs a change first (C-0003 section 6) | Failure | Failure |
| A NOTICE file at the top of a package or runtime pack of an Apache-2.0 component that is not among its texts (C-0003 section 5) | Failure | Failure |
| An FTL component without a credit (C-0003 section 5) | Failure | Failure |
| An LGPL component other than SteamKit2 in the main executable (C-0003 section 3: only SteamKit2 is compiled into the executable; other LGPL code ships as libraries that the user can replace) | Failure | Failure |
| A package of the lock file that `eng/license-map.json` does not map | Failure | Failure |
| An executable that `Info.plist` names and the bundle lacks | Failure | Failure |

A license counts as named by a register row when its identifier stands in the row's License text as a whole name: `MIT` counts in "MIT. Its native-asset packages ..." but not in `MIT-Modern-Variant`. After a failure the generator writes nothing. Exit codes: 0 when the notices are written, 1 after a failure, 2 for a wrong argument or an input that cannot be read.

**Report.** It prints the runtime packs, the coverage of every file with its components, the license and notice files anywhere in the packages and runtime packs of the notices that are not among their texts, the rules that match no file, each component with its number of files, texts and sources, and every warning and failure; under GitHub Actions each warning and failure is also an annotation. The list of license and notice files is for the coverage review of M9.1: it shows which packages carry notices of their own, for example for the native libraries inside them, and is not a failure.

**Self-test.** `--self-test` builds its fixtures at run time in a temporary folder and deletes them: a register, a license map and a mapping of made-up components, packages with their descriptions and texts, two runtime packs, a repository file, and a bundle with a binary `Info.plist`. The fully registered bundle passes in both modes; its three outputs list each component once in the same order; the main executable maps to SteamKit2 among the packages of the lock file; the MIT text of a package without a license file carries the package's copyright line; the Apache-2.0 NOTICE file and the FreeType credit are kept; the outputs name no local path; and the generator writes only into the bundle's `licenses/` and the output folder, replaces its own output and refuses a folder that it did not write; and the report lists a notices file of a package that is not among its texts. Then each failure of the table is planted on its own, and each must fail and name what fails; an unattributed file, a hidden one too, must warn in `ios` mode and fail in `release` mode. The readers have checks of their own: binary and XML property lists, truncated and damaged ones, the path patterns, the mapping and its rejections, MSBuild's answer, a lock file, SPDX expressions and the MIT text. The `ios` job runs the self-test before it uses the generator, and it runs on Windows as well.

## 4. Where the texts come from

Each component's texts come from one of four kinds of source, which the mapping names (section 5):

| Source | Text |
|---|---|
| A file of a package of the lock file | The file in NuGet's package folder, such as `LICENSE.txt` or `THIRD-PARTY-NOTICES.txt`. |
| A package that declares an expression | For MIT only: the standard MIT text of `LICENSES/MIT.txt` with its copyright line replaced by the `copyright` of the package's description (its `.nuspec`), which must declare the expression `MIT` and a copyright notice. |
| A file of a runtime pack | The file in the folder of the runtime pack that MSBuild resolved for the framework, such as `Microsoft.NETCore.App` or `Microsoft.iOS`. |
| A file of this repository | A path below the repository root, such as a license file of a fork under `external/`. |

This answers the M9.1 question of where the full text and the copyright line come from for a package that declares only a license expression: from the package's own files when it has any, and for MIT from the standard text and the copyright of its description. The generator builds no other license from an expression; a component whose packages carry no text of another license fails as a missing license text (section 10).

The packages that the app ships carry these texts, as the mapping lists them:

| Component | Texts |
|---|---|
| .NET runtime | `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` of the runtime pack of `Microsoft.NETCore.App`, Mono's for `ios-arm64`. The notices list the third-party code of the runtime, such as Mono's, zlib-ng, Brotli and the Unicode data. |
| .NET for iOS (dotnet/macios) | `LICENSE` of its two runtime packs, `Microsoft.iOS` with the bindings and `Microsoft.iOS.Runtimes` with the native code for `ios-arm64`: the MIT license of dotnet/macios, followed by the notices of the third-party code that it carries. The two files are alike, so the notices hold the text once, under a heading that names both packs. |
| Avalonia, Avalonia.BuildServices, MicroCom.Runtime | They declare MIT and ship no license file, so their texts are the MIT text with the copyright of their description. |
| CommunityToolkit.Mvvm | `License.md` and `ThirdPartyNotices.txt` of the package. |
| SkiaSharp, HarfBuzzSharp | `LICENSE.txt` of the managed package and `THIRD-PARTY-NOTICES.txt` of the iOS native-asset package, which lists the further components of the native library, as the register rows say. |

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
    }
  }
}
```

- **The main executable.** `mainExecutable` names the components of the executable, besides those of the packages of the lock file, a `note`, and in `paths` the other files that the build makes of the whole app and that map in the same way.
- **Rules.** Each rule has a `path` relative to the bundle, its `components` and a `note` that says why. In a path, `*` matches within one part, `?` one character of it, and `**`, which must be a whole part, any number of parts: `Frameworks/x.framework/**` is everything inside that framework, and `**/x` an x in any folder. The first rule that matches a file attributes it, so `Microsoft.iOS.*`, which belongs to .NET for iOS, comes before `Microsoft.*`, which belongs to the .NET runtime. `Paneport` stands for our own code.
- **Texts.** Each component of the notices needs an entry with at least one text, each written as `{ "package", "file" }`, `{ "package", "license": "MIT" }`, `{ "runtimePack", "file" }` or `{ "repository" }`, and may have a `credit` and a `note`. Paths stay below their folder. A text that two sources hold alike is written once, under a heading that names both.
- **Strict.** The generator rejects an unknown key, a rule without a note or components, a component named twice, a path that leaves its folder, a pattern with a partial `**`, and texts for Paneport.
- **Adding files to the bundle.** A stage that adds files adds rules for them, and for a new component its texts, after the component is registered (C-0003 section 5). The `ios` job warns about an unattributed file and fails on a component that is not registered or not allowed to ship (L9 brief, M9.1 hand-over).

## 6. The ios job

Between building both flavors and signing them, the `ios` job finds both bundles, runs the generator's self-test, and runs the generator in `ios` mode on each flavor with its own project: `src/Paneport.iOS/Paneport.iOS.csproj` for the main app and `tests/device/Paneport.DeviceTest.csproj` for the device test flavor, with the copies in `build/notices/main` and `build/notices/test`. After packaging and the scan, it checks that each `.ipa` holds `licenses/THIRD_PARTY_NOTICES.md`, `licenses/index.json` and every text that the index names. A failure in either step keeps the `.ipa` files from being uploaded, as every failed step does. [ios-app-head.md](ios-app-head.md) section 11 lists all the steps.

## 7. Shared code between tools

The M9.1 brief left open how the tools of the stage reuse the register parsing of M0.2's tools. They share source files: the register parser and the parser of `eng/license-map.json` live in `tools/Shared/Register.cs` and `tools/Shared/LicenseMap.cs`, and `tools/CheckLicenses.cs` and `tools/GenerateNotices.cs` include both with `#:include`, a directive of file-based apps that SDK 10.0.403 accepts. A program of several files needs a `#!` line at its top, or the analyzer warns (CA2266), which the build treats as an error; C-0010 section 6 puts the SPDX lines right after it. The SDK applies the `#:` directives of an included file to the tool that includes it, so the license checker fails on an `#:include` of anything but a `.cs` file in `tools/Shared/`, and on any `#:` directive in a shared file ([repository-checks.md](repository-checks.md) sections 2 and 4). The detectors of `tools/ScanPersonalData.cs`, which the full scan of S9.1.3 reuses, can move into `tools/Shared/` the same way.

## 8. Confirmed in this stage

- SDK 10.0.403 accepts `#:include` without an opt-in; only `#:ref` is experimental. It applies an included file's `#:package` directives: a restore with an included file that names an unknown package fails. A program of several files gets CA2266 without a `#!` line, and none with it.
- `dotnet msbuild <project> -t:ResolveFrameworkReferences -getItem:ResolvedRuntimePack -getProperty:NuGetPackageRoot` prints the folder, package ID and version of each runtime pack and NuGet's package folder as one JSON object, without building; on the workstation it ran from inside the generator.
- Of the packages that the app ships, Avalonia's, Avalonia.BuildServices and MicroCom.Runtime declare MIT and ship no license file; SkiaSharp and HarfBuzzSharp ship `LICENSE.txt` and their native-asset packages `THIRD-PARTY-NOTICES.txt`; CommunityToolkit.Mvvm ships `License.md` and `ThirdPartyNotices.txt`; the .NET runtime packs ship `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT`; and the packs of .NET for iOS ship the `LICENSE` of dotnet/macios, which holds its third-party notices.
- The build writes the app's `Info.plist` in the binary format: .NET for iOS saves the compiled manifest as a binary property list.
- In the first run of the `ios` job ([run 44424469253](https://github.com/recelilious/Paneport/actions/runs/44424469253)), each flavor's bundle held 125 files before signing: the executable, 56 assemblies, each with its ahead-of-time data, `aot-instances.aotdata.arm64`, `Info.plist`, `PkgInfo`, `runtimeconfig.bin` and three frameworks, the host's and those of SkiaSharp and HarfBuzzSharp, which arrive with a `_CodeSignature` of their own. Every file but `aot-instances.aotdata.arm64` matched a rule, and the notices list eight components. With the notices, each `.ipa` holds 137 entries, and `tools/ScanPackage.cs` finds nothing in either. MSBuild resolves three runtime packs: `Microsoft.NETCore.App.Runtime.Mono.ios-arm64` 10.0.14, and for .NET for iOS `Microsoft.iOS.Runtime.ios.net10.0_27.1` and `Microsoft.iOS.Runtime.ios-arm64.net10.0_27.1` 27.1.10018. No ICU data ships: the bundle holds no `icudt.dat`. `MicroCom.Runtime.dll` does not ship either, because the trimmer removes it whole, but its package stays in the lock file, so the executable still maps to it.

## 9. Not yet in this document

M9.1 adds the `release` workflow and its modes, the build from the tag, stripping, the source archives, the full scan of C-0009 section 6 with its allow-list, the checksums, the composed release notes and the version rules.

## 10. Open items

- **Texts of other licenses declared only as expressions.** For a package that declares a license other than MIT by its expression and ships no license file, the generator has no text and fails. M2.3 brings the first: protobuf-net and protobuf-net.Core 3.4.40 declare Apache-2.0 and ship neither a license file nor a NOTICE file. SteamKit2 itself ships `license.txt` and `3rd party.txt`. A source for the standard Apache-2.0 text, such as committed standard license texts or a pinned download, needs recelilious's decision, because C-0009 section 6 keeps the generated texts out of the repository; it is wanted before M2.3 merges.
- **E-mail addresses in the notices.** Upstream notices keep their authors' e-mail addresses (C-0003 section 5); the notices of the .NET runtime already hold one. Once the `ios` job runs the full scan of S9.1.3, its allow-list must accept them, by file and by the SHA-256 of the text (L9 brief, M9.1 risks).
- **Components inside packages.** The notices of SkiaSharp's and HarfBuzzSharp's native-asset packages, and those of the .NET runtime, list further components, which ship as part of their packages. Which of them the iOS libraries contain, such as the libmicrohttpd that the M0.1 note names, stays with the coverage review of M9.1.

## 11. Sources

Public material consulted (C-0001 section 6):

- The packaging of .NET for iOS, whose packs carry the repository's `LICENSE`, and the save of the compiled app manifest as a binary property list: <https://github.com/dotnet/macios/blob/main/dotnet/package/common.csproj>, <https://github.com/dotnet/macios/blob/main/LICENSE> and <https://github.com/dotnet/macios/blob/main/msbuild/Xamarin.MacDev.Tasks/Tasks/CompileAppManifest.cs>
- Apple's open-source binary property list format: <https://github.com/apple-oss-distributions/CF/blob/main/CFBinaryPList.c>
- The package descriptions and the file lists of SteamKit2 4.0.0-beta.2, protobuf-net 3.4.40 and protobuf-net.Core 3.4.40 in the NuGet catalog: <https://api.nuget.org/v3/catalog0/data/2026.11.05.23.57.36/steamkit2.4.0.0-beta.2.json> and <https://api.nuget.org/v3/catalog0/data/2026.10.21.16.08.07/protobuf-net.core.3.4.40.json>
- The packages of the app's lock file, read in the workstation's NuGet package folder: their `.nuspec` files and license files.
