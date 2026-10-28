# C-0010 Languages and Code Style

| Field | Value |
|---|---|
| Status | Draft |
| Version | 0.1 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract fixes which languages each part of Paneport uses and how code in them is written.

## 1. Languages and runtime

| Area | Language | Version |
|---|---|---|
| App shell, services, Steam, interop bindings, tests, tools | C# | C# 14 on .NET 10 |
| .NET runtime on iOS | Mono, the default runtime of `net10.0-ios` | .NET 10 is the last .NET for iOS release that supports Mono: .NET 11 rejects Mono iOS builds, and .NET for iOS 10 is supported until 2027-05-11. Moving to CoreCLR needs a change to this contract and to C-0005, because CoreCLR uses `SIGUSR1` on Apple platforms. That move is planned before 2027-05-11 (C-0012 section 3). |
| UI markup | AXAML | Avalonia 12 |
| Native host | C, with Objective-C where Apple APIs need it | C17; Objective-C with ARC |
| Dock host | C | C17, built as a Windows program with llvm-mingw |
| Code inside the forks | The upstream project's language and standard | As upstream |
| JIT broker script | JavaScript | The language level that StikDebug's script interface supports, recorded in the broker's design document |
| Build steps that run only on macOS | Bash | Bash 3.2, which macOS ships |
| Cross-platform tools | C# file-based apps, run with `dotnet run <file>.cs` | .NET 10 |
| Build and configuration files | CMake, MSBuild XML, GitHub Actions YAML, JSON and JSON Schema (draft 2020-12), Xcode configuration files | Versions pinned under C-0009 section 2 |

Our own code and scripts are never written in Swift, Kotlin, Python or Rust. Build tools that the forks require, such as Meson for DXMT, may run on the macOS build host.

Two languages have planned conditional paths:

- **Swift**, for the app shell, only if the signal dispatcher gate fails and recelilious adopts that fallback (C-0012 section 3).
- **Rust**, only if an in-app JIT enabler based on idevice is adopted through a change to C-0006.

In either case this contract changes at the same time to add the language's version, style and checks. Adding any other language also needs a change to this contract.

## 2. C#

- Nullable reference types are enabled everywhere, and warnings are errors.
- Analysis level is `latest-recommended`.
- Libraries set `IsAotCompatible`, which also makes them trimmable, so the trimming and AOT analyzers report problems in our own code as build errors. The analyzers do not look inside dependencies, so CI also publishes a trimmed test app that roots our libraries (C-0011 section 4).
- Paneport.Steam may suppress a trimming or AOT warning only where it comes from a SteamKit2 or protobuf-net API, with `[UnconditionalSuppressMessage]` and a justification that cites C-0009 section 3. Each such suppression goes once SteamKit2 is clean under AOT.
- Projects under `src/` use no reflection-based serialization (JSON goes through `System.Text.Json` source generators), no `dynamic` and no `Reflection.Emit`.
- Unsafe code and `LibraryImport` declarations are allowed only in `Paneport.Interop`, the only project that sets `AllowUnsafeBlocks`.
- No `async void`, except for UI event handlers.
- File-scoped namespaces and one public type per file, with the file named after the type.
- Naming follows the C# identifier naming conventions on Microsoft Learn: `PascalCase` for types, namespaces, public members and constants, `_camelCase` for private and internal instance fields, an `I` prefix for interfaces.
- `.editorconfig` defines the formatting, and `dotnet format` applies it.
- View models use the MVVM source generators of CommunityToolkit.Mvvm, once it is registered under C-0003.
- Managed logging goes through a logging interface defined in Paneport.Core. No log message, metric or crash record contains a password, token or other personal data.

## 3. C, Objective-C and C++ in our own code

- A `.clang-format` file at the repository root defines formatting: LLVM style with four-space indentation and a 100-column limit.
- A `.clang-tidy` file at the repository root defines the static checks.
- Our code compiles with `-Wall -Wextra -Wpedantic -Werror`. Headers from Apple's SDK, mingw-w64 and the Wine fork are included as system headers, so these flags apply only to our own code.
- Names declared in `src/native/include/paneport/` follow C-0005 section 1. A module that implements a Windows API exports exactly the names that API and Wine's conventions require, and nothing else. Other C names use `snake_case`, and macros use `UPPER_SNAKE_CASE`. Objective-C follows Apple's naming conventions.
- No variable-length arrays. No mutable global state, except documented process-wide singletons in the native host.
- Every function in a public header has a comment that states its purpose, its thread rule and who owns the memory it takes or returns.
- C++ exceptions never cross a C interface.
- Native code logs only through the host's log callback (C-0005).

## 4. Code inside the forks

- Follow the upstream project's coding style, so that our diff stays small and easy to rebase. The one deviation is the header of a new file: it keeps upstream's usual copyright and notice block, names recelilious and the Paneport contributors, and adds the SPDX license line that C-0003 section 4 requires.
- Keep iOS-specific code in separate, clearly named files where the upstream structure allows it, for example files ending in `_ios.c`.
- Fork commits follow the upstream project's subject convention, and their message explains why the change is needed for iOS.
- The root `.clang-format`, `.clang-tidy` and `.editorconfig` apply only to `src/`, `tests/` and `tools/`. Each fork carries its own configuration that matches upstream, and the format and analysis checks in C-0011 exclude `external/`.

## 5. Comments and documentation

- Comments and documentation are in English. A `_zh_CN` copy of a document exists only when recelilious asks for one (C-0002 section 7).
- Comments explain why, not what.
- Public C# APIs have XML documentation comments. The host interface headers are documented in Doxygen style.

## 6. License headers

Every original file that has a comment syntax starts with these two lines as comments, placed right after the `#!` line where the file needs one:

```
SPDX-FileCopyrightText: <year the file was created> recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
```

- Files that cannot carry comments, such as JSON and images, and the documentation under `docs/`, are covered by `REUSE.toml` at the repository root, which declares the same copyright text and license.
- `LICENSES/MIT.txt` is added together with the first file that carries these lines. `LICENSES/` holds only the licenses that files in this repository use; third-party license texts are generated into releases (C-0009 section 6). This depends on the C-0003 change proposed alongside this contract.
- New files inside a fork follow section 4.

## 7. Text and file names

- UTF-8 everywhere, and LF line endings except for `.bat` and `.cmd` files, which use CRLF (C-0002 section 7).
- C# files, including file-based tools, use `PascalCase` names. C, C++ and Objective-C files use `snake_case`. Bash scripts and new documentation files use `kebab-case`.
- Exceptions: conventional files keep their usual names (`README.md`, `CONTRIBUTING.md`, `CLAUDE.md`, `THIRD_PARTY_NOTICES.md`, license files); contracts are named `C-NNNN-<title>.md`; a Chinese copy adds the `_zh_CN` suffix (C-0002 section 7); and the research notes already published in `docs/research/` keep their names.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
