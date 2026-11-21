<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# eng/trimming

The trimming warnings from inside dependencies that our builds excuse. This is the approved list under `eng/` that [repository-checks.md](../../docs/design/repository-checks.md) section 7 names, and recelilious approved it on 2026-11-21.

| File | What it excuses |
|---|---|
| `protobuf-net.xml` | 61 warnings in 58 methods of protobuf-net 3.4.40 |
| `protobuf-net.Core.xml` | 64 warnings in 60 methods of protobuf-net.Core 3.4.40 |

- **What an entry is.** Each file is an attributes file of the trimmer, ILLink. A `method` element names a method by its signature, inside its `assembly` and `type`. It holds one `UnconditionalSuppressMessageAttribute` per warning code, with the category `Trimming` and a justification that cites C-0009 section 3.
- **Which warnings.** The list holds every warning that the trimmer reports for protobuf-net and protobuf-net.Core when it keeps them whole. The iOS build does that, because neither assembly is marked trimmable. The trimmed test app keeps only the code that our libraries reach and reports 11 of them, all in protobuf-net.Core.
- **Where they come from.** Most come from protobuf-net's reflection over types known only at run time, which serves callers without a compile-time model. The rest come from annotations that its overrides do not match (IL2046, IL2095). SteamKit2 serializes Steam's messages through its compile-time model, and the device check S2.3.8 runs them in the ahead-of-time compiled app (C-0009 section 3, [steam-service.md](../../docs/design/steam-service.md) section 10).
- **Who reads it.** `tests/Paneport.TrimTest` and `src/Paneport.iOS/AppHead.props`, which both iOS flavors import, add every file here to `_ILLinkSuppressions`. That is an internal item of the SDK, which `PrepareForILLink` passes to the trimmer as `--link-attributes`. An SDK that renames or drops the item would bring the warnings back and fail the builds, not hide them. The iOS heads also set `TrimmerSingleWarn` to false on the two assemblies, so that their warnings show by code and method instead of as one IL2104 per assembly.
- **What still fails.** A warning outside the list fails the build. So does a method of the list that the trimmer cannot find in the package (IL2009). With one entry taken out, the build fails on exactly that warning (repository-checks.md section 9).
- **Making the list.** A publish of the trimmed test app in partial trim mode keeps protobuf-net whole, as the iOS build does, and lists all its warnings. With `_TrimmerShowRedundantSuppressions` it also lists every entry that no warning uses (repository-checks.md sections 7 and 8).
- **When it changes.** A version change of protobuf-net makes the list again in the same reviewed change, which recelilious approves. The list goes once protobuf-net is clean under trimming, or once SteamKit2 no longer needs it.
