<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Shared

Source files that several test projects compile in, each through a `Compile` item with a link in its project file. This folder is not a project, so no test project depends on another one.

| File or folder | What it is |
|---|---|
| `ProjectReferences.cs` | Reads the project references that the build recorded in the test assembly's `deps.json` file. The dependency direction tests use it. |
| `Fakes/` | Hand-written fakes of the services that the shell needs (C-0011 section 1), the made-up titles they list, and `FakeServices`, which collects them. The settings fake can hold a load or a save open, as a store on disk would take its time. For M1.3: the native host's calls (`FakeNativeHost`, whose model call fails on a host older than 1.1, as the real one does), the device, the clock, the UI dispatcher, the file system, the backup marks and the secure-item store, a log that records its lines, synthetic code signatures and profiles (`SigningSamples`) and crash records (`CrashRecordBytes`). Paneport.Core.Tests and Paneport.UI.Tests compile them in. |
