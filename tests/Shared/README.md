<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Shared

Source files that several test projects compile in, each through a `Compile` item with a link in its project file. This folder is not a project, so no test project depends on another one.

| File | What it is |
|---|---|
| `ProjectReferences.cs` | Reads the project references that the build recorded in the test assembly's `deps.json` file. The dependency direction tests use it. |
