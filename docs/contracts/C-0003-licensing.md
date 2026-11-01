# C-0003 Open-Source Licensing

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.3 |
| Date | 2026-10-31 |
| Owner | recelilious |

These are engineering rules for managing licenses, not legal advice. Before a formal public release, have a professional review them if possible.

## 1. Decision

| Code | License | Where it lives |
|---|---|---|
| Paneport's own code | MIT | This repository |
| Changes to LGPL upstreams such as Wine and DXMT | Stays LGPL-2.1-or-later | That upstream's fork repository |
| Changes to MIT upstreams such as FEX-Emu and Box64 | Stays MIT | That upstream's fork repository |

Every line we write ourselves is MIT. The app package as a whole cannot be released under MIT because it contains LGPL components. Within the upstream licenses, this is as close to MIT as the project can get.

## 2. Why not MIT everywhere

Wine and DXMT are both LGPL-2.1-or-later. Code that modifies them is a derivative of the library and must stay LGPL. That includes the iOS display driver written inside the Wine source tree and the changes that make Wine run in a single process.

DXMT was MIT up to and including 0.80 and has been LGPL-2.1-or-later since. We follow current versions and treat it as LGPL.

## 3. Why MIT code and LGPL components can ship in one app

Section 6 of LGPL-2.1 allows a library to be combined and distributed with "a work that uses the Library" under three conditions:

1. The terms of the combined work allow modification for the user's own use and reverse engineering to debug such modifications. MIT meets this.
2. The complete source of the library accompanies it.
3. The user can relink the work with a modified library to produce a new executable.

How we meet them:

- **Dynamic packaging:** the Unix-side code of Wine and similar components ships inside the .app as dynamic libraries or frameworks that the user can replace.
- **Public source:** SteamKit2 (LGPL-2.1-only) is compiled statically into the executable by .NET AOT. All source code and build scripts in this repository are public, so a user can swap SteamKit2 and rebuild, which satisfies the relinking requirement.
- **User installability:** sideloading already means the user signs the app with their own Apple ID, so the user can install a rebuilt version.
- **Shipped with every release:** each release includes `THIRD_PARTY_NOTICES.md` and the full text of every license. The app has an open-source licenses screen. Each release attaches source archives of this repository and of every fork at exactly the released revisions, and its notes link to them (C-0009 section 6).

## 4. Repository layout

- `src/` in this repository holds only original code, all MIT.
- Code derived from an upstream always lives in its own fork repository named `Paneport-<upstream>`, for example `Paneport-wine`, attached under `external/` as a git submodule.
- Fork repositories keep the upstream license files and copyright notices.
- Original source files start with `SPDX-License-Identifier: MIT`. New files inside a fork use that upstream's identifier, for example `SPDX-License-Identifier: LGPL-2.1-or-later`.
- `LICENSES/` holds the texts of the licenses that files in this repository use. It is created with `MIT.txt` together with the first file that carries the SPDX lines of C-0010 section 6. Files that cannot carry a header are covered by `REUSE.toml` (C-0010 section 6). The license texts of third-party components are generated from the register in section 6 into each release and into the app bundle, not into `LICENSES/`.

## 5. Intake rules

Before adding any dependency, register it in the table in section 6.

| Category | Licenses | Rule |
|---|---|---|
| Allowed | MIT, MIT-Modern-Variant, BSD-2-Clause, BSD-3-Clause, ISC, Zlib, 0BSD, BSL-1.0, Apache-2.0, Apache-2.0 WITH LLVM-exception, CC0-1.0, FTL, OFL-1.1 | Keep the copyright notices. Apache-2.0 also requires keeping the NOTICE file. FTL requires crediting FreeType in the documentation. OFL-1.1 is only for fonts: keep the copyright notice and the license with the font, never sell the font by itself, and never give a modified font the original's Reserved Font Names. |
| Conditional | LGPL-2.1-only, LGPL-2.1-or-later, LGPL-3.0, MPL-2.0 | Only as separately identifiable components. Changes are published under the same license. Relinking must stay possible. LGPL-3.0 also requires installation information. |
| Forbidden | GPL-2.0, GPL-3.0, AGPL-3.0, SSPL, non-commercial-only licenses, code with no license | Never enters this repository, a fork repository or the app package. |
| Forbidden | Vendor SDK headers, decompiled code | For example the Steamworks SDK and Windows SDK headers. Write the needed interface definitions ourselves and record their basis in a design document. |
| Under review | Proprietary components such as Apple's Metal Shader Converter | No redistribution terms were found publicly. Read its license and record the conclusion before using it, and never put it in the repository. |
| Build and CI tool | Any license, including GPL | Only for tools that build or check the repository and are never copied into the repository, a fork or the app package. Registered in section 6 with the category Tool. |

For dual-licensed components, choose the less restrictive option. For example, FreeType is used under FTL rather than GPL-2.0.

## 6. Component register

Checked on 2026-10-28, on 2026-10-29 for the rows that version 1.2 added, and on 2026-10-31 for the rows that version 1.3 added. Licenses are taken from each repository's license file.

| Component | Purpose | License | Category |
|---|---|---|---|
| Wine | Windows compatibility layer | LGPL-2.1-or-later | Conditional |
| FEX-Emu | x86-64 translation | MIT | Allowed |
| Box64 | Fallback x86 translation | MIT | Allowed |
| FEX-darwin (dbc-hbin) | FEX integration modules for Darwin and Wine | MIT | Allowed |
| DXMT | Direct3D 10/11 to Metal | LGPL-2.1-or-later | Conditional |
| MoltenVK | Vulkan to Metal, optional | Apache-2.0 | Allowed |
| SPIRV-Cross | Shader translation, a MoltenVK dependency | Apache-2.0 | Allowed |
| DXVK | Direct3D to Vulkan, probably unused on iOS | Zlib | Allowed |
| vkd3d-proton | Direct3D 12 to Vulkan, probably unused on iOS | LGPL-2.1 | Conditional |
| SteamKit2 | Steam network protocol | LGPL-2.1-only | Conditional |
| protobuf-net | SteamKit2 dependency | Apache-2.0 | Allowed |
| Avalonia | UI shell | MIT | Allowed |
| SkiaSharp | Avalonia's rendering library | MIT. Its native-asset packages contain the Skia library (BSD-3-Clause) and list the further third-party components they contain in their `THIRD-PARTY-NOTICES.txt`. | Allowed |
| Avalonia.BuildServices | Usage statistics that Avalonia's packages collect at build time; CI turns them off with `AVALONIA_TELEMETRY_OPTOUT` | MIT | Allowed |
| MicroCom.Runtime | COM-style interop runtime, an Avalonia dependency | MIT | Allowed |
| HarfBuzzSharp | Text shaping for Avalonia, with native HarfBuzz libraries | MIT. The native HarfBuzz library is under HarfBuzz's "Old MIT" license (MIT-Modern-Variant), and the native-asset packages list further components in their `THIRD-PARTY-NOTICES.txt`. | Allowed |
| ANGLE (Avalonia.Angle.Windows.Natives) | OpenGL ES on Direct3D for Avalonia on Windows; desktop head only, not shipped | BSD-3-Clause | Allowed |
| Tmds.DBus.Protocol | D-Bus client for Avalonia on Linux; desktop head only, not shipped | MIT | Allowed |
| Inter font (Avalonia.Fonts.Inter) | Default font of Avalonia's headless platform; tests only, not shipped | OFL-1.1 | Allowed |
| .NET runtime | Managed runtime | MIT | Allowed |
| .NET for iOS (dotnet/macios) | iOS bindings | MIT | Allowed |
| SDL | Input and audio helpers | Zlib | Allowed |
| FAudio | XAudio2 implementation | Zlib | Allowed |
| FreeType | Font rendering, a Wine dependency | FTL or GPL-2.0 | Allowed, under FTL |
| GStreamer | Media decoding, an optional Wine dependency | LGPL-2.1-or-later | Conditional, no GPL plugins |
| idevice (jkcoxson) | Device protocol, optional | MIT | Allowed |
| LLVM runtime libraries (compiler-rt, libunwind, libc++, libc++abi) | Linked by llvm-mingw into Windows-side binaries that ship | Apache-2.0 WITH LLVM-exception; parts not yet relicensed remain under LLVM's legacy NCSA-or-MIT dual license, used under MIT | Allowed |
| mingw-w64 runtime | Linked by llvm-mingw into Windows-side binaries that ship | Several permissive licenses with notice requirements, listed in its `COPYING.MinGW-w64-runtime.txt` | Under review: collect the notices and confirm that no code of unclear license is linked, before the first Windows-side build |
| xunit.v3 | Test framework, not shipped | Apache-2.0 | Allowed |
| Avalonia.Headless.XUnit | Headless UI tests, not shipped | MIT | Allowed |
| Microsoft.Testing.Platform | Runs the xunit.v3 tests under `dotnet test`, with its MSBuild, report and telemetry extensions; not shipped | MIT | Allowed |
| Application Insights SDK (Microsoft.ApplicationInsights) | Sends the test platform's telemetry; not shipped; CI turns it off | MIT | Allowed |
| CommunityToolkit.Mvvm | MVVM source generators | MIT | Allowed |
| JsonSchema.Net | Schema validation, CI only | MIT source; NuGet packages under the json-everything Open Source Maintenance Fee agreement | Under review; not used until recelilious decides |
| llvm-mingw | Windows-side toolchain | Recorded when first pinned | Tool |
| CMake | Native build configuration | BSD-3-Clause | Tool |
| Meson, Ninja | Build systems the forks need | Apache-2.0 | Tool |
| Xcode | Apple's toolchain | Apple proprietary | Tool |
| clang-format, clang-tidy | Native formatting and analysis | Apache-2.0 WITH LLVM-exception | Tool |
| gitleaks (command-line tool) | Secret scanning | MIT | Tool |
| REUSE tool | License header checks | GPL-3.0-or-later | Tool |
| lychee | Link checks | MIT OR Apache-2.0 | Tool |
| actions/checkout | GitHub Action that checks out the repository in CI | MIT | Tool |
| actions/setup-dotnet | GitHub Action that installs the SDK that `global.json` pins in CI | MIT | Tool |
| actions/upload-artifact | GitHub Action that uploads what a CI job built, such as the app of the `ios` job | MIT | Tool |
| GitHub-hosted runner images (actions/runner-images) | The virtual machine images that the CI jobs run on, with the tools installed on them; `eng/toolchain.json` pins the macOS image of the `ios` job (C-0009 section 2) | MIT, for the scripts and documentation that define the images; each installed tool keeps its own license, and Xcode has its own row | Tool |

Media decoding prefers Apple's system frameworks, which avoids GStreamer's GPL plugins and codec patent issues.

## 7. Reference-only projects whose code must not be reused

These projects are either existing products, covered by section 6 of C-0001, or licensed incompatibly with this project.

| Project | License |
|---|---|
| Madeira | GPL-3.0 |
| madeira-dock | GPL-3.0-or-later with the Madeira Converter Exception |
| BCD, WiniOS, Playport, iDeck (Madeira forks) | GPL-3.0 family, per each repository |
| GameNative | GPL-3.0 |
| Winlator | LGPL-2.1 |
| DepotDownloader | GPL-2.0 |
| iSH | GPL-3.0 |
| LiveContainer | AGPL-3.0 |
| StikDebug | AGPL-3.0 |
| UTM | Apache-2.0 |
| SteamARM (LukeOkk) | MIT |
| SteamRE/open-steamworks | No license, archived |
| Apple D3DMetal (Game Porting Toolkit) | Proprietary, non-commercial use only, macOS only |
| GameHub | Closed source |

UTM and SteamARM have permissive licenses, but they are existing products, so they are reference-only as well.

## 8. Components downloaded at run time, never in the repository or package

| Component | Source |
|---|---|
| Microsoft VC++ and DirectX runtimes | Downloaded by the user's device from Microsoft, or provided by the user |
| Valve Steam client components | Downloaded by the user's device from Valve's official CDN |
| Wine Mono and Wine Gecko | Downloaded from their official locations when first needed, under their own licenses |
| Game files | Downloaded with the user's own Steam account |

## 9. Boundary with AGPL programs

StikDebug is a separate app under AGPL-3.0. Paneport cooperates with it only through the JIT request convention and the URL scheme documented in `INTEGRATION.md` of StikDebug/StikJIT (MPL-2.0), using a broker script that Paneport writes itself (C-0006). Paneport never links, embeds or copies StikDebug's code or scripts, and nobody writing Paneport's side reads their source.

If the app ever enables JIT by itself, that code will be our own, built on the MIT-licensed idevice.

## 10. Contributing upstream

Upstream projects treat AI-assisted contributions differently.

| Project | Policy |
|---|---|
| FEX-Emu | Forbids AI-generated code in contributions, stated in its AGENTS.md, CLAUDE.md and CONTRIBUTING.md |
| QEMU | Explicitly declines AI-generated code since 2025 |
| Gentoo, NetBSD | Ban or tightly restrict it |
| GNU Binutils | Requires LLM-generated content to be labeled |
| GCC | Still under discussion as of April 2026 |
| Wine | No explicit policy found as of 2026-10-28 |

Rules:

1. Before submitting anything upstream, check that project's current policy and follow it.
2. Never remove AI attribution to get around a policy.
3. Our FEX-Emu changes stay in our fork and are never proposed upstream. Wine and DXMT changes may also have to live in our forks for the long term.

## 11. Alternative

If an explicit patent grant becomes necessary, our own code can move to `MIT OR Apache-2.0` dual licensing. MIT stays the default.

## 12. Sources

- Wine license: <https://github.com/wine-mirror/wine/blob/HEAD/LICENSE>
- DXMT license: <https://github.com/3Shain/dxmt/blob/HEAD/LICENSE>
- FEX-Emu license: <https://github.com/FEX-Emu/FEX/blob/HEAD/LICENSE>
- FEX-Emu contribution policy: <https://github.com/FEX-Emu/FEX/blob/HEAD/CONTRIBUTING.md>
- SteamKit2 license: <https://github.com/SteamRE/SteamKit/blob/HEAD/LICENSE>
- protobuf-net license: <https://github.com/protobuf-net/protobuf-net/blob/HEAD/Licence.txt>
- Madeira: <https://github.com/willfaust/Madeira>
- madeira-dock: <https://github.com/125hz/madeira-dock>
- FEX-darwin: <https://github.com/dbc-hbin/FEX-darwin>
- Metal Shader Converter: <https://developer.apple.com/metal/shader-converter/>
- Unanswered question about shipping Metal Shader Converter inside an app: <https://developer.apple.com/forums/thread/829379>
- QEMU AI code policy patch: <https://patchew.org/QEMU/20250616092241.212898-1-armbru@redhat.com>
- GCC discussion: <https://gcc.gnu.org/pipermail/gcc/2026-April/247958.html>
- GNU Binutils statement: <https://gnu.googlesource.com/binutils-gdb/+/c0316a54b489d68e30de718aee69ab441799aad2%5E%21>
- Survey of open-source AI policies: <https://www.ssdnodes.com/learn/lang/pcm/ai-assisted-code-open-source-policies>
- Avalonia.BuildServices license: <https://github.com/AvaloniaUI/Avalonia.BuildServices/blob/HEAD/LICENSE>
- MicroCom license: <https://github.com/kekekeks/MicroCom/blob/HEAD/LICENSE>
- SkiaSharp and HarfBuzzSharp license: <https://github.com/mono/SkiaSharp/blob/HEAD/LICENSE.md>
- Skia license: <https://github.com/google/skia/blob/HEAD/LICENSE>
- HarfBuzz license: <https://github.com/harfbuzz/harfbuzz/blob/HEAD/COPYING>
- ANGLE license, Avalonia's build: <https://github.com/AvaloniaUI/angle/blob/HEAD/LICENSE>
- Tmds.DBus license: <https://github.com/tmds/Tmds.DBus/blob/HEAD/COPYING>
- Inter font license: <https://github.com/rsms/inter/blob/HEAD/LICENSE.txt>
- Microsoft.Testing.Platform license: <https://github.com/microsoft/testfx/blob/HEAD/LICENSE>
- Application Insights SDK license: <https://github.com/microsoft/ApplicationInsights-dotnet/blob/HEAD/LICENSE>
- actions/checkout license: <https://github.com/actions/checkout/blob/HEAD/LICENSE>
- actions/setup-dotnet license: <https://github.com/actions/setup-dotnet/blob/HEAD/LICENSE>
- actions/upload-artifact license: <https://github.com/actions/upload-artifact/blob/HEAD/LICENSE>
- GitHub-hosted runner images license: <https://github.com/actions/runner-images/blob/HEAD/LICENSE>
- MIT-Modern-Variant: <https://spdx.org/licenses/MIT-Modern-Variant.html>
- OFL-1.1: <https://spdx.org/licenses/OFL-1.1.html>

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious; translated to English; corrected the FEX-Emu entry in section 10, since FEX-Emu forbids AI-generated contributions |
| 1.1 | 2026-10-28 | Accepted by recelilious. Adds the Build and CI tool category, LGPL-2.1-only and Apache-2.0 WITH LLVM-exception to section 5. Registers the llvm-mingw runtime libraries, test libraries and CI tools in section 6, and corrects SteamKit2 to LGPL-2.1-only. Creates `LICENSES/` with the first SPDX header and generates third-party license texts into releases (section 4). Releases attach source archives (section 3). Section 9 allows the convention and URL scheme documented in StikJIT's `INTEGRATION.md`. |
| 1.2 | 2026-10-29 | Accepted by recelilious. Adds MIT-Modern-Variant, and OFL-1.1 for fonts only, to section 5. Registers in section 6 the components that stage M0.1's packages and CI workflow bring in: Avalonia.BuildServices, MicroCom.Runtime, HarfBuzzSharp, ANGLE, Tmds.DBus.Protocol, the Inter font, Microsoft.Testing.Platform, the Application Insights SDK, and the actions/checkout and actions/setup-dotnet actions. The SkiaSharp row names the Skia library and the third-party notices of its native-asset packages. |
| 1.3 | 2026-10-31 | Accepted by recelilious. Registers in section 6 the two components that stage M1.1's `ios` CI job brings in: the actions/upload-artifact action and the GitHub-hosted runner images, whose macOS image C-0009 section 2 pins in `eng/toolchain.json`. |
