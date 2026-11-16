// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core;
using Paneport.Core.Diagnostics;
using Paneport.Core.Storage;

namespace Paneport.Tests.Fakes;

// One fake for each service the shell needs, so that a test can set them up and check them after.
internal sealed class FakeServices
{
    public FakeServices()
    {
        Memory = new MemoryMonitor(Host, Device, Time, Log);
    }

    public FakeRuntimeHost Host { get; } = new();

    public FakeLibraryService Library { get; } = new(TestTitles.All);

    public FakeSteamService Steam { get; } = new();

    public FakeSettingsStore Settings { get; } = new();

    public FakeTokenStore Tokens { get; } = new();

    public RecordingLog Log { get; } = new();

    public FakeDeviceInfo Device { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public MemoryMonitor Memory { get; }

    public BuildStamp Build { get; set; } = new(
        Commit: "0123456789abcdef0123456789abcdef01234567",
        Version: "0.1.0 (7)",
        Configuration: "Release",
        Runtime: "Mono",
        CompilationMode: "Full ahead-of-time",
        Interpreter: "Off",
        HostImport: "Framework");

    public IReadOnlyList<EntitlementReading> Entitlements { get; set; } = [];

    public CrashRecordStore? CrashRecords { get; set; }

    public IReadOnlyList<FolderState> Folders { get; set; } = [];

    public DiagnosticsSources ToDiagnosticsSources() =>
        new(Memory, Device, Build, Entitlements, CrashRecords, () => Folders);

    public AppServices ToAppServices() =>
        new(Host, Library, Steam, Settings, Tokens, Log, ToDiagnosticsSources());
}
