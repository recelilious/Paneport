# Paneport <!-- fill: the version, such as 0.1.0 -->

<!-- fill: Copy this template to docs/releases/<tag>.md, such as docs/releases/v0.1.0.md, on a branch, and fill it in: replace every comment that starts with "fill:" by its text, this one included, and keep the three comments that start with "release:". The release workflow puts the generated text in their place, and in release mode it refuses notes that still hold a "fill:" comment, a gate in scope that has not passed or been waived, or an empty list of known issues. Write no link to the tag: it does not exist yet when the notes are committed, and the workflow writes those links. Relative links, such as those to the evidence records, become links to the tagged files in the release body. -->

## Summary

<!-- release: build -->

<!-- fill: two or three sentences on what this version brings and who it is for. -->

## Gates in scope

The milestone gates of C-0012 section 3 that this release covers, each with its verdict and its latest evidence record, as the gate table in `docs/evidence/README.md` shows them. Every gate in scope has passed or been waived by recelilious (C-0012 section 5).

| Phase | Gate | Verdict | Record |
|---|---|---|---|
<!-- fill: one row per gate in scope, copied from the gate table in docs/evidence/README.md, with a relative link to its latest record, such as ../evidence/YYYY-MM-DD-<topic>.md. Say below the table which gate was waived rather than passed, and why. -->

## Supported devices and systems

Paneport targets the iPhone 17 Pro and 17 Pro Max, with the A19 Pro, and the iPhone 18 Pro and 18 Pro Max, with the A20 Pro, on iOS 26.x and iOS 27.x (C-0001 section 2). Other devices and system versions are neither guaranteed nor deliberately blocked.

JIT, which every game needs, is verified only on the iOS builds that `compat/ios-builds.json` lists in this version's source (C-0006 section 6). On another build the app warns and still runs.

<!-- fill: the iOS builds that compat/ios-builds.json lists at the tag, each with its device model identifier, such as iPhone18,1. -->

## Installation

1. Turn on Developer Mode on the iPhone, in Settings, Privacy & Security.
2. Install the `.ipa` with AltStore 2.2 or later, through AltServer on Windows or macOS, signed with your own Apple ID (C-0009 section 5). AltStore keeps the increased memory limit that the app asks for when it re-signs the app with your Apple ID. The `.ipa` is signed ad hoc: it holds no Apple identity and no provisioning profile.
3. SideStore and Sideloadly are not supported routes. SideStore is reported to drop the increased memory limit for free accounts, and Sideloadly is untested.
4. With a free Apple account, AltStore must re-sign the app every 7 days, and the account holds at most three sideloaded apps at a time. A paid account signs the app for a year.
5. Games need JIT. When Paneport asks for it, enable it with StikDebug, a separate app (C-0006).
6. To update, install the new `.ipa` over the installed app. Do not delete the app first: its Wine prefixes, games and saves live in its container and would be deleted with it (C-0004 section 4).

## Known issues

<!-- fill: keep the items below that still hold, and add the known issues of this version from the evidence records and session logs of its stages. -->

- Switching games needs a relaunch of Paneport (C-0004 section 1).
- Saves inside the Wine prefixes are not backed up (C-0004 section 4).
- 32-bit titles that use the Steam API do not run through the Dock host (C-0007 section 6).
- Games that need anti-cheat are out of scope (C-0001 section 5).
- SideStore and Sideloadly are not supported routes, and a free Apple account must re-sign the app every 7 days (C-0009 section 5).
- JIT is verified only on the iOS builds that `compat/ios-builds.json` lists (C-0006 section 6).
- Paneport runs on Mono from .NET for iOS 10, whose support ends on 2027-05-11 (C-0010 section 1).

## Licenses

Paneport's own code is under the MIT license. The package as a whole is not: it contains components under their own licenses, among them Wine and SteamKit2 under the LGPL (C-0003 section 1). `THIRD_PARTY_NOTICES.md` and the license texts attached to this release list every component of the package with its license and the full text of its license and notices. The app carries the same files in `licenses/` and shows them on its open-source licenses screen.

The LGPL lets you replace its components. The Unix side of Wine ships as frameworks inside the app that you can replace, and SteamKit2, which the build compiles into the app, can be swapped by rebuilding the app from the attached sources and installing your build with your own Apple ID (C-0003 section 3).

## Compliance

- The package holds no binary from Microsoft, Valve or a game. Microsoft's runtimes, Valve's client and the game files are downloaded by your device from their official sources, or provided by you (C-0001 section 7, C-0003 section 8).
- Paneport never bypasses, removes or emulates any DRM, and holds no `steam_api` emulation (C-0001 section 7).
- Paneport is not affiliated with Microsoft, Valve or Apple (C-0001 section 7).

## Sources

This release attaches the source of this version of Paneport, of every fork at the revision that it uses, and of each LGPL component that is neither this repository nor a fork, so the source of every LGPL component comes from the same place as the `.ipa` (C-0009 section 6, LGPL-2.1 section 6).

<!-- release: sources -->

## Checksums

The SHA-256 of every file attached to this release. On Windows, `Get-FileHash -Algorithm SHA256 <file>` in PowerShell prints it; on macOS, `shasum -a 256 <file>`.

<!-- release: checksums -->
