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
- **Checks.** S4.2.1: the installed loader starts and prints its version. S4.2.2: for each PE architecture folder of the install, every module that the build tree holds for that architecture is installed, and `ntdll.dll`, `kernel32.dll`, `kernelbase.dll`, `ucrtbase.dll` and `user32.dll`, and in the 64-bit folder also `wineboot.exe`, have the machine type of their folder, read with the pinned llvm-mingw's `llvm-readobj`. It reports the 64-bit modules as `IMAGE_FILE_MACHINE_ARM64X`, the hybrid files that hold ARM64 and ARM64EC code, and Wine's programs exist in the 64-bit folder only, as the first build showed (section 9).

### 2.1 What the fork must add

Upstream Wine 11.20 does not run natively on arm64 macOS. Merge request 11638, "Support Apple silicon mac for running native windows on arm softwares", is still open; its author keeps it on GitHub as one commit, `458eb1a48180` on the branch `apple-silicon-mac-woa` of `trcrsired/wine`, based on Wine's master of 2026-08-13. It:

- builds the loader as a position-independent executable, because the kernel enforces a 4 GB page zero and refuses a smaller one;
- places the shared user data at an address that macOS chooses, and gives the PE side its address through a Unix call, since `0x7ffe0000` lies in the page zero;
- applies the protections of a PE image's sections in two passes, so that a 16 KB host page is never both writable and executable;
- finds the TEB from the thread's data instead of trusting x18 and `TPIDR_EL0`;
- reserves the address ranges that Windows images need on Apple silicon.

The fork carries a copy of it rebased onto Wine 11.20, on `paneport/M4.2` (`8fe7cfe`, and `42f009e` and `f95f7a1` for the notices of section 11; [forks.md](forks.md) sections 2 and 4.2). Wine 11.20 had moved the code under four of the request's hunks, which the rebase ports: the syscall and Unix call dispatchers, which now keep their scratch values in other registers, the reservation of the address space in `virtual_init`, and the mapping of the shared user data; the request's limit of the TEB block to the low 2 GB is no longer needed. The request changes `configure.ac` and carries its own regenerated hunks of `configure`. The fork places those hunks where Wine 11.20's `configure` holds the matching code instead of regenerating the whole file, because regenerating needs GNU M4 besides Autoconf 2.73, and C-0003 registers no M4 yet; Autoconf stays unpinned until a stage changes `configure.ac` itself (section 11). The first scratch build configured and built the fork with Xcode 27.1's clang and the pinned llvm-mingw (section 9).

macOS 27 refuses two things that the request relies on, as small programs of our own showed on the runner (section 9). It kills an arm64 executable whose segments are aligned to 4 KB at exec time, as the request's loader was with `-segalign,0x1000`, so `wineboot` died before it printed anything. And it keeps x18 across context switches only for a main executable whose build version names the macOS 12 SDK or an older one, whatever its deployment target, while it overwrites `TPIDR_EL0` with a value of its own in every program; the request sets only the deployment target, `-mmacosx-version-min=12`, and its syscall dispatcher took the TEB from `TPIDR_EL0`, or from a global variable that other threads overwrite. The fork therefore links the loader with the default 16 KB alignment and `-platform_version macos 12.0 12.0` (`0e1e42c`), and its dispatcher takes the TEB from x18 again, as Wine 11.20 does on other hosts (`dcade67`). With x18 kept, the request's other workarounds went too: it reloaded x18 from the initial context in `LdrInitializeThunk`, which left x18 zero in the first thread on the runner, and it copied the TEB into `TPIDR_EL0`, which macOS overwrites (`908c58a`). `build-wine.sh` checks both properties of the installed loader. Naming an old SDK keeps compatibility behavior of macOS that Apple may drop in a later release; the check would then fail at once.

Two hooks that FEX needs on macOS are not part of the request. The fork adds them in `53cf7fa`:

- **Executable allocations.** FEX allocates its code buffers with `VirtualAlloc2` and `PAGE_EXECUTE_READWRITE`, marked as ARM64EC code with `MEM_EXTENDED_PARAMETER_EC_CODE` (FEX's `AllocatorHooks.h`). On Apple silicon a page can be writable and executable at once only in a mapping created with `MAP_JIT`, which the kernel places itself, and each thread sees such pages either as writable or as executable, as `pthread_jit_write_protect_np` last set it for that thread (Apple's porting guide for JIT compilers). Wine maps a view at an address of its own choosing and without `PROT_EXEC`, and outside a `MAP_JIT` mapping the later `mprotect` cannot add execution to writing. `map_jit_view` in `dlls/ntdll/unix/virtual.c` therefore maps an allocation marked as ARM64EC code with `MAP_JIT` when it leaves the address, the limits and write watching to Wine, and marks its view `VPROT_JIT`. Other allocations that ask for execution and writing keep Wine's behavior and stay writable but not executable, which is all that x86-64 code needs, since FEX only reads it.
- **Write scopes.** FEX's module, a PE file, cannot call `pthread_jit_write_protect_np` itself. The fork's ntdll exports `__wine_jit_write_protect` for aarch64 and ARM64EC, which makes those views writable or executable again for the calling thread through a Unix call; on other hosts it answers `STATUS_NOT_SUPPORTED`.

Wine replaces decommitted pages with a new mapping at the same address, and a `MAP_JIT` mapping cannot be placed there, so pages of a `MAP_JIT` view that are decommitted and committed again can no longer be both writable and executable. FEX never decommits its code buffers (section 11).

The harness runs without the hardened runtime, which a locally built loader does not carry, so `MAP_JIT` needs no `com.apple.security.cs.allow-jit` entitlement there; whether that holds on the runner is confirmed with the first run (section 9).

These changes are integration code, written from Wine's and FEX's own code and Apple's public documentation only (C-0003 section 10).

## 3. Building FEX

`eng/mac/build-fex.sh` builds `libarm64ecfex.dll` with FEX's MinGW toolchain file for `arm64ec-w64-mingw32`, into `build/fex/install`:

- `-DBUILD_TESTING=False`, `-DENABLE_JEMALLOC_GLIBC_ALLOC=False`, `-DENABLE_LTO=False`, `-DENABLE_CCACHE=False`, `-DTUNE_CPU=none` and `-DTUNE_ARCH=generic`, as the `ios` job's first target configured it ([forks.md](forks.md) section 7).
- `-DOVERRIDE_VERSION=FEX-2610-paneport-<commit>` and `-DOVERRIDE_HASH=<commit>`, because a checkout at depth 1 holds no tag for FEX's own version detection ([forks.md](forks.md) section 11).
- `-Wl,--section-alignment,16384`, so that no 16 KB page holds both code and writable data.
- `-Wl,-Map=`, for the check of the mingw-w64 runtime review ([forks.md](forks.md) section 9). FEX links its module with `-static -nostdlib -nostartfiles -nodefaultlibs` and LLVM's libc++, libc++abi, libunwind and compiler-rt, with a C library of its own, so the map should show LLVM's runtime libraries and no object of mingw-w64's runtime. lld's map names an archive member without its archive, so the script lists the map's input files and names those of mingw-w64's runtime by the library prefix that mingw-w64 gives its members, such as `lib64_libmingwex_a-strtod.o`.
- The only submodules it fetches are the six that the MinGW build needs (forks.md section 5.3).

llvm-readobj reports FEX's module as `IMAGE_FILE_MACHINE_ARM64EC`, and the script checks that its load configuration points to CHPE metadata.

### 3.1 FEX on macOS

FEX upstream does not build for macOS. Its ARM64EC module needs these changes on Apple silicon, which the fork carries on `paneport/M4.2`:

- **Write scopes** (`13c0a6a`). `FEXCore::Allocator::JITWriteScope` brackets every write to FEX's code buffers: the copy of a compiled or cached block into the code buffer, the emission of the dispatcher, the patches that link and unlink blocks, and the relocations of the code cache. It calls a hook, `Allocator::JITWriteProtect`, that stays empty unless a frontend installs one, and the instruction cache is cleared after the scope ends. The Windows frontend installs the hook when ntdll exports `__wine_jit_write_protect` of section 2.1 and a first call to it succeeds. Each scope holds only the stores themselves, so scopes never nest and no generated code runs inside one.
- **Guard regions of a whole host page** (`4d07acd` and `026d646`). Wine's simulation of 4 KB pages on 16 KB hosts gives a host page the union of the protections of its four pages, so a 4 KB guard page next to writable memory stays writable. `FEX_GUARD_SIZE` is 16 KB on Windows builds, which works on 4 KB hosts too, and 4 KB elsewhere. The code buffers, the temporary buffers in which FEX compiles a block and the call-return stack use it, and so do the checks that recognize a fault in a guard region. FEX relies on the guard after the temporary buffer to notice that a block needs more room and to compile it again.
- **The x86 divide error** (`5c3cbd8`). FEX lowers `div` and `idiv` to ARM64's division, which returns zero for a zero divisor where x86 raises a divide error, so a guest never sees `EXCEPTION_INT_DIVIDE_BY_ZERO`; the `seh` case found it. On Windows builds both instructions now branch on a zero divisor to a block that raises trap 0 at the instruction, which `Source/Windows/Common/Exception.h` reports as `EXCEPTION_INT_DIVIDE_BY_ZERO`, as programs and runtimes such as .NET expect. The quotient overflow that also raises it, such as `idiv` of the smallest value by -1, is not checked yet (section 11). The change is not specific to macOS: FEX behaves the same on Windows on Arm.
- **Nothing for its optional Unix library**, `Source/Windows/UnixLib`, whose functions (hardware TSO, transparent huge pages, naming mappings, shared statistics) FEX's module does without when the library is not there.

FEX-darwin carries such changes, but since 2026-11-08 its Darwin support is five patches that it imported from Hadron, a Mac app that plays Windows games from a Steam library, and it moved its own patches to a FEX commit after FEX-2611 on 2026-11-11. Hadron is an existing product under C-0001 section 6, which forbids its code whatever the license, and so is Arknights-MacOS-Runtime, from which FEX-darwin imported another patch. **recelilious decided on 2026-11-15, and confirmed on 2026-11-17,** that Paneport takes no code from FEX-darwin, its Wine patch and scripts included, from Hadron or from Arknights-MacOS-Runtime: the FEX fork's macOS changes are written from FEX's own code and Apple's public documentation alone, and nobody writing them opens any of their patches, so that nothing is rewritten from them (C-0001 section 6, C-0003 section 10 rule 4). FEX-darwin's README and published results are read as evidence of what FEX needs on Darwin, as for any product (C-0001 section 6 item 3), and C-0003 1.6 proposes to move its register row to the reference-only list, with rows for Hadron and Arknights-MacOS-Runtime.

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

C-0011 section 4 has no job for the harness yet, and C-0004 section 3 does not list `tests/runtime/` and `tests/win32/`. M4.2 proposes both changes, with C-0003 1.6 and C-0010 1.1, on a contract branch (plan section 8); until recelilious accepts them, the job is not in `.github/workflows/ci.yml`. recelilious decided on 2026-11-17 that it runs for every pull request and every push to `main`, and that a merge waits for it (plan decision D14). The proposed job runs on the pinned macOS image: it fetches the Wine and FEX forks with the submodules the builds need, runs `eng/mac/bootstrap.sh --forks`, restores the cached builds, builds what is missing, builds the Win32 test programs and analyzes them with llvm-mingw's clang-tidy, runs `eng/mac/run-harness.sh --mode multi` and the planted cases, and uploads the logs. The build and analysis of the test programs move there from the `ios` job, which runs them until the `runtime` job exists (section 8).

**Caching.** As [forks.md](forks.md) section 8 plans it, with keys that change whenever anything that shapes the output changes, and only then: Wine's install by the Wine fork's commit, and FEX's module by the FEX fork's commit, each with the pins in `eng/toolchain.json` of the macOS image and of the tools that build the forks (Xcode, llvm-mingw, CMake, Ninja, Python, GNU Bison and flex), the fork's build script, `eng/mac/bootstrap.sh` and `eng/mac/select-xcode.sh`; the build tools of the Mac by their pins and `eng/mac/bootstrap.sh`. Nothing is restored by a partial key. Each build is saved with `actions/cache/save` as soon as it is built, so that a failing harness, a timeout or a cancelled run keeps it. The job's concurrency group does not cancel a run in progress: a newer push to the same pull request waits for it, so that a cold Wine build is not thrown away halfway through its step. Caching needs `actions/cache`, which C-0003 must register first; M4.2 proposes its row, maps it in `eng/license-map.json` and pins it at v6.1.0, commit `55cc8345863c7cc4c66a329aec7e433d2d1c52a9`, which the action's repository showed for that tag on 2026-11-17.

**Limits.** A job on a GitHub-hosted runner may run for 6 hours; this one stops after 90 minutes (D14), about twice its slowest steps so far, which add up to about 46 minutes. A repository keeps 10 GB of caches and evicts the least recently used entries and those unused for 7 days. On 2026-11-17 the caches held 11 GB, mostly NuGet caches of the `dotnet` job, saved for every branch and pull request, so from S4.2.6 on NuGet caches are saved only on `main` and restored everywhere (D14). A cold build of Wine with three PE architectures takes the longest; if it does not fit the job, the job splits into a part that fills the cache and a part that tests (the brief's fallback). The first runs measure it (section 9).

Until the job exists, there is no local Mac (D1), so the builds and the harness run in a workflow on a scratch branch that is deleted afterwards, the way M4.1 ran its runtime review, with recelilious's approval.

## 8. Checks in the other jobs

- `native`: clang-format covers `tests/runtime/` and `tests/win32/`, as it covers all of `tests/`. clang-tidy covers `tests/runtime/` through the compile database of `asan-ubsan`, and `tests/win32/` through the database that `eng/mac/build-win32-tests.sh` writes, which needs llvm-mingw's headers; a job that has llvm-mingw runs that part: the `ios` job until S4.2.6 moves it into the `runtime` job (section 7, C-0011 1.3). CTest runs the harness's own cases with both sanitizer presets.
- `checks`: REUSE covers the expected outputs through `REUSE.toml`, and the personal-data check covers `tests/`.

## 9. Confirmed and to be confirmed

| Question of the brief | Answer |
|---|---|
| Compiler `__try` blocks or a vectored handler for seh.exe | `__try` with `-fms-extensions`, confirmed with clang 22 and with the pinned llvm-mingw, which builds the six programs with `-Werror` in the `ios` job: scope tables with `__C_specific_handler`, as long as each fault happens in a called function. The run under Wine confirms the behavior. |
| How the harness runs Wine without a display driver | `winemac.drv=d` and the null driver (section 6), confirmed: `wineboot` creates the prefix in about 60 s, and the programs that try to open a window during it log `nodrv_CreateWindow` errors, on which no case depends. |
| `com.apple.security.cs.allow-jit` or no hardened runtime | No entitlement: a program that the linker signs ad hoc, without the hardened runtime, maps `MAP_JIT` memory and switches it with `pthread_jit_write_protect_np` (probe of 2026-11-16). |
| What macOS 27 lets a process execute | Probes on the runner: code in a file mapped executable, or mapped read-only and made executable with `mprotect`, as Wine maps an image, runs; anonymous memory written and then made executable runs; memory both writable and executable is refused without `MAP_JIT` (`EACCES`); `MAP_JIT` refuses `MAP_FIXED` (`EINVAL`) and allows `mprotect` inside it. |
| x18, `TPIDR_EL0` and the loader's link | x18 survives context switches only when the main executable names SDK 11 or 12, not 13 or the current one, whatever its deployment target; `TPIDR_EL0` never keeps a value written to it; an executable linked with `-segalign,0x1000` is killed at exec, one with the deployment target 12 alone runs (section 2.1). |
| The runner's limits | 6 hours per job and 10 GB of caches per repository. |
| The runner | macOS 27.0.1 on an Apple M1 (Virtual) with 3 cores, 7 GiB of memory and 36 GiB of free disk, with Xcode 27.1 (scratch run 43695160263). |
| Build times, cold | Wine: configured in 44 s, built in 1572 s with 3 jobs, installed in 32 s. FEX: its six submodules fetched, configured in 17 s, built in 65 s. The test programs and the harness: 11 s. A cold run fits a job by far, so the job needs no split. |
| The first harness run | Run 43928068884: `wineboot` made the template prefix in 58 s; `checksum`, `cpu_features`, `file_io` and `threads` passed under FEX, the checksum at about 4.4 GB/s; `console`, `console_exit_code` and `seh` failed on the divide check alone (section 3.1). |
| The harness | Run 43936699061, with Wine `908c58a` and FEX `5c3cbd8`: all seven cases pass in 10 to 13 s each, and the planted cases fail with their names (`wrong_exit_code` exited with 3, not 4; `wrong_line` differs in line 1). FEX's map lists 235 input files and no object of mingw-w64's runtime. |
| Does merge request 11638 build on Wine 11.20 | Yes: the rebased fork configures and builds for `arm64ec`, `aarch64` and `i386`, the loader prints `wine-11.20`, and every module that the build made is installed. The 64-bit modules are ARM64X files. |

## 10. Third-party code in the builds

The PE side of Wine and FEX's module compile code of other projects, which C-0003 section 4 asks to register before a build compiles it into a binary that ships. Nothing ships from M4.2: its builds run on the harness only. M4.2 surveyed that code instead, on 2026-11-15, from the license files and the headers of every file in Wine's bundled libraries and in the folders of FEX that its Windows build compiles, at the forks' bases `0bec332` and `dd288e6`. M4.4, the first stage that packages Wine's PE modules and FEX's module into the app, proposes their register rows and the additions to C-0003 section 5 before it ships them, and the release dry run of M9.1 checks that nothing is missing. This moves the rows from M4.2, to which [forks.md](forks.md) section 10 had given them as the first stage that would ship these builds. The one row that M4.2 needs itself, `actions/cache` for the job's caches, is in its C-0003 proposal.

| Component | Where | License, as SPDX | On C-0003 section 5's list |
|---|---|---|---|
| LLVM's libc++, libc++abi, libunwind and compiler-rt | Wine `libs/c++`, `libs/c++abi`, `libs/unwind`, `libs/compiler-rt` | MIT OR NCSA | Yes, as MIT |
| Capstone | Wine `libs/capstone` | BSD-3-Clause AND NCSA (28 files from LLVM) | NCSA no |
| FAudio | Wine `libs/faudio` | Zlib | Yes |
| FFmpeg 8.1.1 | Wine `libs/ffmpeg` | LGPL-2.1-or-later; `libavutil/aarch64/crc.S` GPL-2.0-or-later | The GPL file is forbidden in anything that ships |
| FluidSynth | Wine `libs/fluidsynth` | LGPL-2.1-or-later | Yes |
| GSM | Wine `libs/gsm` | TU-Berlin-2.0 | No |
| ICU | Wine `libs/icucommon`, `libs/icui18n` | Unicode-DFS-2016 AND ICU, with BSD-3-Clause for double-conversion | No |
| libjpeg | Wine `libs/jpeg` | IJG | No |
| jxrlib | Wine `libs/jxr` | BSD-2-Clause | Yes |
| Little CMS | Wine `libs/lcms2` | MIT | Yes |
| OpenLDAP | Wine `libs/ldap` | OLDAP-2.8, with the University of Michigan's notice | No |
| mpg123 | Wine `libs/mpg123` | LGPL-2.1-only | Yes |
| musl's libm | Wine `libs/musl` | MIT AND SunPro AND BSD-2-Clause | Yes |
| libpng | Wine `libs/png` | libpng-2.0 | No |
| SQLite 3.53.2 | Wine `libs/sqlite3` | blessing, a public-domain dedication | Yes, as public domain |
| SymCrypt | Wine `libs/symcrypt` | MIT | Yes |
| libtiff | Wine `libs/tiff` | libtiff AND BSD-4.3TAHOE AND MIT | No |
| vkd3d | Wine `libs/vkd3d` | LGPL-2.1-or-later AND MIT-Khronos-old AND MIT | MIT-Khronos-old no |
| libxml2 | Wine `libs/xml2` | MIT AND ISC-Veillard | ISC-Veillard no |
| libxslt | Wine `libs/xslt` | X11 | No |
| zlib | Wine `libs/zlib` | Zlib | Yes |
| fmt 12.2.1 | FEX `External/fmt` | MIT | Yes |
| xxHash | FEX `External/xxhash` | BSD-2-Clause; its `cli/` is GPL-2.0 and not built | Yes |
| range-v3 | FEX `External/range-v3` | BSL-1.0 AND (MIT OR NCSA), with the notices of HP, SGI and Stepanov and McJones | The three notices no |
| unordered_dense | FEX `External/unordered_dense` | MIT | Yes |
| rpmalloc | FEX `External/rpmalloc` | 0BSD OR Unlicense | Yes, as 0BSD |
| cpp-optparse | FEX `Source/Common/cpp-optparse` | MIT | Yes |
| tiny-json | FEX `External/tiny-json` | MIT | Yes |
| musl's and Arm's C library code | FEX `Source/Windows/Common/CRT/Musl` | MIT | Yes |
| Wine's headers | FEX `Source/Windows/include` | LGPL-2.1-or-later | Yes |
| `LoadConfig.S` from mingw-w64 | FEX `Source/Windows/Common` | Public domain | Yes |
| VIXL's utilities | FEX `CodeEmitter/CodeEmitter/VixlUtils.inl` | BSD-3-Clause | Yes |
| SplitMix64 | FEX `FEXCore/Source/Interface/Core/JIT/JIT.cpp` | CC0-1.0 | Yes |
| SoftFloat 3e | FEX `External/SoftFloat-3e` | BSD-3-Clause | Yes |
| Cephes | FEX `External/cephes` | None stated: "BSD licensed", from an e-mail of its author | No: code without a license |

What M4.4 has to settle:

- **A GPL file is compiled.** FFmpeg's `libavutil/aarch64/crc.S` states GPL-2.0-or-later, as upstream FFmpeg's does, though FFmpeg's `LICENSE.md` does not list it among its GPL parts. Wine compiles it for aarch64, and through `libavutil/arm64ec/crc.S` for ARM64EC, with `HAVE_ARM_CRC` set. Only `av_crc` uses it, through `libavutil/hash.c` and `lfg.c`, and none of the six modules that link Wine's FFmpeg (colorcnv, iyuv_32, msvproc, quartz, resampledmo and vidreszr) calls those, so it is probably in none of them; the built modules have not been checked. C-0003 section 4 forbids it in anything that ships: M4.4 shows from link maps that no module holds it, or the fork leaves it out of the build.
- **Cephes has no license text.** FEX's `External/cephes/LICENSE` rests on an e-mail in which its author allowed BSD terms without naming a variant, and its files carry copyright lines only. FEX's module compiles it, and C-0003 section 5 forbids code without a license, so recelilious decides before the module ships.
- **Licenses not on the list.** NCSA, TU-Berlin-2.0, Unicode-DFS-2016 and ICU, IJG, OLDAP-2.8, libpng-2.0, libtiff, BSD-4.3TAHOE, X11, ISC-Veillard, MIT-Khronos-old and range-v3's three notices are permissive terms that ask for a notice, a credit in the documentation or a mark on changed files, so they can be added to C-0003 section 5.
- **Wine's `NOTICES.md` is incomplete:** it has no entry for FAudio and GSM, names only libc++ for LLVM, and lacks Capstone's NCSA text, ICU's legacy and double-conversion notices, libtiff's Berkeley notice and vkd3d's Khronos and Intel notices. The release's notices come from the register (C-0009 section 6), not from that file.

## 11. Open items

- **Decided on 2026-11-17:** the `runtime` job runs for every pull request and every push to `main`, and a merge waits for it, with each fork build saved as soon as it is built, a 90-minute timeout and NuGet caches saved only on `main` (section 7, plan decision D14).
- **Decided on 2026-11-15, and confirmed on 2026-11-17:** no code from FEX-darwin, its Wine patch and scripts included, from Hadron or from Arknights-MacOS-Runtime (section 3.1). **Decided on 2026-11-15:** shallow checkouts of the two forks and merge request 11638's commit, as source only and deleted afterwards; and scratch workflows on `scratch/m4-2-*` branches, deleted after their runs, with registered actions pinned by commit, no cache, no secrets, read permissions and no artifacts (section 7).
- **The rule for marking changed files** under LGPL-2.1 section 2(b) is proposed in C-0010 1.1, and the fork's changed files already carry its notice line, `configure` and `include/config.h.in` included (`f95f7a1`), because the request's hunks were placed in them by hand ([forks.md](forks.md) section 11).
- **clang-tidy for `tests/win32/`** needs llvm-mingw's headers, which the `native` job does not install; it runs in the `ios` job, and moves into the `runtime` job with S4.2.6 (section 8).
- **`configure`.** The fork carries the request's own regenerated hunks (section 2.1). The first stage that changes `configure.ac` itself registers GNU M4, pins it and Autoconf 2.73, and regenerates the file.
- **Decommitted `MAP_JIT` pages** lose `MAP_JIT` (section 2.1). Nothing that M4.2 runs decommits them; an emulator that does needs Wine to keep the mapping and zero the pages itself.
- **The request's global variables.** Its dispatchers no longer read `current_teb` and `current_syscall_frame`, which every thread overwrites (section 2.1), but the request still writes them in several places; they can go once the harness passes.
- **The old SDK version** that the loader names keeps x18 through a compatibility behavior of macOS 27, which a later release may drop; `build-wine.sh` and the harness would show it at once.
- **Builds the fork does not serve yet.** The request defines its two TEB Unix calls in the part of `dlls/ntdll/unix/loader.c` that a build for x86-64 Linux skips, so that build would not link. Its Unix calls also have no entries for WoW64, whose i386 `ntdll.dll` asks for the shared user data's address, and FEX's WoW64 module allocates its code without `MEM_EXTENDED_PARAMETER_EC_CODE`, so it gets no `MAP_JIT` view; both are for M7.3 (L7 brief).
- **The divide error on quotient overflow**, such as `idiv` of the smallest value by -1, is not raised yet (section 3.1).
- **32-bit processes.** During `wineboot`, a 32-bit process could not reserve its low memory (`map_fixed_area out of memory for 0x10000-0x110000`), because macOS keeps the low 4 GB as the loader's page zero; WoW64 needs that range, which is for M7.3.
- **Third-party code** for M4.4 to register, among it a GPL file of FFmpeg and Cephes without a license (section 10).
- **The `runtime` job** (section 7) waits for the contract proposals C-0011 1.3, C-0004 1.1, C-0003 1.6 and C-0010 1.1; until then, the scratch runs of section 9 stand in for it.

## 12. Sources

Public material consulted (C-0001 section 6):

- Merge request 11638, as its author keeps it on GitHub: <https://github.com/trcrsired/wine/commit/458eb1a48180> and its discussion on Wine's mailing list: <https://list.winehq.org/hyperkitty/list/wine-gitlab@list.winehq.org/thread/KFIKY4FPELQQMU45N65AJ4IETWPSZIN7/>
- Wine 11.20's `configure.ac`, `dlls/ntdll/loader.c` and `dlls/ntdll/signal_arm64ec.c`: <https://github.com/wine-mirror/wine/tree/wine-11.20>
- FEX's ARM64EC build instructions: <https://wiki.fex-emu.com/index.php/Development:ARM64EC>
- FEX's allocator for Windows, `FEXCore/include/FEXCore/Utils/AllocatorHooks.h`, and its Windows sources at FEX-2610: <https://github.com/FEX-Emu/FEX/tree/FEX-2610>
- FEX-darwin, its patch queue and its provenance record: <https://github.com/dbc-hbin/FEX-darwin>
- Hadron, as its README describes it, and its license files: <https://github.com/Broyojo/hadron>, <https://github.com/Broyojo/hadron/blob/HEAD/LICENSE> and <https://github.com/Broyojo/hadron/blob/HEAD/LICENSE.hadron>
- Arknights-MacOS-Runtime, as its repository describes itself: <https://github.com/LuMiSxh/Arknights-MacOS-Runtime>
- Porting just-in-time compilers to Apple silicon: <https://developer.apple.com/documentation/apple-silicon/porting-just-in-time-compilers-to-apple-silicon>
- Clang's support for structured exception handling: <https://clang.llvm.org/docs/MSVCCompatibility.html>
- The limits of GitHub-hosted runners: <https://docs.github.com/en/actions/reference/limits> and of dependency caching: <https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching>
- Research notes [wine_on_ios.md](../research/wine_on_ios.md) KQ1 and KQ2, [x86_to_arm_translation.md](../research/x86_to_arm_translation.md) KQ2, KQ3 and KQ5, and [process_and_memory_model.md](../research/process_and_memory_model.md) Q1
