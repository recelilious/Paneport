// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core;

namespace Paneport.Tests.Fakes;

// One fake for each service the shell needs, so that a test can set them up and check them after.
internal sealed class FakeServices
{
    public FakeRuntimeHost Host { get; } = new();

    public FakeLibraryService Library { get; } = new(TestTitles.All);

    public FakeSteamService Steam { get; } = new();

    public FakeSettingsStore Settings { get; } = new();

    public FakeTokenStore Tokens { get; } = new();

    public RecordingLog Log { get; } = new();

    public AppServices ToAppServices() => new(Host, Library, Steam, Settings, Tokens, Log);
}
