# C-0010 Languages and Code Style

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.3 |
| Date | 2026-11-27 |
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
- The managed exception record of C-0005 section 2 holds the exception's type, and the type and method names of its stack frames, and the same for its inner exceptions. It also holds when and where the exception went unhandled, and the build. It never holds an exception's message or data, a file name, a line number or an argument value, because those can hold paths, account names or other personal data. A file name and a line number here mean the source-file information that debug symbols give for a frame.
- An unhandled exception is one that leaves a dispatcher operation, a callback into native code, or a thread, with nothing catching it. A faulted task is not one, and neither is a timer tick whose exception the dispatcher keeps. An unhandled exception ends the app. Code that observes one, such as the code that writes the managed exception record, never marks it as handled and never lets the app go on after it, because Avalonia's layout and rendering do not recover from one.

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

- Follow the upstream project's coding style, so that our diff stays small and easy to rebase. There are two deviations: the header of a new file, which keeps upstream's usual copyright and notice block, names recelilious and the Paneport contributors, and adds the SPDX license line that C-0003 section 4 requires; and the notice in a changed file that the next item describes.
- A file that we change in a fork carries a notice that we changed it when the file, or the fork, is under the LGPL (LGPL-2.1 section 2(b)) or Apache-2.0 (its section 4(b)): every file of Wine or DXMT, for example, and the Wine headers in FEX-Emu's `Source/Windows/include`. Other files, such as FEX-Emu's own files under MIT or a file under MPL-2.0, need no notice, though their changes follow the same commit rules.
  - **Form.** One line for each stage that changes the file, in the file's comment syntax: `Changed for Paneport on <dates>: <what changed, in a few words>.`, where `<dates>` names, in UTC, each date on which that stage changed the file, such as `2026-11-15 and 2026-11-16`. A date is the UTC date on which the fork commits the change, not the author date of work that the change carries, and a rebase that only moves a change keeps its dates (`docs/design/forks.md` sections 4.2 and 4.4). A change that carries another author's work, such as an upstream merge request that is not merged yet, says so on its line.
  - **Place.** The file's first comment block, after upstream's copyright lines. A file that does not start with a comment block, such as one of Wine's `Makefile.in` files, gets the line as a comment of its own at the top.
  - **Commit.** The line comes in the commit that makes the change. When that commit carries another author's work unchanged, the line may come in the commit right after it.
  - **Generated files.** A file that a tool generates, such as Wine's `configure`, needs no line of its own only when the same commit regenerates it, with a tool that C-0003 registers, from a file that carries one. A generated file that we change by hand gets its own line.
  - **Removal.** A line, or the part of it that describes one change, goes when the fork no longer carries that change, for example once upstream merges the request that the change carries.
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
- `LICENSES/MIT.txt` is added together with the first file that carries these lines. `LICENSES/` holds only the licenses that files in this repository use; third-party license texts are generated into releases (C-0009 section 6). C-0003 section 4 sets the same rule.
- New and changed files inside a fork follow section 4.

## 7. Text and file names

- UTF-8 everywhere, and LF line endings except for `.bat` and `.cmd` files, which use CRLF (C-0002 section 7).
- C# files, including file-based tools, use `PascalCase` names. C, C++ and Objective-C files use `snake_case`. Bash scripts and new documentation files use `kebab-case`.
- Exceptions: conventional files keep their usual names (`README.md`, `CONTRIBUTING.md`, `CLAUDE.md`, `THIRD_PARTY_NOTICES.md`, license files); contracts are named `C-NNNN-<title>.md`; a Chinese copy adds the `_zh_CN` suffix (C-0002 section 7); and the research notes already published in `docs/research/` keep their names.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious |
| 1.1 | 2026-11-17 | Accepted by recelilious, together with C-0003 1.6. Comes from stage M4.2, which makes the first changes to files of the Wine fork; LGPL-2.1 section 2(b) asks that a changed file carry a prominent notice with the date of each change. Section 4 says which changed fork files carry the notice: those under the LGPL or Apache-2.0, or in a fork under one of them. It gives the notice's form, one line for each stage with the UTC dates of its changes, its place, the commit it comes in, when a generated file needs a line of its own and when a line goes, and names the notice as the second deviation from upstream style. Section 6 applies section 4 to changed files as well as new ones. |
| 1.2 | 2026-11-23 | Accepted by recelilious, together with C-0003 1.9, C-0004 1.2, C-0006 1.1 and C-0009 1.3. Comes from stage M3.1 and from recelilious's decision of 2026-11-23 that Paneport names StikDebug's built-in `universal.js` in its JIT request and writes no broker script of its own (C-0006 1.1). Section 1 drops the row of the JIT broker script, because Paneport writes no JavaScript. |
| 1.3 | 2026-11-27 | Accepted by recelilious, together with C-0005 1.1. Comes from stage M2.4 and from recelilious's decision of 2026-11-26 that the managed exception record is a stage of its own. Section 2 says what that record may hold: the type and method names of the exception and of its frames, but never its message. It also says what an unhandled exception is, and that nothing goes on after one. |
