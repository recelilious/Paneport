// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using Paneport.Core.Diagnostics;
using Paneport.Core.Logging;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

// S1.3.7: the entitlements of C-0009 section 4 from the code signature and the embedded profile,
// on synthetic signatures and profiles. Only three Boolean values are read; nothing else of a
// profile, such as its team or its devices, is kept.
public sealed class EntitlementReportTests
{
    [Fact]
    public void AnAdHocBuildDeclaresOnlyTheIncreasedMemoryLimit()
    {
        var readings = Read(SigningSamples.Executable(SigningSamples.Entitlements(increasedMemoryLimit: true)), profile: null);

        Assert.Equal(EntitlementNames.All, readings.Select(reading => reading.Name));
        var limit = readings[1];
        Assert.True(limit.Signature);
        Assert.Equal(EntitlementSourceState.Read, limit.SignatureState);
        Assert.Equal(EntitlementSourceState.Absent, limit.ProfileState);
        Assert.True(limit.Effective);
        Assert.Equal("true in the code signature; no profile, as in an ad-hoc-signed build", limit.Description);
        Assert.Null(readings[0].Signature);
        Assert.False(readings[0].Effective);
        Assert.Equal("absent in the code signature; no profile, as in an ad-hoc-signed build", readings[2].Description);
    }

    [Fact]
    public void AReSignedAppReadsBothSources()
    {
        var executable = SigningSamples.Executable(SigningSamples.Entitlements(true, "<key>get-task-allow</key>\n<true/>\n"));

        var readings = Read(executable, SigningSamples.Profile(increasedMemoryLimit: true, getTaskAllow: true));

        Assert.All(readings.Take(2), reading => Assert.True(reading.Effective));
        Assert.Equal("true in the code signature; true in the profile", readings[0].Description);
        Assert.False(readings[2].Effective);
        Assert.Equal("absent in the code signature; absent in the profile", readings[2].Description);
    }

    [Fact]
    public void AProfileThatDropsAnEntitlementShowsIt()
    {
        var readings = Read(
            SigningSamples.Executable(SigningSamples.Entitlements(increasedMemoryLimit: true)),
            SigningSamples.Profile(increasedMemoryLimit: false, getTaskAllow: true));

        Assert.Equal("true in the code signature; false in the profile", readings[1].Description);
        Assert.False(readings[1].Effective);
    }

    [Fact]
    public void AFatExecutableIsReadThroughItsArm64Slice()
    {
        var fat = SigningSamples.Fat(SigningSamples.Executable(SigningSamples.Entitlements(increasedMemoryLimit: true)));

        Assert.True(Read(fat, profile: null)[1].Signature);
    }

    [Fact]
    public void AnUnsignedExecutableHasNoSignatureSource()
    {
        var executable = SigningSamples.Executable(null);

        var readings = Read(executable, profile: null);

        Assert.False(CodeSignatureReader.HasSignature(new MemoryStream(executable)));
        Assert.All(readings, reading => Assert.Equal(EntitlementSourceState.Absent, reading.SignatureState));
        Assert.All(readings, reading => Assert.Null(reading.Effective));
        Assert.Equal("unknown: neither the code signature nor a profile could be read", readings[1].Description);
        Assert.Equal(
            "the app is not signed; true in the profile",
            Read(executable, SigningSamples.Profile(increasedMemoryLimit: true, getTaskAllow: false))[1].Description);
    }

    [Fact]
    public void ASignatureWithoutTheXmlBlobIsNotTakenForAnUnsignedApp()
    {
        // A tool that writes only the DER form of the entitlements leaves no XML blob to read: the
        // app is signed, and what the signature holds stays unknown.
        var executable = SigningSamples.Executable(SigningSamples.Entitlements(increasedMemoryLimit: true), derOnly: true);

        var readings = Read(executable, profile: null);

        Assert.True(CodeSignatureReader.HasSignature(new MemoryStream(executable)));
        Assert.Null(CodeSignatureReader.ReadEntitlements(new MemoryStream(executable)));
        Assert.All(readings, reading => Assert.Equal(EntitlementSourceState.NoXmlEntitlements, reading.SignatureState));
        Assert.All(readings, reading => Assert.Null(reading.Signature));
        Assert.All(readings, reading => Assert.Null(reading.Effective));
        Assert.Equal("the code signature holds no XML entitlements; no profile, as in an ad-hoc-signed build", readings[1].Description);
    }

    [Fact]
    public void ASignatureWithoutTheXmlBlobLeavesTheProfileToTell()
    {
        var readings = Read(
            SigningSamples.Executable(SigningSamples.Entitlements(increasedMemoryLimit: true), derOnly: true),
            SigningSamples.Profile(increasedMemoryLimit: true, getTaskAllow: false));

        Assert.Equal("the code signature holds no XML entitlements; true in the profile", readings[1].Description);
        Assert.Null(readings[1].Effective);
        Assert.Equal("the code signature holds no XML entitlements; false in the profile", readings[0].Description);
        Assert.False(readings[0].Effective);
    }

    [Fact]
    public void UnreadableSourcesAreLoggedWithTheirTypeAndShownAsUnknown()
    {
        var log = new RecordingLog();

        var readings = EntitlementReport.Read(
            () => new MemoryStream(Encoding.ASCII.GetBytes("not an executable at all, but longer than a header")),
            () => Encoding.ASCII.GetBytes("no property list here"),
            log);

        Assert.All(readings, reading => Assert.Equal(EntitlementSourceState.Unreadable, reading.SignatureState));
        Assert.All(readings, reading => Assert.Equal(EntitlementSourceState.Unreadable, reading.ProfileState));
        Assert.All(readings, reading => Assert.Null(reading.Effective));
        Assert.Equal(
            [
                "Reading the code signature failed with InvalidDataException.",
                "Reading the embedded profile failed with InvalidDataException.",
            ],
            log.Lines.Select(line => line.Message));
        Assert.All(log.Lines, line => Assert.Equal(LogLevel.Warning, line.Level));
    }

    [Fact]
    public void NothingOfTheProfileButTheThreeValuesIsKept()
    {
        var log = new RecordingLog();

        var readings = EntitlementReport.Read(
            () => new MemoryStream(SigningSamples.Executable(SigningSamples.Entitlements(true))),
            () => SigningSamples.Profile(increasedMemoryLimit: true, getTaskAllow: true),
            log);

        string[] texts = [.. readings.Select(reading => reading.ToString()), .. readings.Select(reading => reading.Description), .. log.Lines.Select(line => line.Message)];
        Assert.All(texts, text => Assert.DoesNotContain(SigningSamples.Team, text, StringComparison.Ordinal));
        Assert.All(texts, text => Assert.DoesNotContain(SigningSamples.DeviceIdentifier, text, StringComparison.Ordinal));
    }

    [Fact]
    public void ABrokenSignatureIndexIsUnreadable()
    {
        var executable = SigningSamples.Executable(SigningSamples.Entitlements(true));
        executable[128] = 0x00;

        Assert.Throws<InvalidDataException>(() => CodeSignatureReader.ReadEntitlements(new MemoryStream(executable)));
    }

    [Fact]
    public void APropertyListWithoutTheNestedDictionaryIsUnreadable()
    {
        var plist = Encoding.UTF8.GetBytes(SigningSamples.Plist("<dict>\n<key>Name</key>\n<string>x</string>\n</dict>"));

        Assert.Throws<InvalidDataException>(() => PlistBooleans.Read(plist, EntitlementNames.All, "Entitlements"));
    }

    [Fact]
    public void APropertyListNeverFetchesItsDocumentTypeDefinition()
    {
        // An entity that would expand from a file is ignored with the DTD, never resolved.
        var plist = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?>\n<!DOCTYPE plist [<!ENTITY x SYSTEM \"file:///etc/hostname\">]>\n"
                + "<plist version=\"1.0\"><dict><key>get-task-allow</key><true/></dict></plist>");

        Assert.True(PlistBooleans.Read(plist, EntitlementNames.All, null)[EntitlementNames.GetTaskAllow]);
    }

    private static IReadOnlyList<EntitlementReading> Read(byte[] executable, byte[]? profile) =>
        EntitlementReport.Read(() => new MemoryStream(executable), () => profile, new RecordingLog());
}
