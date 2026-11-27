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

The bundle location is `wine/prefix-template/` (C-0009 section 4, since 1.4). To settle: how CI generates the template on the harness, its version marker and what a newer template replaces, and the installer in Paneport.Core with its platform parts in Paneport.iOS.

## 8. Guest console capture (S4.4.7)

To settle: the bounded buffering, the line length limit and the count of dropped lines on the way from M4.3's capture to .NET.

## 9. The bundle check and the size report (S4.4.8)

To settle: the rules of `eng/bundle-layout.json` and how `tools/CheckBundle.cs` checks a built app against them, with its self-test.

## 10. Confirmed and to be confirmed

Nothing is confirmed yet.

## 11. Sources

The sources are those of the documents this one builds on, until the sections above add their own.
