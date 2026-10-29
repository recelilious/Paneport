<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Steam.Tests

Tests of [Paneport.Steam](../../src/Paneport.Steam/README.md). They never talk to Steam: later stages test with hand-written fakes and synthetic data only (C-0011 section 1).

| File | What it tests |
|---|---|
| `DependencyDirectionTests.cs` | Paneport.Steam references no project other than Paneport.Core, and this test project references only Paneport.Steam and its dependencies (C-0004 section 2). |

M2.3 adds the tests of the Steam service.
