// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Paneport.Core.Logging;
using Paneport.Core.Steam;
using Paneport.Steam.Apps;
using Paneport.Steam.Content;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.Session;
using static System.FormattableString;

namespace Paneport.Steam.Gate;

// The steps of the SteamKit2 gate after sign-in (S2.3.4 to S2.3.6, steam-service.md section 6). It
// keeps the depot key, the manifest request code and the manifest to itself, registers the key and
// the code with the log filter as soon as it has them, and tells the page only whether it got them.
// Each chunk is checked against its manifest entry, and only a chunk that matches is written.
internal sealed class GateSteps : ISteamGateSteps
{
    // How many content servers a download tries before it gives up.
    public const int MaxServers = 4;

    private const string LogSource = "SteamGate";

    private readonly SteamSession _session;
    private readonly ISteamApps _apps;
    private readonly ISteamContent _content;
    private readonly TestDownloadFolder _folder;
    private readonly TimeProvider _time;
    private readonly RedactingLog _log;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string?> _serverTokens = new(StringComparer.OrdinalIgnoreCase);

    private uint _appId;
    private SteamAppInfo? _app;
    private Dictionary<uint, ulong> _publicManifests = [];
    private uint _keyDepot;
    private byte[]? _depotKey;
    private uint _codeDepot;
    private ulong _manifestCode;
    private DepotManifestData? _manifest;
    private IReadOnlyList<ContentServer>? _servers;

    public GateSteps(
        SteamSession session,
        ISteamApps apps,
        ISteamContent content,
        TestDownloadFolder folder,
        TimeProvider time,
        RedactingLog log)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _session = session;
        _apps = apps;
        _content = content;
        _folder = folder;
        _time = time;
        _log = log;
    }

    public async Task<int> CountLicensesAsync(CancellationToken cancellationToken)
    {
        RequireLoggedOn();
        var licenses = await _session.GetLicensesAsync(cancellationToken).ConfigureAwait(false);
        _log.Write(LogLevel.Information, LogSource, Invariant($"The account has {licenses.Count} licenses."));
        return licenses.Count;
    }

    public async Task<SteamAppInfo> ReadAppInfoAsync(uint appId, CancellationToken cancellationToken)
    {
        RequireLoggedOn();
        if (appId == 0)
        {
            throw new SteamStepException("An app ID is a number above 0.");
        }

        var accessToken = await _apps.GetAppAccessTokenAsync(appId, cancellationToken).ConfigureAwait(false);
        var node = await _apps.GetAppInfoAsync(appId, accessToken, cancellationToken).ConfigureAwait(false);
        var info = AppInfoReader.Read(appId, node);
        var manifests = new Dictionary<uint, ulong>();
        foreach (var depot in info.WindowsDepots)
        {
            var id = depot.DepotId.ToString(CultureInfo.InvariantCulture);
            if (AppInfoReader.ReadManifestId(node?["depots"]?[id]?["manifests"]?["public"]) is { } manifestId)
            {
                manifests[depot.DepotId] = manifestId;
            }
        }

        lock (_lock)
        {
            Forget();
            _appId = appId;
            _app = info;
            _publicManifests = manifests;
        }

        _log.Write(
            LogLevel.Information,
            LogSource,
            Invariant($"Read the app information: {info.WindowsDepots.Count} Windows depots, {info.Problems.Count} problems."));
        return info;
    }

    public async Task<SteamGrant> GetDepotKeyAsync(uint depotId, CancellationToken cancellationToken)
    {
        RequireLoggedOn();
        var appId = RequireDepot(depotId);
        var result = await _apps.GetDepotKeyAsync(depotId, appId, cancellationToken).ConfigureAwait(false);
        if (!result.IsGranted)
        {
            _log.Write(LogLevel.Warning, LogSource, $"Steam granted no depot key: {SteamResults.Describe(result.Result)}.");
            return SteamGrant.NotGranted;
        }

        var key = result.Key!;
        _log.Protect(key);
        lock (_lock)
        {
            if (_depotKey is { } old && !ReferenceEquals(old, key))
            {
                if (!old.AsSpan().SequenceEqual(key))
                {
                    _log.Unprotect(old);
                }

                CryptographicOperations.ZeroMemory(old);
            }

            _depotKey = key;
            _keyDepot = depotId;
        }

        _log.Write(LogLevel.Information, LogSource, "Steam granted the depot key.");
        return SteamGrant.Obtained;
    }

    public async Task<SteamGrant> GetManifestRequestCodeAsync(uint depotId, CancellationToken cancellationToken)
    {
        RequireLoggedOn();
        var appId = RequireDepot(depotId);
        var manifestId = PublicManifestOf(depotId);
        var code = await _content.GetManifestRequestCodeAsync(depotId, appId, manifestId, cancellationToken).ConfigureAwait(false);
        if (code == 0)
        {
            _log.Write(LogLevel.Warning, LogSource, "Steam granted no manifest request code.");
            return SteamGrant.NotGranted;
        }

        _log.Protect(code.ToString(CultureInfo.InvariantCulture));
        lock (_lock)
        {
            _manifestCode = code;
            _codeDepot = depotId;
        }

        _log.Write(LogLevel.Information, LogSource, "Steam granted the manifest request code.");
        return SteamGrant.Obtained;
    }

    public async Task<ManifestSummary> DownloadManifestAsync(uint depotId, CancellationToken cancellationToken)
    {
        RequireLoggedOn();
        RequireDepot(depotId);
        var manifestId = PublicManifestOf(depotId);
        byte[] key;
        ulong code;
        uint appId;
        lock (_lock)
        {
            if (_depotKey is null || _keyDepot != depotId)
            {
                throw new SteamStepException("Get the depot key of this depot first.");
            }

            code = _manifestCode != 0 && _codeDepot == depotId
                ? _manifestCode
                : throw new SteamStepException("Get the manifest request code of this depot first.");
            appId = _appId;
            key = CopyOfKey(_depotKey);
        }

        using var wipe = new Wiping(key);

        var started = _time.GetTimestamp();
        var servers = await ServersAsync(cancellationToken).ConfigureAwait(false);
        foreach (var server in servers.Take(MaxServers))
        {
            try
            {
                var manifest = await WithServerTokenAsync(
                    appId,
                    depotId,
                    server,
                    token => _content.DownloadManifestAsync(depotId, manifestId, code, server, key, token, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                var summary = ManifestSummaries.Summarize(manifest, _time.GetElapsedTime(started));
                lock (_lock)
                {
                    _manifest = manifest;
                }

                var counts = Invariant($"{summary.FileCount} files, {summary.ChunkCount} chunks, {summary.TotalSize} bytes");
                _log.Write(
                    LogLevel.Information,
                    LogSource,
                    Invariant($"Read the manifest: {counts}, {summary.DownloadTime.TotalMilliseconds:0} ms."));
                return summary;
            }
            catch (ContentDownloadException exception)
            {
                _log.Write(LogLevel.Warning, LogSource, $"A content server failed the manifest download ({Describe(exception)}).");
            }
        }

        var tried = Math.Min(servers.Count, MaxServers);
        throw new SteamStepException(Invariant($"No content server gave the manifest ({tried} tried)."));
    }

    public async Task<ChunkRunResult> DownloadChunksAsync(int count, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        RequireLoggedOn();
        DepotManifestData manifest;
        byte[] key;
        uint appId;
        lock (_lock)
        {
            manifest = _manifest ?? throw new SteamStepException("Download the manifest first.");
            if (_depotKey is null || _keyDepot != manifest.DepotId)
            {
                throw new SteamStepException("Get the depot key of this depot first.");
            }

            appId = _appId;
            key = CopyOfKey(_depotKey);
        }

        using var wipe = new Wiping(key);

        var chunks = ManifestSummaries.DistinctChunks(manifest).Take(count).ToList();
        var servers = await ServersAsync(cancellationToken).ConfigureAwait(false);
        var started = _time.GetTimestamp();
        int verified = 0, rejected = 0, failed = 0;
        ulong downloadedBytes = 0, verifiedBytes = 0;
        foreach (var chunk in chunks)
        {
            var outcome = await DownloadChunkAsync(appId, manifest.DepotId, chunk, key, servers, cancellationToken).ConfigureAwait(false);
            switch (outcome.Check)
            {
                case ChunkCheck.Matches:
                    verified++;
                    downloadedBytes += chunk.CompressedLength;
                    verifiedBytes += chunk.UncompressedLength;
                    break;
                case ChunkCheck.WrongLength or ChunkCheck.WrongHash:
                    rejected++;
                    downloadedBytes += chunk.CompressedLength;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        var result = new ChunkRunResult(chunks.Count, verified, rejected, failed, downloadedBytes, verifiedBytes, _time.GetElapsedTime(started));
        var outcomes = Invariant($"{verified} verified, {rejected} rejected, {failed} failed of {chunks.Count}");
        _log.Write(LogLevel.Information, LogSource, Invariant($"Downloaded chunks: {outcomes}, {result.Time.TotalMilliseconds:0} ms."));
        return result;
    }

    public Task DeleteTestDataAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _folder.Delete();
        lock (_lock)
        {
            Forget();
            _app = null;
            _appId = 0;
            _publicManifests = [];
        }

        _log.Write(LogLevel.Information, LogSource, "Deleted the test data.");
        return Task.CompletedTask;
    }

    private static string Describe(ContentDownloadException exception) =>
        exception.Status is { } status
            ? Invariant($"HTTP {(int)status}")
            : exception.InnerException?.GetType().Name ?? nameof(ContentDownloadException);

    // Downloads one chunk from the first server that answers, and checks it. The outcome's check is
    // null when no server gave the chunk.
    private async Task<(ChunkCheck? Check, int Written)> DownloadChunkAsync(
        uint appId,
        uint depotId,
        ManifestChunk chunk,
        byte[] key,
        IReadOnlyList<ContentServer> servers,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[chunk.UncompressedLength];
        foreach (var server in servers.Take(MaxServers))
        {
            int written;
            try
            {
                written = await WithServerTokenAsync(
                    appId,
                    depotId,
                    server,
                    token => _content.DownloadChunkAsync(depotId, chunk, server, key, buffer, token, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ChunkRejectedException)
            {
                _log.Write(LogLevel.Warning, LogSource, "A chunk failed SteamKit2's processing and was rejected.");
                return (ChunkCheck.WrongHash, 0);
            }
            catch (ContentDownloadException exception)
            {
                _log.Write(LogLevel.Warning, LogSource, $"A content server failed a chunk download ({Describe(exception)}).");
                continue;
            }

            var content = buffer.AsMemory(0, Math.Clamp(written, 0, buffer.Length));
            var check = written == buffer.Length ? ChunkVerifier.Check(chunk, content.Span) : ChunkCheck.WrongLength;
            if (check != ChunkCheck.Matches)
            {
                _log.Write(LogLevel.Warning, LogSource, $"A chunk did not match its manifest entry ({check}) and was rejected.");
                return (check, written);
            }

            await _folder.WriteChunkAsync(depotId, chunk, content, cancellationToken).ConfigureAwait(false);
            return (check, written);
        }

        return (null, 0);
    }

    // Runs a download, and once more with a server token when the server refused it without one.
    private async Task<T> WithServerTokenAsync<T>(
        uint appId,
        uint depotId,
        ContentServer server,
        Func<string?, Task<T>> download,
        CancellationToken cancellationToken)
    {
        string? token;
        lock (_lock)
        {
            _serverTokens.TryGetValue(server.Host, out token);
        }

        try
        {
            return await download(token).ConfigureAwait(false);
        }
        catch (ContentDownloadException exception) when (exception.Status == HttpStatusCode.Forbidden && token is null)
        {
            var fresh = await _content.GetServerTokenAsync(appId, depotId, server, cancellationToken).ConfigureAwait(false);
            if (fresh is null)
            {
                throw;
            }

            _log.Protect(fresh);
            lock (_lock)
            {
                _serverTokens[server.Host] = fresh;
            }

            return await download(fresh).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<ContentServer>> ServersAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (_servers is { Count: > 0 } known)
            {
                return known;
            }
        }

        var servers = await _content.GetServersAsync(cancellationToken).ConfigureAwait(false);
        if (servers.Count == 0)
        {
            throw new SteamStepException("Steam named no content server.");
        }

        lock (_lock)
        {
            _servers = servers;
        }

        return servers;
    }

    private void RequireLoggedOn()
    {
        if (!_session.IsLoggedOn)
        {
            throw new SteamStepException("Sign in first.");
        }
    }

    // The app ID, when the depot is one of the Windows depots of the app read last.
    private uint RequireDepot(uint depotId)
    {
        lock (_lock)
        {
            if (_app is null)
            {
                throw new SteamStepException("Read the app information first.");
            }

            if (!_app.WindowsDepots.Any(depot => depot.DepotId == depotId))
            {
                throw new SteamStepException(Invariant($"Depot {depotId} is not a Windows depot of app {_appId}."));
            }

            return _appId;
        }
    }

    private ulong PublicManifestOf(uint depotId)
    {
        lock (_lock)
        {
            return _publicManifests.TryGetValue(depotId, out var manifestId)
                ? manifestId
                : throw new SteamStepException(Invariant($"Depot {depotId} names no public manifest."));
        }
    }

    // A step's own copy of the depot key: a key fetched again, or forgotten, while the step runs is
    // wiped without changing the key that the step decrypts with. The step wipes its copy when it
    // ends.
    private static byte[] CopyOfKey(byte[] key) => (byte[])key.Clone();

    // Forgets the key, the code, the server tokens and the manifest, wipes the key's bytes and takes
    // their text out of the log filter, which would otherwise keep it for the service's life.
    // Called under the lock.
    private void Forget()
    {
        if (_depotKey is { } key)
        {
            _log.Unprotect(key);
            CryptographicOperations.ZeroMemory(key);
        }

        if (_manifestCode != 0)
        {
            _log.Unprotect(_manifestCode.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var token in _serverTokens.Values)
        {
            _log.Unprotect(token);
        }

        _depotKey = null;
        _keyDepot = 0;
        _manifestCode = 0;
        _codeDepot = 0;
        _manifest = null;
        _serverTokens.Clear();
    }

    // Wipes a step's copy of the depot key when the step ends.
    private readonly struct Wiping(byte[] bytes) : IDisposable
    {
        public void Dispose() => CryptographicOperations.ZeroMemory(bytes);
    }
}
