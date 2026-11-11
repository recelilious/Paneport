// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Interop.Tests;
using Xunit;

// One host for every test, because pp_init runs once per process, and one test at a time,
// because they share its handlers.
[assembly: AssemblyFixture(typeof(HostFixture))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
