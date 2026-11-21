// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.SteamKit;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Steam.Tests;

// The service over SteamKit2 as the heads create it, and the installation's machine identity
// (S2.3.1, steam-service.md section 5). Nothing here connects to Steam.
public sealed class SteamServicesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void CreatingTheServicesConnectsToNothing()
    {
        var log = new RecordingLog();
        var folder = Path.Combine(Path.GetTempPath(), "paneport-steam-tests-" + Guid.NewGuid().ToString("N"));
        using (var services = SteamServices.Create(new SteamServiceOptions
        {
            Log = log,
            Tokens = new FakeTokenStore(),
            DeviceName = "Paneport (tests)",
            DownloadsFolder = folder,
            MachineIdentity = RandomNumberGenerator.GetBytes(MachineIdentity.Length),
        }))
        {
            Assert.Equal(SteamSessionState.SignedOut, services.Service.Status.State);
            Assert.Equal(SteamTransport.None, services.Service.Status.Transport);
            Assert.Equal(SteamSignInState.SignedOut, services.Service.SignInState);
        }

        Assert.Contains(log.Lines, line => line.Message.StartsWith("The Steam service is ready", StringComparison.Ordinal));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void TheDeviceNameAndTheDownloadsFolderAreRequired()
    {
        Assert.Throws<ArgumentException>(() => SteamServices.Create(new SteamServiceOptions
        {
            Log = new RecordingLog(),
            Tokens = new FakeTokenStore(),
            DeviceName = " ",
            DownloadsFolder = Path.GetTempPath(),
        }));
    }

    [Fact]
    public async Task TheMachineIdentityIsCreatedOnceAndKept()
    {
        var store = new FakeSecureItemStore();

        var first = await MachineIdentity.LoadOrCreateAsync(store, Token);
        var second = await MachineIdentity.LoadOrCreateAsync(store, Token);

        Assert.Equal(MachineIdentity.Length, first.Length);
        Assert.Equal(first, second);
        Assert.Equal(first, store.Items[MachineIdentity.ItemKey]);
        Assert.NotEqual(SecureTokenStore.ItemKey, MachineIdentity.ItemKey);
    }

    [Fact]
    public async Task AnUnreadableMachineIdentityIsReplaced()
    {
        var store = new FakeSecureItemStore();
        store.Plant(MachineIdentity.ItemKey, [1, 2, 3]);

        var identity = await MachineIdentity.LoadOrCreateAsync(store, Token);

        Assert.Equal(MachineIdentity.Length, identity.Length);
        Assert.Equal(identity, store.Items[MachineIdentity.ItemKey]);
    }

    [Fact]
    public void TheMachineValuesComeFromTheIdentityAlone()
    {
        var identity = RandomNumberGenerator.GetBytes(MachineIdentity.Length);
        var info = new InstallIdentityMachineInfo(identity);
        var again = new InstallIdentityMachineInfo((byte[])identity.Clone());
        var other = new InstallIdentityMachineInfo(RandomNumberGenerator.GetBytes(MachineIdentity.Length));

        Assert.Equal(info.GetMachineGuid(), again.GetMachineGuid());
        Assert.Equal(info.GetMacAddress(), again.GetMacAddress());
        Assert.Equal(info.GetDiskId(), again.GetDiskId());
        Assert.NotEqual(info.GetMachineGuid(), info.GetMacAddress());
        Assert.NotEqual(info.GetMachineGuid(), info.GetDiskId());
        Assert.NotEqual(info.GetMachineGuid(), other.GetMachineGuid());
        Assert.Throws<ArgumentException>(() => new InstallIdentityMachineInfo(new byte[MachineIdentity.Length - 1]));
    }
}
