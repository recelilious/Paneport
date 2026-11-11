# Forks

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-10T08:57Z |

Back to the [design index](README.md).

This document records how Paneport keeps the code it derives from Wine, FEX-Emu and DXMT: the three fork repositories (C-0003 section 4), the upstream versions they start from (decision D5), how their branches and this repository's submodule pointers move, the rule for contributing upstream, the build tools that the forks need on the Mac and how they are pinned, the first fork targets that CI runs, and the plan for caching fork builds in CI. Stage M4.1 wrote it (see the [L4 brief](stages/L4-first-windows-program.md)). It builds on [dev-environment.md](dev-environment.md) and [repository-checks.md](repository-checks.md). Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. The forks

| Fork repository | Upstream | License | Submodule in this repository |
|---|---|---|---|
| `recelilious/Paneport-wine` | Wine, through `wine-mirror/wine`, WineHQ's GitHub mirror of `gitlab.winehq.org/wine/wine` | LGPL-2.1-or-later | `external/Paneport-wine` |
| `recelilious/Paneport-FEX` | `FEX-Emu/FEX` | MIT | `external/Paneport-FEX` |
| `recelilious/Paneport-dxmt` | `3Shain/dxmt` | LGPL-2.1-or-later since 2026-04-25; MIT up to release 0.80 | `external/Paneport-dxmt` |

- **Created on 2026-11-10** as GitHub forks under recelilious's account, after recelilious confirmed the names (D4 in [plan section 4.2](development-plan.md)), with `gh repo fork <upstream> --fork-name <name> --default-branch-only --clone=false`. GitHub makes the copy on its side, so nothing large is uploaded, and each fork shows where it came from.
- **Public.** A fork of a public repository is public, as a release needs (C-0012 section 5).
- **Upstream's files stay.** Each fork keeps upstream's license files and copyright notices as they are (C-0003 section 4), and upstream's `CLAUDE.md` and `AGENTS.md` too (section 4.3).
- **No upstream CI.** GitHub does not run the workflows that a fork inherits until its owner enables them in the fork's Actions tab. No run started in the three forks when their branches were pushed, and they stay off.
- **The default branch** is `paneport/integration` (section 4). GitHub's copy of upstream's default branch from the day of creation, `master` for Wine and `main` for FEX and DXMT, stays as it was, and nothing uses it.
- **Pushing.** GitHub refuses, through its API, a branch whose workflow files differ from those of the default branch, unless the token carries the `workflow` scope, and answers with "Not Found". FEX's integration branch was therefore pushed over SSH, which that rule does not reach; Wine's and DXMT's were created through the API.

## 2. Versions (D5)

D5 asks for "the newest versions that work together" (plan section 4.2, 2026-10-29). This stage first proposed Wine 11.21 with FEX-2611, the newest of each; an independent check of that proposal's facts favored the newest pair that is reported working on Apple silicon, and recelilious approved it on 2026-11-10:

| Fork | Base | Commit | Released |
|---|---|---|---|
| Wine | `wine-11.20`, a development release | `7298a818b79446bae153b8d5aa27235ab3703bf9` | 2026-10-16 |
| FEX-Emu | `FEX-2610`, a monthly release, with two commits of FEX-2611 | `14c92681f4d62cf84d901460e0358de09c8847a7`, then `4dbb046d62c5` and `3a2b1c0b438f` | 2026-10-07 |
| DXMT | `main` at a commit on which DXMT's own CI passed; no release since 0.80 | `6e69d0f9429056c65de15445af7148d7f75c9876` | 2026-11-09 |

The toolchain that goes with them is llvm-mingw 20261104, with LLVM 23.1.5 (section 6).

- **Wine 11.20 with FEX-2610.** It is the newest pair that a public project reports working on Apple silicon macOS: FEX-darwin (MIT, registered in C-0003 section 6) pins FEX-2610 and builds it against Wine 11.20 with a companion patch (its provenance record of 2026-11-08). No public source shows Wine 11.21 with FEX-2611 working there.
- **Why not FEX-2611.** FEX-2611 turns `preserve_all` on for MinGW when clang is 22.1 or newer, as llvm-mingw 20261104 is (`c76fccf256a1`, 2026-11-03), and a day later turned off asynchronous unwind tables for that case (`ef006bdb10b0`). That path is days old, and FEX-2610 predates it. The fork does not take those two commits.
- **The two commits of FEX-2611 that the fork does take,** `4dbb046d62c5` and `3a2b1c0b438f`, fix the bounds check of the ntdll redirection table in `CheckCall`, which let an offset equal to the table's size through and read one entry past its end. Both change only `Source/Windows/ARM64EC/Module.S`, and both are cherry-picked with `-x`, so each names its upstream commit.
- **Why not Wine 11.21.** Native arm64 macOS support is upstream merge request 11638: still open, last updated 2026-08-14. GitLab's merge check for it is stale, but a conflict is near certain on either base, so the fork carries a rebased copy from M4.2 on. Wine 11.21 removed old macOS code in `dlls/ntdll/unix/virtual.c` and `loader.c` (`b98a0a7bef54`, `efa29db4d4b6`), files that the request edits, so the request moves less onto 11.20. Wine 12.0, due around January 2027, will be about 1,100 commits past either base, so the choice hardly changes that later move.
- **Why not the stable Wine 11.0.** Wine 11.0 (2026-01-13) brought full new-WoW64 support and 4 KB page simulation on 16 KB hosts ([wine_on_ios.md](../research/wine_on_ios.md) KQ2), and ARM64EC has improved in the development releases since. The Wine branch that the FEX wiki recommends for ARM64EC, `bylaws/wine` `upstream-arm64ec`, last changed on 2025-08-21 and is 7,692 commits behind 11.20.
- **DXMT `main`.** Release 0.80 (2026-04-23) is the last under MIT; `main` is LGPL-2.1-or-later. C-0003 section 2 says that Paneport follows current DXMT versions and treats DXMT as LGPL, and its register row already says so. DXMT builds for ARM64EC since 2026-03 (`build-arm64ec.txt`), and its own CI builds that side with upstream llvm-mingw 20260908 against a build of Wine 11.2 whose release note says it does not work yet. DXMT's CI only builds, so no runtime test of its ARM64EC side exists anywhere; M5.2 runs the first. `main` has moved one commit past the pin since (`0df86108348b`, 2026-11-10); the fork stays at the approved commit.
- **llvm-mingw 20261104** is the newest release. The `bylaws/llvm-mingw` fork that the FEX wiki names has had no release since 2025-09, while upstream llvm-mingw releases carry ARM64EC and DXMT's CI uses them.

**Consequences for later stages.** M4.2 rebases merge request 11638 onto Wine 11.20. It changes `configure.ac`, so M4.2 regenerates `configure` with GNU Autoconf 2.73, the version that generated Wine 11.20's, and pins it. FEX-darwin's patches are written against FEX-2610, the fork's base, so they apply as written; the one that FEX-darwin imports under MPL-2.0 is a Conditional component (C-0003 section 5) and stays out until the register covers it (section 11).

## 3. What each integration branch carries

`paneport/integration` starts at the base of section 2 and carries the commits below and nothing else. Changes of Paneport's own to code start with M4.2, on stage branches (section 4).

| Fork | Commits on top of the base |
|---|---|
| Wine | `0bec332` "Add a .clang-format file that turns clang-format off." |
| FEX | `90909f3` and `8034aea`, the two cherry-picks of section 2; `a827aea` "Repo: Add an .editorconfig that matches .clang-format"; `0192e62` "External: Don't fetch the test binary submodules" |
| DXMT | `a640a10` "chore: add .editorconfig matching .clang-format"; `03d8683` "build: do not fetch the nvapi submodule" |

| Fork | Its own `.clang-format` | Its own `.editorconfig` | `.gitmodules` |
|---|---|---|---|
| Wine | Added. Wine has no formatter and no `.clang-format`, so the file turns clang-format off (`DisableFormat: true`, `SortIncludes: Never`) and every file keeps its own style. | Upstream's, which already says `root = true`. | Wine has no submodules. |
| FEX | Upstream's. | Added: `root = true`, and for C and C++ files the indentation and line length of upstream's `.clang-format`, 2 spaces and 140 columns. | `update = none` for the three repositories of test programs (section 5.3). |
| DXMT | Upstream's, which keeps clang-format's default LLVM style. | Added: `root = true`, and for C, C++ and Objective-C files that style's 2 spaces and 80 columns. | `update = none` for `external/nvapi` (section 5.3). |

- **Why.** The root `.clang-format`, `.clang-tidy` and `.editorconfig` of this repository apply only to `src/`, `tests/` and `tools/` (C-0010 section 4). clang-format uses the nearest `.clang-format` above a file, and an `.editorconfig` with `root = true` stops editors from reading the ones above it, so each fork needs both files of its own, or this repository's style would reach into it.
- **Headers.** A new file in a fork starts with two comment lines, the copyright of recelilious and the Paneport contributors and the SPDX identifier of upstream's license (C-0003 section 4, C-0010 section 4).
- **Commits.** Each follows its upstream's subject style: Wine's `area: Sentence.` (a root file has no area), FEX's `Area: Sentence` and DXMT's `type: sentence`. Its message says why Paneport needs the change. A cherry-pick keeps upstream's author and message and names its upstream commit. Like every commit Claude takes part in, each ends with the co-author line of C-0002 section 4, which is never removed (C-0003 section 10).

## 4. Branches, pointers and rebases

### 4.1 Branches in a fork

| Branch or tag | What it holds |
|---|---|
| `paneport/integration` | The fork's default branch: the base of section 2 and every change of a merged stage. It moves only by fast-forward, except at a rebase (section 4.4). |
| `paneport/<stage ID>`, such as `paneport/M4.2` | The changes of one stage, branched from `paneport/integration` (plan section 3.3). |
| `paneport/archive/<base>`, such as `paneport/archive/wine-11.20` | A tag on the last commit of `paneport/integration` before a rebase moved it to another base. |

### 4.2 How a pointer moves

1. The stage session commits in the fork on `paneport/<stage ID>` and pushes that branch.
2. The stage branch of this repository moves the submodule pointer to the fork's new commit and says in the pull request which fork commits it brings. CI builds the fork at that commit.
3. When recelilious merges the pull request, the fork's `paneport/integration` is fast-forwarded to the same commit, by recelilious or by Claude when recelilious asks, as for any merge (C-0002 section 2). The fork's stage branch is deleted afterwards.
4. Every fork commit that a revision of `main` has ever recorded stays reachable from a branch or tag of its fork. That is why a fork's stage branch is deleted only once its commits are on `paneport/integration`, and why a rebase tags the old tip first: a release attaches the source of every fork at exactly the recorded commit (C-0003 section 3, C-0009 section 6).

When two stages change the same fork at the same time, the one merged second rebases its fork branch onto the new `paneport/integration`, moves its pointer again and runs its checks again.

### 4.3 The upstream rule and the provenance of fork changes

C-0003 section 10: check a project's current policy before submitting anything upstream, never remove AI attribution, and never propose FEX changes upstream. As of 2026-11-10 all three upstreams refuse AI-generated code:

| Upstream | Policy |
|---|---|
| FEX-Emu | Forbids AI-generated code in contributions, in its `AGENTS.md`, `CLAUDE.md` and `CONTRIBUTING.md` (C-0003 section 10). |
| DXMT | Since 2026-07-27, its `AGENTS.md` says AI must not be used to generate code for contributions, and its `CONTRIBUTING.md` that it cannot accept contributions made or co-authored by AI. |
| Wine | Its Clean Room Guidelines, as edited on 2026-04-14, tell contributors not to use an LLM tool to generate code. |

- **Nothing goes upstream.** Paneport's changes in all three forks stay in the forks and are never proposed upstream. C-0003 section 10 lists all three since version 1.4, which recelilious accepted on 2026-11-10.
- **Provenance.** Wine's guidelines give their reason: there is no guarantee that an LLM's training material respects Wine's clean-room rules, so its output may reproduce Microsoft source code, disassembly or ReactOS code. The same risk applies to the AI-written changes in Paneport's forks. Those changes therefore stay integration code, written from public documentation only, and nobody writing them reads Microsoft or ReactOS source code or disassembly. The professional review that C-0003 recommends before a formal public release covers this risk. C-0003 1.4 writes both rules into section 10 and the preamble.
- **The upstreams' policy files stay.** FEX's `CLAUDE.md` and `AGENTS.md` and DXMT's `AGENTS.md` are one line each, refusing AI-made contributions. They govern contributions to the upstream projects, which Paneport never sends, and the forks keep them unchanged. Claude Code loads a `CLAUDE.md` in such a tree as project instructions; a session that works in a fork reads it as upstream's contribution policy, which its work does not touch.

### 4.4 Moving to a new upstream base

A new base is a decision like D5: the session proposes it with reasons, recelilious approves, and section 2 records the new base with its date. Reasons to move are a fix that a stage needs, a fork that needs a newer version of another to keep working with it, and a stable Wine release such as 12.0.

1. Tag the tip of `paneport/integration` as `paneport/archive/<old base>` and push the tag.
2. Rebase the fork's own commits onto the new base on a branch `paneport/rebase-<new base>`, keeping each commit's message and co-author line, and dropping a cherry-pick that the new base already holds.
3. A stage branch of this repository moves the pointer to the rebased tip, and CI builds it and runs the harness from M4.2 on.
4. Once recelilious merges that pull request, `paneport/integration` is moved to the rebased tip with `git push --force-with-lease`, the only move of that branch that is not a fast-forward.

## 5. Submodules

### 5.1 `.gitmodules`

Each fork is one entry, such as:

```
[submodule "external/Paneport-wine"]
	path = external/Paneport-wine
	url = https://github.com/recelilious/Paneport-wine.git
	branch = paneport/integration
	shallow = true
```

- The address is https, so that a clone works without an SSH key, in CI too.
- `branch` names the branch that `git submodule update --remote` follows; a clone checks out the recorded commit.
- `shallow = true` makes `git clone --recurse-submodules` and `git submodule update --init` fetch each fork at depth 1. GitHub serves a recorded commit by its hash, also when it is not the tip of a branch.
- `eng/license-map.json` maps each submodule path to its register row, Wine, FEX-Emu or DXMT, which `tools/CheckLicenses.cs` checks ([repository-checks.md](repository-checks.md) section 4).
- The repository checks do not reach into `external/` (C-0010 section 4): REUSE skips submodules, `eng/lychee.toml` excludes the folder, the personal-data scan and the license checker read only the files that git lists for this repository, where a submodule is one entry, and the format and analysis checks name their own folders.

### 5.2 Cloning

`git clone --recurse-submodules https://github.com/recelilious/Paneport.git` checks out every fork at its recorded commit, and `git submodule status` lists them. A plain clone leaves the three folders empty, which is enough for everything except the fork builds; `git submodule update --init` fetches them later. The recursion also fetches the forks' own submodules (section 5.3), except those marked `update = none`. On the workstation on 2026-11-10 it took 113 s and about 1.4 GB on disk: 418 MB for Wine, 592 MB for FEX with its submodules, most of which keep their whole history, 9 MB for DXMT and 408 MB in `.git`.

On Windows, the trees of the three upstream commits hold no name that Windows refuses (such as `aux` or `con`, or a name ending in a dot), no two names that differ only in letter case, and no path longer than 113 characters. The recursion goes deeper in `.git/modules`, though: the third level of submodules (section 5.3) puts files up to 149 characters below the clone's folder, such as `.git/modules/external/Paneport-FEX/modules/External/zydis/modules/dependencies/zycore/objects/pack/pack-<hash>.keep`. Windows refuses paths longer than 260 characters unless long paths are on, so a recursive clone needs a folder whose path has at most about 110 characters, as a folder near the top of a drive has, or `git config --global core.longpaths true`. A clone into a folder of 115 characters failed on 2026-11-10 with "Filename too long", and one into a folder of 15 characters passed (section 7).

### 5.3 The forks' own submodules

| Fork | Submodule | License, as its repository states it | Fetched |
|---|---|---|---|
| FEX | `External/fex-posixtest-bins`, `External/fex-gcc-target-tests-bins`, `External/fex-gvisor-tests-bins` | No license file at their roots. They hold Linux test programs built from the Open POSIX Test Suite, whose GPL-2.0 texts lie below the root, from GCC's test suite, and from gVisor's tests, which are Apache-2.0. | Never: `update = none`. C-0003 section 5 keeps code without a license and GPL code out of this repository, the forks and the app, and Paneport does not run FEX's Linux tests. |
| FEX | `External/fmt`, `External/unordered_dense`, `External/zydis` | MIT | Yes |
| FEX | `External/xxhash` | BSD-2-Clause for the library; its command-line tool is GPL-2.0, and FEX turns its build off | Yes |
| FEX | `External/Catch2`, `External/range-v3` | BSL-1.0 | Yes |
| FEX | `External/vixl`, `External/jemalloc_glibc`, `External/tracy`, `External/Vulkan-Headers`, `External/drm-headers`, `Source/Common/cpp-optparse`, `External/rpmalloc` | Permissive licenses, to be read before a build first compiles one into a module that ships | Yes |
| DXMT | `include/native/directx`, `misyltoad/mingw-directx-headers` at `9df86f23` | mingw-w64's DirectX headers, generated by WIDL from IDL files largely from Wine; LGPL-2.1, as its README says | Yes. DXMT's build needs them: Meson stops without `d3d11.h`, and they are on the include path of the Mac side. C-0003 1.4 registers them. |
| DXMT | `external/nvapi` | NVIDIA's NVAPI SDK, MIT, with prebuilt libraries | Never: `update = none`. It is a vendor SDK, which C-0003 section 5 forbids whatever its license, and DXMT builds it only with `enable_nvapi` or `enable_nvngx`, which stay off. |

Two of them have submodules of their own, which a recursive clone fetches as well: range-v3 holds its documentation site as `doc/gh-pages`, a second copy of range-v3 at its `gh-pages` branch, and zydis holds `dependencies/zycore`, zycore-c under MIT. FEX's MinGW build needs neither. None of FEX's 13 fetched submodules has a row in the register yet. Each one that a module which ships compiles needs a row before that build (section 10). The MinGW build of FEX needs only `fmt`, `xxhash`, `range-v3`, `unordered_dense`, `rpmalloc` and `cpp-optparse`, so CI fetches those alone (section 7).

## 6. Build tools of the Mac

C-0009 section 2 pins every tool that the fork builds need, in `eng/toolchain.json`. `eng/mac/bootstrap.sh --forks` installs them on a Mac and in the `ios` job, and `tools/CheckToolchain.cs -- --fork-tools` compares each with its pin, CMake with `--cmake` ([repository-checks.md](repository-checks.md) section 5).

| Tool | Version | Needed by | From | Why this version |
|---|---|---|---|---|
| llvm-mingw | 20261104, with LLVM 23.1.5 and mingw-w64 at `ee9bd876` | Every Windows-side build: Wine's PE side, FEX, DXMT's PE side, and later the Dock host and `tests/win32` | Its GitHub release, `ucrt-macos-universal` | The newest release. Upstream llvm-mingw carries ARM64EC (section 2). |
| CMake | 4.4.5 | FEX, and the native host | Kitware's GitHub release | The pin that M1.2 set for the native host, and the newest release; 4.5.0 is still a release candidate. |
| Ninja | 1.13.2 | FEX and Meson | Its GitHub release | The newest release. |
| Meson | 1.12.1 | DXMT, which needs 1.3 or later | Its GitHub release, run with the pinned Python | The newest release. It needs Python 3.10 or later. |
| Python | 3.14.10 | FEX's build scripts, which need 3.9 or later, and Meson | python-build-standalone's release 20261026 | A build that unpacks into a folder of its own and needs no installer. 3.14 has had ten bug-fix releases; the newest series, 3.15, is weeks old. |
| GNU Bison | 3.8.2 | Wine, which needs 3.0 or later | GNU's source release, built by the bootstrap | The newest release. macOS ships Bison 2.3, which Wine refuses. |
| flex | 2.6.4 | Wine, which needs 2.5.33 or later | Xcode | The flex that Xcode carries, and the newest flex release. |

- **Where they go.** The bootstrap installs each tool into `$PANEPORT_TOOLS_DIR/<tool>-<version>`, by default below `~/Library/Caches/paneport-tools`, outside the repository, as it installs CMake and LLVM ([dev-environment.md](dev-environment.md) section 4). It checks each download's SHA-256 before it unpacks it, builds Bison from its source in the same folder, and keeps a finished install, so a second run downloads nothing. Meson runs from its source folder, through a small script beside it that starts it with the pinned Python.
- **The PATH.** Under GitHub Actions the bootstrap puts CMake, Ninja, Meson, Python and Bison first on the PATH of the job's later steps and sets `LLVM_MINGW` to llvm-mingw's folder; on a local Mac it prints the two `export` lines that do the same in a shell. llvm-mingw's folder stays off the PATH, because it also holds a `clang`, `clang-format` and `clang-tidy` for the Mac that would hide Xcode's and the pinned ones. A build that cross-compiles puts `$LLVM_MINGW/bin` first on its own PATH, as the FEX wiki asks.
- **The llvm-mingw check.** clang reports only LLVM's version, so the check reads the release from the folder that clang names on its `InstalledDir` line, `llvm-mingw-<release>` as the bootstrap names it. The bootstrap's own check puts `$LLVM_MINGW/bin` last on the PATH, so that the cross compiler is found and hides nothing.
- **Registration.** CMake, Meson and Ninja, and llvm-mingw as a Tool, already have rows in C-0003 section 6. Python, GNU Bison and flex have theirs since C-0003 1.4, which also records llvm-mingw's license, registers GNU Autoconf for M4.2, and moves the mingw-w64 runtime out of Under review (section 9).
- **What they leave in shipped code.** Bison copies its parser skeleton into the parsers it generates, under the Bison exception, which lets the parsers ship under Wine's license. flex places the scanners it generates under no license at all. Both kinds end up inside Wine's modules.

## 7. First fork targets

The `ios` job builds the forks (C-0011 section 4). In this stage it runs their first targets, which need the pinned tools and nothing else, and logs every version it uses:

1. Fetch the three forks at depth 1, and of their own submodules only those that these targets need: FEX's `fmt`, `xxhash`, `range-v3`, `unordered_dense`, `rpmalloc` and `cpp-optparse`, and DXMT's `include/native/directx`.
2. Wine's `configure` for the macOS host with the PE architectures that the FEX wiki gives, `--enable-archs=arm64ec,aarch64,i386`, with llvm-mingw as the PE compiler, then Wine's build tools (`make __tooldeps__`).
3. From the same Wine build, the i386 import libraries and `winecrt0` that DXMT's build looks for.
4. FEX's CMake configuration for `arm64ec-w64-mingw32`, with FEX's MinGW toolchain file.
5. DXMT's Meson setup for its 32-bit PE side, against that Wine build. The ARM64EC setup also compiles DXMT's shader converter for the Mac, which needs LLVM 15 with its headers and static libraries; M5.2 pins that LLVM and registers the libraries it links (plan section 8), and then sets up the ARM64EC side.

Each step passes when it exits with 0. None of them builds a module that ships; building the forks for real starts with M4.2.

On 2026-11-10 every step passed in the `ios` job ([run](https://github.com/recelilious/Paneport/actions/runs/42792375947)): fetching the forks took 36 s, Wine's `configure` and tools 93 s, its i386 libraries 4 s, FEX's configuration 20 s and DXMT's setup 15 s, and `git submodule status` showed the three forks at their recorded commits. A fresh `git clone --recurse-submodules` on the workstation did too (section 5.2).

## 8. Caching fork builds in CI

The jobs that do not build the forks never fetch them: `actions/checkout` fetches no submodules unless asked, so `dotnet` and `checks` stay as fast as before. The `ios` job, and the `runtime` job from M4.2 on, would otherwise download about 180 MB of tools and rebuild every fork on every run. The plan:

| Cache | Key | Holds | Used by |
|---|---|---|---|
| Build tools of the Mac | A hash of `eng/toolchain.json` and `eng/mac/bootstrap.sh` | `$PANEPORT_TOOLS_DIR`, which the bootstrap then finds installed | `ios` now, `runtime` from M4.2 |
| One fork's build | The fork's submodule commit, a hash of `eng/toolchain.json`, and a hash of that fork's build script under `eng/mac/` | The fork's build output | `runtime` from M4.2, then `ios` when it packages the runtime (M4.4) |

- A key changes whenever anything that shapes the output changes: a new fork commit, a new pin, or a new build script. A run whose key is new builds from scratch and saves the result; nothing is restored by a partial match, so a stale build is never used.
- Caching needs `actions/cache`, a GitHub Action that C-0003 does not register yet. It is registered, pinned and added with the first stage that caches: M4.2, which owns the `runtime` job and its build caching (S4.2.6). Until then the `ios` job downloads the tools on every run.
- GitHub keeps 10 GB of caches per repository and evicts the least recently used. A fork build that grows beyond that becomes an open item for M4.2.

## 9. The mingw-w64 runtime review

llvm-mingw links the mingw-w64 runtime into the Windows-side binaries that it builds, and C-0003 section 6 kept that runtime Under review until its notices were collected and it was confirmed that no code of unclear license is linked, before the first Windows-side build. The review, done on 2026-11-10 for llvm-mingw 20261104, whose runtime is mingw-w64 at commit `ee9bd876`:

- **Method.** In a throwaway CI run on the pinned image with the pinned llvm-mingw, one scratch link per PE target (i686, x86_64, aarch64 and arm64ec) of each of two kinds. A C program linked by default, as `tests/win32` and the Dock host will be, that formats floating point, starts a thread, installs a vectored exception handler and writes a file. A C++ DLL linked with `-static`, as FEX's and DXMT's MinGW builds link, that uses strings, vectors, exceptions, a mutex and `std::to_string` of a floating-point value. Each link wrote a linker map (`-Wl,-Map=`), and every object in a map that came from the runtime was traced to its source file at that commit and the file to its notice. The maps and the binaries were never committed, and nothing from them ships.
- **Wine's modules** link none of the runtime, because winegcc links them with `-nodefaultlibs -nostartfiles` (section 10).
- **Result.** The eight links pulled in 63 distinct runtime objects, 27 to 30 for a C program and 31 to 43 for a C++ DLL:

| Source files at `ee9bd876` | Their notice | What a release must carry |
|---|---|---|
| 42 files: the startup code in `crt/`, the UCRT glue in `stdio/ucrt_*.c` and `misc/`, Control Flow Guard support in `cfguard/`, and the CHPE metadata and ARM64EC call checker (`crt/chpe.S`, `crt/arm64ec_icall.S`) | Public domain: "This file has no copyright assigned and is placed in the Public Domain" | Nothing |
| `crt/cinitexe.c` and `misc/dllmain.c` | None of their own, so the license of the mingw-w64 project applies, the Zope Public License 2.1 (its `COPYING`) | The "Overall copyright notice" of `COPYING.MinGW-w64-runtime.txt` |
| Keith Marshall's printf family: `stdio/mingw_fprintf.c`, `mingw_snprintf.c`, `mingw_vfprintf.c`, `mingw_vsnprintf.c` and `mingw_pformat.c` | "You may redistribute and/or modify it as you see fit, without restriction of copyright" | Nothing beyond gdtoa's notice, whose code `mingw_pformat.c` calls |
| 14 files of David M. Gay's gdtoa in `gdtoa/`: string-to-number conversion and the formatting core | The permissive notice of Lucent Technologies and David M. Gay | The "gdtoa" notice of `COPYING.MinGW-w64-runtime.txt` |

- **When gdtoa comes in.** Every C++ DLL linked it, through the C++ library's conversions of floating point, and so does any code that formats or parses floating point through mingw-w64's own printf and strtod family. The C program, whose printf the UCRT supplied, did not.
- **Not linked.** No object came from the Cephes math code that `COPYING.MinGW-w64-runtime.txt` lists under "FIXME" without a clear license (`math/cbrt*.c`, `coshl.c`, `hypotl.c`, `lgamma*.c`, `sinhl.c`, `tanhl.c`, `tgamma*.c`), nor from the files under Sun's SunPro notice (`math/s_erf.c`, `sf_erf.c`, the remquo, rint and trunc functions for Arm), musl's MIT notice, the getopt notices or the LGPL. They hold `cbrt`, `lgamma`, `tgamma`, `erf` and the like, string and time helpers, and `getopt`, which the UCRT supplies or which these links did not call.
- **Decisions.** ZPL-2.1, gdtoa's notice, Sun's SunPro notice and public-domain code are permissive terms that only ask for a notice, or nothing; none of them was on the allowed list of C-0003 section 5, so C-0003 1.4 adds all four. The Cephes code has no clear license and is never linked into a binary that ships.
- **Conclusion.** The linked runtime is in the public domain or under permissive notices, ZPL-2.1 and gdtoa's notice are carried in the release notices (C-0009 section 6), and nothing of unclear license is linked. C-0003 1.4 moves the entry to Allowed on that basis.
- **Keeping it true.** The first real build of each kind, of FEX and DXMT in M4.2 and M5.2 and of the Dock host, checks its own linker map against this table. An object from another file of the runtime adds that file's notice; one from the Cephes files is not shipped, and the stage asks recelilious. A move of the llvm-mingw pin repeats the review.

## 10. Third-party code inside the forks

The register lists each fork as one component, Wine, FEX-Emu or DXMT. Their trees also hold code of other projects that is compiled into their binaries, and every component in the app needs its notices in the release (C-0012 section 5, C-0009 section 6). Found on 2026-11-10, for the stages that first build and ship it:

- **Wine** bundles libraries in `libs/`, among them compiler-rt, libc++, libc++abi and libunwind (LLVM), capstone, FAudio, FFmpeg, FluidSynth, gsm, ICU, libjpeg, jxrlib, Little CMS, OpenLDAP, mpg123, musl, libpng, SQLite, SymCrypt, libtiff, vkd3d, libxml2, libxslt and zlib, and lists their notices in its `NOTICES.md`. Wine links its PE modules with `-nodefaultlibs -nostartfiles` (`tools/winegcc/winegcc.c`), against its own `winecrt0`, import libraries and the compiler-rt in `libs/`, so they link no mingw-w64 runtime code.
- **FEX** keeps SoftFloat 3e (BSD-3-Clause) and Cephes in its tree. Its Cephes copy states that the author allowed the code to be relicensed under BSD. Its 13 fetched submodules (section 5.3) have no register rows yet, and each one that a module which ships compiles needs a row before that build.
- **DXMT** is LGPL-2.1-or-later from `main` on, but its notices still include the MIT notice in `LICENSE.OLD`, for the releases up to 0.80 that its code grew from, and those of the code it embeds: DXVK's utilities in `src/util` under the zlib license, a public-domain SHA-1, and a CC0 header in `include/tl`. Its `libs/DXBCParser` holds a DXBC parser and the header `d3d12tokenizedprogramformat.hpp` that Microsoft published under MIT. That header is code that an upstream embeds under an allowed license, not a vendor SDK header that Paneport uses, and Paneport's own code still writes its own interface definitions (C-0003 section 5, since version 1.4). DXMT also uses the headers of section 5.3.

M4.2, the first stage that compiles these into binaries that ship, lists what its builds link and proposes the register rows they need; the release dry run of M9.1 checks that nothing is missing.

## 11. Open items

- **The ARM64EC setup of DXMT** waits for LLVM 15 in M5.2 (section 7), and no runtime test of DXMT's ARM64EC side exists anywhere yet (section 2).
- **Autoconf 2.73** is registered but not pinned; M4.2 pins it when the fork first changes `configure.ac`.
- **Bundled third-party code** is listed in section 10 for M4.2 and M9.1, with FEX's 13 submodules first among it.
- **Marking changed files.** LGPL-2.1 section 2(b) asks that a modified file carry a prominent notice that it was changed, with the date. C-0010 section 4 covers only the headers of new files in a fork, so no contract says yet how Paneport marks a changed Wine or DXMT file; C-0003 section 4 records the gap since version 1.4. M4.2 changes Wine first and proposes the rule before it does.
- **FEX-darwin has moved.** On 2026-11-08 it rewrote its history into a single commit of a patch queue, and it now carries one patch under MPL-2.0 (`Darwin/patches/arknights-macos-runtime`) beside its MIT ones. C-0003 1.4 corrects its register row, which said MIT; the description in [x86_to_arm_translation.md](../research/x86_to_arm_translation.md), which shows its earlier layout on FEX-2609.1, are out of date. That patch needs a Conditional row of its own, MPL-2.0, before any stage takes it.
- **FEX's version string.** A checkout at depth 1 holds no tag, so FEX's CMake reports an empty version; M4.2's build script sets one.

## 12. Sources

Public material consulted (C-0001 section 6):

- Wine's tags and the Wine 11.21 tree: <https://gitlab.winehq.org/wine/wine> and its mirror <https://github.com/wine-mirror/wine>
- Merge request 11638, native arm64 macOS: <https://gitlab.winehq.org/wine/wine/-/merge_requests/11638>
- Wine's Clean Room Guidelines: <https://gitlab.winehq.org/wine/wine/-/wikis/Clean-Room-Guidelines>
- Wine on macOS: <https://gitlab.winehq.org/wine/wine/-/wikis/MacOS-Building>
- Wine's notices of the code it bundles: <https://github.com/wine-mirror/wine/blob/wine-11.20/NOTICES.md>
- The ARM64EC toolchain, by Wine's developers: <https://gitlab.winehq.org/wine/wine/-/wikis/ARM64EC-Toolchain>
- FEX's releases, among them FEX-2610 and FEX-2611: <https://github.com/FEX-Emu/FEX/releases>
- The two fixes to `CheckCall`: <https://github.com/FEX-Emu/FEX/commit/4dbb046d62c57639fd93d433bb64896df7332ff0> and <https://github.com/FEX-Emu/FEX/commit/3a2b1c0b438fed5062bebff0c5f7c8e21879fe48>
- `preserve_all` on MinGW in FEX-2611: <https://github.com/FEX-Emu/FEX/commit/c76fccf256a185a2a20008de2578fa5c8dab217f> and <https://github.com/FEX-Emu/FEX/commit/ef006bdb10b0c58ccda7d72011c287902b2db5d0>
- Wine 11.21's removal of old macOS code: <https://github.com/wine-mirror/wine/commit/b98a0a7bef54> and <https://github.com/wine-mirror/wine/commit/efa29db4d4b6>
- FEX's ARM64EC build instructions: <https://wiki.fex-emu.com/index.php/Development:ARM64EC>
- FEX's contribution policy: <https://github.com/FEX-Emu/FEX/blob/main/CONTRIBUTING.md>
- The Wine branch that the FEX wiki recommends: <https://github.com/bylaws/wine/tree/upstream-arm64ec>
- FEX-darwin and its provenance record: <https://github.com/dbc-hbin/FEX-darwin>
- DXMT, its releases and its build documentation: <https://github.com/3Shain/dxmt>, <https://github.com/3Shain/dxmt/releases> and <https://github.com/3Shain/dxmt/blob/main/docs/DEVELOPMENT.md>
- DXMT's AI policy: <https://github.com/3Shain/dxmt/blob/main/AGENTS.md> and <https://github.com/3Shain/dxmt/blob/main/CONTRIBUTING.md>
- DXMT's earlier MIT notice: <https://github.com/3Shain/dxmt/blob/main/LICENSE.OLD>
- The DirectX headers that DXMT uses: <https://github.com/misyltoad/mingw-directx-headers>
- LGPL-2.1, section 2: <https://spdx.org/licenses/LGPL-2.1-or-later.html>
- Creating references with GitHub's API, and the `workflow` scope: <https://docs.github.com/en/rest/git/refs#create-a-reference>
- llvm-mingw and its releases: <https://github.com/mstorsjo/llvm-mingw>
- The llvm-mingw fork that the FEX wiki names: <https://github.com/bylaws/llvm-mingw>
- mingw-w64's runtime notices and license, at the commit llvm-mingw 20261104 uses: <https://raw.githubusercontent.com/mingw-w64/mingw-w64/ee9bd87644156c41072a11ad957d1e11831a72df/COPYING.MinGW-w64-runtime/COPYING.MinGW-w64-runtime.txt> and <https://github.com/mingw-w64/mingw-w64/blob/ee9bd87644156c41072a11ad957d1e11831a72df/COPYING>
- The mingw-w64 commit that llvm-mingw 20261104 builds: <https://github.com/mstorsjo/llvm-mingw/blob/20261104/build-mingw-w64.sh>
- lld's MinGW options, among them `-Map`: <https://github.com/llvm/llvm-project/blob/llvmorg-23.1.5/lld/MinGW/Options.td>
- Hangover, which pairs Wine with FEX on Linux: <https://github.com/AndreRH/hangover/releases>
- CMake 4.4.5: <https://github.com/Kitware/CMake/releases/tag/v4.4.5>
- Ninja 1.13.2: <https://github.com/ninja-build/ninja/releases/tag/v1.13.2>
- Meson 1.12.1: <https://github.com/mesonbuild/meson/releases/tag/1.12.1> and <https://pypi.org/project/meson/1.12.1/>
- python-build-standalone 20261026: <https://github.com/astral-sh/python-build-standalone/releases/tag/20261026>
- GNU Bison 3.8.2, its announcement with the SHA-256 of the release: <https://lists.gnu.org/archive/html/bison-announce/2021-09/msg00002.html>
- flex and its license: <https://github.com/westes/flex>
- Git submodules, `shallow` and `update`: <https://git-scm.com/docs/gitmodules>
- Forking with the GitHub CLI: <https://cli.github.com/manual/gh_repo_fork>
- Caching in GitHub Actions, and its limits: <https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching>
- The `xcode-27` image and the tools it carries: <https://github.com/actions/runner-images/blob/main/images/macos/xcode-27-arm64-Readme.md>
- Research notes [wine_on_ios.md](../research/wine_on_ios.md) KQ2, [x86_to_arm_translation.md](../research/x86_to_arm_translation.md) KQ1 and KQ5, and [graphics_directx_to_metal.md](../research/graphics_directx_to_metal.md) KQ3
