<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport

> Run Windows programs on iPhone 17 Pro and iPhone 18 Pro, and launch the Windows games in your own Steam library through Steam.

**Status:** contracts and a development plan. There is no runnable code yet.

## The name

Paneport combines pane and port.

- **pane** stands for Windows, as in a window pane.
- **port** means porting, and also suggests portable and a harbor.

Pane sounds like pain, which fits how porting usually feels.

## What it is

A sideloaded iOS app built from four layers:

- **Windows compatibility layer:** an ARM64 build of upstream Wine, with the whole runtime inside one iOS process.
- **CPU translation:** dynamic translation of x86 and x86-64 machine code to ARM64, which needs JIT.
- **Graphics:** Direct3D calls translated to Metal.
- **Steam integration:** sign in to the user's own account, manage the library, and keep each game's Steam features and DRM working as intended.

The UI shell is Avalonia (.NET). [C-0004](docs/contracts/C-0004-architecture-and-layout.md) and [C-0010](docs/contracts/C-0010-languages-and-style.md) record the technology choices.

## Requirements

- Developer Mode is enabled, and the app is signed with your own Apple ID and sideloaded.
- JIT is obtained by attaching a debugger.
- The app will not be published on the App Store.

## Contracts first

This project writes contracts before implementation. All contracts live in [docs/contracts](docs/contracts/README.md).

| Number | Title |
|---|---|
| [C-0001](docs/contracts/C-0001-project-charter.md) | Project charter |
| [C-0002](docs/contracts/C-0002-repository-and-commits.md) | Repository and commits |
| [C-0003](docs/contracts/C-0003-licensing.md) | Open-source licensing |
| [C-0004](docs/contracts/C-0004-architecture-and-layout.md) | Architecture, components and layout |
| [C-0005](docs/contracts/C-0005-native-host-interface.md) | Native runtime host interface |
| [C-0006](docs/contracts/C-0006-jit-request-protocol.md) | JIT request protocol |
| [C-0007](docs/contracts/C-0007-steam-integration.md) | Steam integration |
| [C-0008](docs/contracts/C-0008-compatibility-database.md) | Compatibility database |
| [C-0009](docs/contracts/C-0009-build-signing-release.md) | Build, signing and release |
| [C-0010](docs/contracts/C-0010-languages-and-style.md) | Languages and code style |
| [C-0011](docs/contracts/C-0011-testing-review-quality.md) | Testing, review and quality tools |
| [C-0012](docs/contracts/C-0012-acceptance-process.md) | Acceptance process |

## Development plan

Development is split into Large, Medium and Small stages that can each be verified. The plan, the stage map and one brief per Large stage are in [docs/design](docs/design/README.md).

## Research

The research behind the design, including the implementation roadmap, is in [docs/research](docs/research/README.md).

## License

Original code in this repository is released under the [MIT License](LICENSE). Upstream components such as Wine and DXMT keep their own licenses; see [C-0003](docs/contracts/C-0003-licensing.md).

## Disclaimer

This project is not affiliated with Microsoft, Valve or Apple. Windows, Steam and iPhone are trademarks of their respective owners.

This project does not distribute any games or any binaries from Microsoft, Valve or game publishers, and it does not bypass any DRM.
