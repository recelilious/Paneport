# Runtime on iOS

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-27T12:40Z |

Back to the [design index](README.md).

This document describes how stage M4.4 brings the single-process runtime of M4.3 to arm64 iOS (see the [L4 brief](stages/L4-first-windows-program.md)). It covers the cross-builds of Wine's Unix side, FEX and the host, their packaging as frameworks and the loader's search paths, the hand-off of M3.1's JIT pool to FEX, the PE code pages in the pool, the constraints on address space, registers and TLS, the template prefix and its installer, the capture of guest console output, and the bundle check with its size report. [single-process-wine.md](single-process-wine.md) describes the runtime itself, [runtime-macos-harness.md](runtime-macos-harness.md) the macOS builds and the harness, [jit.md](jit.md) the pool, and [ios-app-head.md](ios-app-head.md) the app head and the device test flavor. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

Nothing here has run on a device yet. Everything that only the iPhone can show is **not yet verified on device**, and L4's acceptance verifies it (C-0012 section 1 item 9).

**Status.** The stage is in progress. The sections below fill in as the small stages land. Until then they state what the stage must settle, and they make no decision.

## 1. Scope

In scope are the small stages S4.4.1 to S4.4.8 of the brief. Display, input and audio (M5.1, M6.1 and M6.2), DXMT (M5.2), the 32-bit pieces (M7.3) and the in-app console (S4.0.1) are out of scope.

## 2. The iOS cross-builds (S4.4.1)

To settle: how M4.2's build scripts in `eng/mac/` target `arm64-apple-ios` for Wine's Unix side, FEX and the host; which Wine modules need frameworks that iOS lacks and are left out; and whether the PE modules built for macOS can be reused unchanged.

## 3. Frameworks and the loader's search paths (S4.4.2)

To settle: one framework per Unix module or one merged framework (C-0003 section 3 and C-0009 section 4); how Wine's loader finds them in `Frameworks/` and the PE modules in `wine/aarch64-windows/` and `wine/arm64ec-windows/`; and the list of modules left out.

## 4. The pool hand-off to FEX (S4.4.3)

To settle: the bridge member that carries M3.1's regions (C-0005 section 3), how FEX's code emitter writes through the writable alias and runs from the execute view, and how FEX refuses any other executable allocation (C-0006 section 3, steps 4 and 7). This replaces the Wine fork's `map_jit_view` and `__wine_jit_write_protect` on iOS.

## 5. PE code pages in the pool (S4.4.4)

To settle: how the loader places the executable sections of PE images in the pool through the alias, how a test build counts executable pages outside the pool, and the pool's share that PE images take. Any change to C-0005 section 3 or C-0006 section 3 is proposed on a contract branch first.

## 6. Address space, x18 and TLS (S4.4.5)

To settle: whether iOS zeroes x18 on thread switches, whether PE modules use implicit TLS, and how the reservations of M4.3's address-space plan fit the smaller address map of installs without extended virtual addressing (D2).

## 7. The template prefix and its installer (S4.4.6)

The bundle location is `wine/prefix-template/` (C-0009 section 4, since 1.4). This section describes the installer. **How CI generates the template on the macOS harness is described by a later work item of this stage and is not part of this section yet.**

**The marker.** The template's root holds `paneport-template.json`: `{ "schema": 1, "version": N, "wineRevision": "<fork commit>", "replaceOnUpdate": ["drive_c/windows/"] }`. `version` is an integer that grows with each change of the template. `replaceOnUpdate` lists folders as relative paths with forward slashes and a trailing slash; the reader refuses an entry that is absolute, holds a `.` or `..` segment, a backslash or a colon, or lacks the slash, and it refuses any marker with a wrong schema, a version below 1 or a missing field. The installed prefix keeps a byte copy of the marker of the template it came from, at its own root.

**Where and what.** `PrefixInstaller` and its interface `IPrefixInstaller` are in Paneport.Core (`Prefixes/`). The code fixed no prefix name before, so the shared prefix of C-0004 section 4 is named `shared`, at `Library/Application Support/Paneport/prefixes/shared`; `PrefixNames.Shared` holds the name, and a title with its own prefix (C-0008) would pass another name. The installer reaches the disk through `IPrefixFiles`, whose System.IO implementation `PhysicalPrefixFiles` works the same on iOS and on the workstation, so Paneport.iOS adds no file-system code. The only platform part is the backup flag, which the installer takes as the existing `IBackupMarks` (`BackupMarks` over `NSURLIsExcludedFromBackupKey`), and it sets on the `prefixes` folder after a run changed files in it, as C-0004 section 4 asks. A test wraps `IPrefixFiles` to make a copy fail after a number of files.

**A run, in order.**

1. No `paneport-template.json` in the bundle folder: result `NoTemplate`, nothing else happens. Today's builds take this branch. A marker that does not parse: `Failed`, nothing is copied.
2. No prefix folder: **first install.** A staging folder `prefixes/.staging-shared` that is still there comes from an interrupted copy, whatever it holds, and is deleted. The installer copies the template tree into a new staging folder (folders, files and symbolic links, never following a link), writes the marker last, and renames the staging folder to `shared` in one step. A prefix under its name is therefore always whole.
3. A prefix folder with a valid marker of the same version: `AlreadyCurrent`. Nothing is copied.
4. A valid marker of a **lower** version than the bundle's: **update.** Only the paths under the new marker's `replaceOnUpdate` folders change. Each file is copied to `<name>.paneport-tmp` and renamed over the old one, which is atomic where the destination is a file. Files and folders under those folders that the template no longer has are deleted (a folder only when it is empty by then). The installed marker is rewritten last, the same way. Result `Updated`.
5. A marker of a **higher** version than the bundle's: `TemplateOlder`. The prefix stays as it is and a warning is logged.
6. A prefix folder without a marker, or with one that does not parse: `Unmanaged`. The installer did not make it, so it is left as it is, with a warning. Whether the session start uses such a prefix is its own decision; `PrefixInstallResult.PrefixExists` is true for it.

**Why the update keeps the registry.** `system.reg` and `user.reg` hold what the user's installers and the games wrote, such as the keys of installed runtimes, and `drive_c/users/` and the program folders hold the save files and installed programs. A new template replaces only what Wine's own build owns, normally `drive_c/windows/`, where the fork's fake DLLs and system files live. A change of the template's registry content that an old prefix needs is not applied by this rule; such a change has to ship as a step of the session's boot (single-process-wine.md section 9) or as a new marker rule, and is decided when the first such change exists. The same goes for `.update-timestamp`: the installer never touches it, and the template carries the `disable` value of M4.3's trick.

**Interruption.** The marker is the last thing written in both paths, and every write that replaces a file goes through a temporary file, so any stop leaves either no prefix under its name (first install: a staging folder, which the next run deletes) or a prefix with its old marker (update: the next run repeats the replacement, and the leftover `.paneport-tmp` files fall under the removal of what the template does not have). Cancelling the run stops it between two files and has the same effect. The copy flushes each file to storage before the rename that makes it visible; .NET has no call that flushes a directory entry, so the last rename itself relies on the file system's journal.

**Threading.** `InstallAsync` waits on a lock and then runs the work on the thread pool, so a call from the UI thread returns at once, and runs are serialized: a second call made while the first runs waits and then finds the prefix current. The iOS head starts it at launch after the host started ([ios-app-head.md](ios-app-head.md) section 3, step 8) and does not wait for it; `AppComposition.PrefixInstall` keeps the task, which never throws except on cancellation, for the session start to await before it uses the prefix path. The desktop head does not call it: it has no bundle template and no host that runs a session, and its fake host needs no prefix. The device test flavor shares the iOS head's composition, so it runs the installer too. The log names the prefix by its path relative to the app container, and a failure by the exception's type; C-0012 section 4 keeps local paths out of evidence.

**Not yet verified on device.** The copy's duration and its per-file flushes on the phone's storage, the symbolic links of a real template inside the signed bundle (code signing may refuse links in the bundle; if it does, the CI step that generates the template must leave them out and the session start already makes `dosdevices/c:` itself), the backup flag on the `prefixes` folder after the install, and the first update from a real older template. The installer has run only in tests, over temporary folders on Windows, and Paneport.iOS, whose small part is the wiring in `CompositionRoot`, cannot be compiled on this workstation.

## 8. Guest console capture (S4.4.7)

To settle: the bounded buffering, the line length limit and the count of dropped lines on the way from M4.3's capture to .NET.

## 9. The bundle check and the size report (S4.4.8)

To settle: the rules of `eng/bundle-layout.json` and how `tools/CheckBundle.cs` checks a built app against them, with its self-test.

## 10. Confirmed and to be confirmed

Nothing is confirmed yet.

## 11. Sources

The sources are those of the documents this one builds on, until the sections above add their own.
