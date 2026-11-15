# Runtime on the macOS Harness

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-15T10:30Z |

Back to the [design index](README.md).

This document describes how stage M4.2 builds the Wine fork as ARM64EC Wine for arm64 macOS with FEX as its x86-64 emulator, the Win32 test programs it runs, and the harness that checks their output (plan section 7.1, the [L4 brief](stages/L4-first-windows-program.md)). It builds on [forks.md](forks.md), which describes the forks, their branches and the build tools of the Mac. Where this document and a contract differ, the [contracts](../contracts/README.md) win. The parts that wait for the first runs in CI, for recelilious's decisions or for a contract change say so.

## 1. Scope

- **In this stage:** Wine's Unix side for arm64 macOS and its PE side for `arm64ec`, `aarch64` and `i386`; FEX's ARM64EC module; the Win32 test programs of `tests/win32/`, built for x86-64; the harness of `tests/runtime/`, its cases and expected outputs; the runner `eng/mac/run-harness.sh`; and the `runtime` CI job once C-0011 has it.
- **Multi-process Wine.** Wine runs here as upstream runs it: the wineserver and every Windows process are processes of their own. This is the baseline that M4.3 turns into one process, with the same cases and expected outputs, which must pass unchanged.
- **Later stages:** single-process mode and the bridge (M4.3), iOS builds and the JIT pool (M4.4), graphics (M5.1, M5.2), the WoW64 module and i686 programs (M7.3), and more than one Windows process (M8.1).

## 2. Building Wine

`eng/mac/build-wine.sh` configures the Wine fork out of tree, in `build/wine/build`, and installs it into `build/wine/install`. Each run starts from an empty build tree.

- **Architectures.** `--enable-archs=arm64ec,aarch64,i386`, as the FEX wiki gives them ([wine_on_ios.md](../research/wine_on_ios.md) KQ2), with `--with-mingw=clang` and the pinned llvm-mingw first on the PATH. Xcode's clang builds the Unix side. With `arm64ec` and `aarch64` together, Wine builds its 64-bit modules as hybrid ARM64X files, which hold ARM64 and ARM64EC code; `i386` is for the 32-bit guests of M7.3.
- **Only what macOS provides.** The optional libraries that would come from Homebrew or a similar source are left out with `--without-*`: ALSA, CAPI, CUPS, D-Bus, FFmpeg for the Unix side, Fontconfig, FreeType, gettext, gphoto, GnuTLS, GSSAPI and Kerberos, GStreamer, hwloc, inotify, NetAPI, OpenCL, OSS, libpcap, PC/SC, PulseAudio, SANE, SDL, udev, libusb, V4L2, VA-API, Vulkan, Wayland and X11. The build is then the same on every Mac, and nothing outside the register reaches the Unix side. Core Audio and OpenGL come with macOS and stay. Console programs need no fonts, so FreeType waits for the stage that draws text.
- **The PE side's bundled libraries.** Wine builds its own copies of the libraries in its `libs/` folder for the PE side when llvm-mingw has none: libc++, libc++abi, libunwind and compiler-rt, Capstone, FAudio, FFmpeg, FluidSynth, GSM, ICU, libjpeg, jxrlib, Little CMS, OpenLDAP, mpg123, musl, libpng, SQLite, SymCrypt, libtiff, vkd3d, libxml2, libxslt and zlib (Wine 11.20's `configure.ac`, `WINE_EXTLIB_FLAGS`). They are compiled into Wine's PE modules, which ship from M4.4 on, so each needs a register row before then (section 10).
- **Tests.** `--disable-tests`: Wine's own regression tests are not built.
- **Checks.** S4.2.1: the installed loader starts and prints its version. S4.2.2: for each PE architecture folder of the install, every module that the build tree holds for that architecture is installed, and `ntdll.dll`, `kernel32.dll`, `kernelbase.dll`, `ucrtbase.dll`, `user32.dll` and `wineboot.exe` have the machine type of their folder, read with the pinned llvm-mingw's `llvm-readobj`, which also shows the CHPE metadata of ARM64X modules.

### 2.1 What the fork must add

Upstream Wine 11.20 does not run natively on arm64 macOS. Merge request 11638, "Support Apple silicon mac for running native windows on arm softwares", is still open; its author keeps it on GitHub as one commit, `458eb1a48180` on the branch `apple-silicon-mac-woa` of `trcrsired/wine`, based on Wine's master of 2026-08-13. It:

- builds the loader as a position-independent executable, because the kernel enforces a 4 GB page zero and refuses a smaller one;
- places the shared user data at an address that macOS chooses, and gives the PE side its address through a Unix call, since `0x7ffe0000` lies in the page zero;
- applies the protections of a PE image's sections in two passes, so that a 16 KB host page is never both writable and executable;
- finds the TEB from the thread's data instead of trusting x18 and `TPIDR_EL0`;
- reserves the address ranges that Windows images need on Apple silicon.

The fork carries a copy of it rebased onto Wine 11.20, on `paneport/M4.2` ([forks.md](forks.md) sections 2 and 4.2). It changes `configure.ac`, so the fork's commit also regenerates `configure` with GNU Autoconf 2.73, the version that generated Wine 11.20's, which `eng/toolchain.json` pins with this stage.

Two hooks that FEX needs on macOS are not part of the request:

- **Executable allocations.** FEX allocates its code buffers with `VirtualAlloc2` and `PAGE_EXECUTE_READWRITE`, marked as ARM64EC code (FEX's `AllocatorHooks.h`). On Apple silicon a mapping can be writable and executable only when it is created with `MAP_JIT`, and a thread writes to it only after `pthread_jit_write_protect_np(0)` and runs it only after `pthread_jit_write_protect_np(1)` (Apple's porting guide for JIT compilers). Wine's `virtual.c` maps no memory with `MAP_JIT`, and drops the write permission when an executable and writable protection fails. The fork therefore maps executable and writable allocations with `MAP_JIT`.
- **Write scopes.** FEX's module, a PE file, cannot call `pthread_jit_write_protect_np` itself, so it switches its thread between writing and running code through a call to the Unix side, around every write to its code buffers.

The harness runs without the hardened runtime, which a locally built loader does not carry, so `MAP_JIT` needs no `com.apple.security.cs.allow-jit` entitlement there; whether that holds on the runner is confirmed with the first run (section 9).

These changes are integration code, written from Wine's, FEX's and Apple's public documentation and source only (C-0003 section 10).

## 3. Building FEX

`eng/mac/build-fex.sh` builds `libarm64ecfex.dll` with FEX's MinGW toolchain file for `arm64ec-w64-mingw32`, into `build/fex/install`:

- `-DBUILD_TESTING=False`, `-DENABLE_JEMALLOC_GLIBC_ALLOC=False`, `-DENABLE_LTO=False`, `-DENABLE_CCACHE=False`, `-DTUNE_CPU=none` and `-DTUNE_ARCH=generic`, as the `ios` job's first target configured it ([forks.md](forks.md) section 7).
- `-DOVERRIDE_VERSION=FEX-2610-paneport-<commit>` and `-DOVERRIDE_HASH=<commit>`, because a checkout at depth 1 holds no tag for FEX's own version detection ([forks.md](forks.md) section 11).
- `-Wl,--section-alignment,16384`, so that no 16 KB page holds both code and writable data.
- `-Wl,-Map=`, for the check of the mingw-w64 runtime review ([forks.md](forks.md) section 9). FEX links its module with `-static -nostdlib -nostartfiles -nodefaultlibs` and LLVM's libc++, libc++abi, libunwind and compiler-rt, with a C library of its own, so the map should show LLVM's runtime libraries and no object of mingw-w64's runtime. The script lists every library member that the link took, and names any from mingw-w64's runtime.
- The only submodules it fetches are the six that the MinGW build needs (forks.md section 5.3).

FEX's module carries the machine type of x86-64 in its header, as every ARM64EC module does, and the script checks that its load configuration holds CHPE metadata.

### 3.1 FEX on macOS

FEX upstream does not build for macOS. Its ARM64EC module needs these changes on Apple silicon:

- the write scopes of section 2.1 around its code writes, which also cover the block links and invalidations that patch existing code;
- guard regions of a whole host page, 16 KB, for its JIT and call-return stacks, because Wine's 4 KB page simulation gives every 4 KB page of a host page the same protection, which would leave a 4 KB guard page writable;
- nothing for its optional Unix library, `Source/Windows/UnixLib`, whose functions (hardware TSO, transparent huge pages, naming mappings, shared statistics) FEX's module does without when the library is not there.

FEX-darwin carries such changes, but since 2026-11-08 its Darwin support is five patches that it imported from Hadron, a Mac app that plays Windows games from a Steam library, and it moved its own patches to a FEX commit after FEX-2611 on 2026-11-11. Hadron is an existing product under C-0001 section 6, which forbids its code whatever the license. **recelilious decided on 2026-11-15** that Paneport takes no code from Hadron, nor any of FEX-darwin's patches that build on it: the FEX fork's macOS changes are written from FEX's own code and Apple's public documentation alone, and nobody writing them opens those patches, so that nothing is rewritten from them (C-0001 section 6, C-0003 section 10 rule 4). FEX-darwin's README and published results are read as evidence of what FEX needs on Darwin, as for any product (C-0001 section 6 item 3), and C-0003 1.6 proposes to move its register row to the reference-only list.

## 4. The Win32 test programs

[tests/win32/README.md](../../tests/win32/README.md) lists the six programs and what they check. The choices behind them:

- **One line per check, no paths.** Every line is `<area>: <text>`, and the result line ends the output. Lines go out through `WriteFile`, one call per line, so that nothing waits in a C library buffer when a check crashes the program. No line holds a path, which would hold the user's name. Only the CPU line and the throughput change between machines, and the expected outputs match them with patterns.
- **Structured exception handling.** clang accepts `__try`, `__except` and `__finally` for MinGW targets with `-fms-extensions`, and emits `__C_specific_handler` scope tables for them. It has no asynchronous exceptions there (`-fasync-exceptions` is ignored for MinGW), so a fault inside the `__try` body itself may be optimized away: every fault happens in a function of its own that the block calls, which clang treats as a call that may raise. The keyword warning that `-Wpedantic` gives for `__try` is turned off around those functions alone. Only `seh_check.c` is built with the flag, because with it clang-tidy's `readability-duplicate-include` reports an include that follows a header whose own include was skipped, as a small case reproduces with LLVM 22 and the `ios` job showed with LLVM 23. This answers the brief's question with compiler `__try` blocks; seh.exe also checks a vectored handler.
- **The checksum.** A 64 MiB buffer from a fixed linear congruential generator, hashed with FNV-1a over 64-bit words and summed in four 32-bit lanes with SSE2, so that both scalar and vector code are translated. Its expected value, `0x5e75a40f80343ce2`, was computed independently in C, with both ways of summing the lanes, and in JavaScript with BigInt arithmetic.
- **Suspend and context.** threads.exe suspends a spinning worker, reads its context, points it at a function with two register arguments, and resumes it, which crosses the emulator's view of an x86-64 thread's registers. On x86-64 the marker fills the whole register, so that a truncated context shows.
- **The C runtime.** `-D__USE_MINGW_ANSI_STDIO=0` keeps the UCRT's formatting, as in the C program of the runtime review, so that no gdtoa code is linked ([forks.md](forks.md) section 9).
- **Sections.** `-Wl,--section-alignment,16384`, as the brief's risks propose ([x86_to_arm_translation.md](../research/x86_to_arm_translation.md) KQ2).

## 5. The harness

[tests/runtime/README.md](../../tests/runtime/README.md) gives the formats of cases and expected outputs and the command line. `paneport_harness` runs one case:

1. It reads the case and the expected output, and refuses an unknown member, a missing one, a value of the wrong type, a name with a folder, a line without its `= ` or `~ ` prefix, and a pattern that does not compile.
2. It copies the program into the prefix at `C:\paneport-tests`, so that the program runs from drive `C:` and no drive needs to map the host's root, which M4.3 removes (S4.3.7).
3. It starts Wine's loader on `C:\paneport-tests\<program>`, with the case's arguments, `WINEPREFIX` naming the prefix, the program's folder as its working folder, `/dev/null` as its input, and pipes as its output and error. The loader runs in a process group of its own.
4. It reads both pipes until the loader exits and they close, at most for the case's timeout. Then it ends the loader's process group. After the loader exits it still reads for at most 2 s, for a helper that keeps the pipes open.
5. It compares the output line by line, and checks the exit code and Wine's error output for `wine: Unhandled` and `Unhandled exception:`, the lines with which Wine and winedbg report an unhandled exception.
6. It prints `PASS <case> (<seconds> s)` or `FAIL <case>: <why>`, and writes the output, Wine's error output and the result into the case's log folder.

The harness is our own C17 code under MIT, built with the native host's CMake project: the preset `macos-harness` builds it, and the presets `asan-ubsan` and `tsan` also build and run the CTest cases of its parts: the case reader, the comparison, the child process with its timeout, and a check that every case and expected output in the repository parses and names a program and an expected output that exist. Only multi mode exists; M4.3 adds single mode, in which the harness starts the native host and the session in its own process and traps `fork`, `exec` and `posix_spawn`.

## 6. The runner

`eng/mac/run-harness.sh` prepares everything and runs the cases:

1. It builds what is missing: Wine and FEX when their installs are not there, and always the test programs and the harness, which take seconds.
2. It makes the runtime: a clone of Wine's install, which APFS makes without copying, with `libarm64ecfex.dll` among Wine's PE modules in `lib/wine/aarch64-windows`, where Wine's loader finds it by name. Wine's install stays as its build left it, so that its cache holds Wine alone.
3. It creates the template prefix with `wineboot --init`, then names FEX's module as the x86-64 emulator in `HKLM\Software\Microsoft\Wow64\amd64`, which Wine's ARM64EC loader reads as a file name in `C:\windows\system32` (`load_arm64ec_module` in `dlls/ntdll/loader.c`), and the null display driver in `HKCU\Software\Wine\Drivers`.
4. For each case it clones the template into a fresh prefix, starts a persistent wineserver for it outside the case's process group, runs the harness, ends the wineserver, and removes the prefix.
5. It writes `summary.md`, also into the job summary under GitHub Actions, and exits non-zero when a case failed. `--planted` runs the planted cases instead, whose expected outputs it derives from the real ones, so that both cannot drift apart.

**No display and no installers.** Console programs need no display driver. The runner disables the Mac driver with `WINEDLLOVERRIDES=winemac.drv=d` and selects Wine's null driver, which also keeps `wineboot` from opening windows, and disables Wine Mono's and Wine Gecko's installers with `mscoree,mshtml=d`, which would otherwise ask to download them while `wineboot` creates the prefix (C-0003 section 8). That this suffices on the runner is confirmed with the first run (section 9).

**Helpers.** In multi mode, `wineboot` starts Wine's helpers, such as `services.exe`, once, when it creates the template. The cases themselves start no helper: no expected output depends on one (process_and_memory_model.md Q1), because M4.3 starts one process only.

## 7. The `runtime` job

C-0011 section 4 has no job for the harness yet, and C-0004 section 3 does not list `tests/runtime/` and `tests/win32/`. M4.2 proposes both changes on a contract branch (plan section 8); until recelilious accepts them, the job is not in `.github/workflows/ci.yml`. The proposed job runs on the pinned macOS image: it fetches the Wine and FEX forks with the submodules the builds need, runs `eng/mac/bootstrap.sh --forks`, restores the cached builds, builds what is missing, runs `eng/mac/run-harness.sh --mode multi` and the planted cases, and uploads the logs.

**Caching.** As [forks.md](forks.md) section 8 plans it, with keys that change whenever anything that shapes the output changes: Wine's install by the Wine fork's commit, a hash of `eng/toolchain.json` and a hash of `eng/mac/build-wine.sh`; FEX's module by the FEX fork's commit, `eng/toolchain.json` and `eng/mac/build-fex.sh`; the build tools of the Mac by `eng/toolchain.json` and `eng/mac/bootstrap.sh`. Nothing is restored by a partial key. Caching needs `actions/cache`, which C-0003 must register first; M4.2 proposes its row.

**Limits.** A job on a GitHub-hosted runner may run for 6 hours, and a repository keeps 10 GB of caches. A cold build of Wine with three PE architectures takes the longest; if it does not fit the job, the job splits into a part that fills the cache and a part that tests (the brief's fallback). The first runs measure it (section 9).

Until the job exists, there is no local Mac (D1), so the builds and the harness run in a workflow on a scratch branch that is deleted afterwards, the way M4.1 ran its runtime review, with recelilious's approval.

## 8. Checks in the other jobs

- `native`: clang-format covers `tests/runtime/` and `tests/win32/`, as it covers all of `tests/`. clang-tidy covers `tests/runtime/` through the compile database of `asan-ubsan`, and `tests/win32/` through the database that `eng/mac/build-win32-tests.sh` writes, which needs llvm-mingw's headers; the job that has llvm-mingw runs that part (section 11). CTest runs the harness's own cases with both sanitizer presets.
- `checks`: REUSE covers the expected outputs through `REUSE.toml`, and the personal-data check covers `tests/`.

## 9. Confirmed and to be confirmed

| Question of the brief | Answer |
|---|---|
| Compiler `__try` blocks or a vectored handler for seh.exe | `__try` with `-fms-extensions`, confirmed with clang 22 and with the pinned llvm-mingw, which builds the six programs with `-Werror` in the `ios` job: scope tables with `__C_specific_handler`, as long as each fault happens in a called function. The run under Wine confirms the behavior. |
| How the harness runs Wine without a display driver | `winemac.drv=d` and the null driver (section 6); to be confirmed with the first run. |
| `com.apple.security.cs.allow-jit` or no hardened runtime | The loader is built locally and not signed with the hardened runtime, so `MAP_JIT` should need no entitlement; to be confirmed with the first run. |
| The runner's limits | 6 hours per job and 10 GB of caches per repository; the build times are measured with the first runs. |

## 10. Third-party code in the builds

The PE side of Wine and FEX's module compile code of other projects, which C-0003 section 4 asks to register before a build compiles it into a binary that ships. Nothing ships from M4.2: its builds run on the harness only. Wine's PE modules and FEX's module ship from M4.4 on, so the rows are proposed with this stage and accepted before M4.4 packages them:

- Wine's bundled libraries of section 2, each with the license of its own license file in Wine's tree;
- FEX's six fetched submodules that its MinGW build needs (fmt, xxHash, range-v3, unordered_dense, rpmalloc, cpp-optparse) and the code of other projects in FEX's own tree ([forks.md](forks.md) section 10);
- `actions/cache`, for the job's caches.

Several of these come under licenses that C-0003 section 5 does not list yet, so the proposal also extends that list where the terms only ask for a notice.

## 11. Open items

- **Decided on 2026-11-15:** no code from Hadron or from FEX-darwin's patches (section 3.1); shallow checkouts of the two forks and merge request 11638's commit, as source only and deleted afterwards; and scratch workflows on `scratch/m4-2-*` branches, deleted after their runs, with registered actions pinned by commit, no cache, no secrets, read permissions and no artifacts (section 7).
- **The rule for marking changed files** under LGPL-2.1 section 2(b) is proposed before the fork's first change to a Wine file ([forks.md](forks.md) section 11).
- **clang-tidy for `tests/win32/`** needs llvm-mingw's headers, which the `native` job does not install; it runs where llvm-mingw is, until the `native` job gets it.
- **The first runs** answer the questions of section 9 and measure the build times.

## 12. Sources

Public material consulted (C-0001 section 6):

- Merge request 11638, as its author keeps it on GitHub: <https://github.com/trcrsired/wine/commit/458eb1a48180> and its discussion on Wine's mailing list: <https://list.winehq.org/hyperkitty/list/wine-gitlab@list.winehq.org/thread/KFIKY4FPELQQMU45N65AJ4IETWPSZIN7/>
- Wine 11.20's `configure.ac`, `dlls/ntdll/loader.c` and `dlls/ntdll/signal_arm64ec.c`: <https://github.com/wine-mirror/wine/tree/wine-11.20>
- FEX's ARM64EC build instructions: <https://wiki.fex-emu.com/index.php/Development:ARM64EC>
- FEX's allocator for Windows, `FEXCore/include/FEXCore/Utils/AllocatorHooks.h`, and its Windows sources at FEX-2610: <https://github.com/FEX-Emu/FEX/tree/FEX-2610>
- FEX-darwin, its patch queue and its provenance record: <https://github.com/dbc-hbin/FEX-darwin>
- Hadron, as its README describes it: <https://github.com/Broyojo/hadron>
- Porting just-in-time compilers to Apple silicon: <https://developer.apple.com/documentation/apple-silicon/porting-just-in-time-compilers-to-apple-silicon>
- Clang's support for structured exception handling: <https://clang.llvm.org/docs/MSVCCompatibility.html>
- The limits of GitHub-hosted runners: <https://docs.github.com/en/actions/reference/limits> and of dependency caching: <https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching>
- Research notes [wine_on_ios.md](../research/wine_on_ios.md) KQ1 and KQ2, [x86_to_arm_translation.md](../research/x86_to_arm_translation.md) KQ2, KQ3 and KQ5, and [process_and_memory_model.md](../research/process_and_memory_model.md) Q1
