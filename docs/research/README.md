# Research

This folder holds the research behind Paneport: an implementation roadmap and nine research notes. Claude (Anthropic) produced all of them in October 2026, working as AI research agents for the project. They are published as a snapshot for anyone who finds them useful, not as maintained documents. The binding project rules are the [contracts](../contracts/README.md); this research is not binding.

## Reading guide

- **Start with the roadmap.** It turns the notes into one plan and is the shortest way in.
- **The notes are raw research.** They were written for the agent that compiled the roadmap, so some of them contain remarks addressed to "the report writer".
- **Check before relying on a claim.** Every claim carries a date and a source link, and facts about iOS, emulators and Steam change quickly.
- **What changed before publishing.** References to the project owner were made neutral and an author and timestamp header was added. Nothing else in the notes was edited.

## Files

All timestamps are UTC.

| File | Timestamp | What it covers |
|---|---|---|
| [roadmap-2026-10.md](roadmap-2026-10.md) | 2026-10-27T14:01Z | The implementation roadmap: architecture, layer choices, phased milestones with acceptance criteria, key risks, build and signing, performance, and the Steam compliance path |
| [ios_platform_constraints.md](ios_platform_constraints.md) | 2026-10-27T07:27Z | What iOS 26 and 27 allow a sideloaded app: JIT under TXM, memory entitlements, 16 KB pages, the ban on child processes, the hypervisor, signing and distribution |
| [x86_to_arm_translation.md](x86_to_arm_translation.md) | 2026-10-27T07:28Z | x86 and x86-64 to ARM64 translators (FEX-Emu, Box64, QEMU, Hangover), how they fit Wine on Apple silicon, and the performance to expect |
| [steam_integration.md](steam_integration.md) | 2026-10-27T07:30Z | Steam integration options: the real client under Wine, protocol libraries such as SteamKit2, the steam_api contract, and the legal tiers |
| [precedents_and_ux_layer.md](precedents_and_ux_layer.md) | 2026-10-27T07:32Z | Prior art on iOS, Android and Apple silicon, plus the iOS-side layer: controllers, touch controls, display, storage and distribution |
| [wine_on_ios.md](wine_on_ios.md) | 2026-10-27T07:33Z | Running Wine inside one iOS app: where Wine's architecture conflicts with iOS, upstream Darwin ARM64 status, the display driver, components and LGPL obligations |
| [graphics_directx_to_metal.md](graphics_directx_to_metal.md) | 2026-10-27T07:35Z | Direct3D 9 to 12 and OpenGL on Metal: MoltenVK, DXVK, DXMT, D3DMetal, vkd3d-proton, shader pipelines, presentation and audio |
| [steam_launch_single_process.md](steam_launch_single_process.md) | 2026-10-27T13:01Z | Launching Steam games from a single iOS process so that steam_api and Steam DRM work: ranked architectures and their legal tiers |
| [process_and_memory_model.md](process_and_memory_model.md) | 2026-10-27T13:10Z | Hosting many Windows processes in one iOS process, extra processes through app extensions, and measured memory ceilings |
| [avalonia_ui_layer.md](avalonia_ui_layer.md) | 2026-10-27T13:21Z | Avalonia and .NET as the UI shell: rendering and overlays on iOS, the .NET iOS toolchain, SteamKit2 under AOT, and a framework comparison, with each key claim checked by an independent verifier |

## Authorship

Every file in this folder was written by Claude (Anthropic) for the Paneport project and carries its author and timestamp at the top. The notes come from separate research agents that searched the web and read primary sources. The roadmap was compiled from those notes and then checked against them.
