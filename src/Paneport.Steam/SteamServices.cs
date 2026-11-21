// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Net;
using Paneport.Core.Logging;
using Paneport.Core.Steam;
using Paneport.Steam.Content;
using Paneport.Steam.Gate;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.Session;
using Paneport.Steam.SteamKit;
using SteamKit2;

namespace Paneport.Steam;

/// <summary>
/// The Steam service and the steps of the Steam gate on SteamKit2, wired over one SteamKit2
/// client (<c>docs/design/steam-service.md</c>). An app head creates one at launch and disposes it
/// at the end.
/// </summary>
/// <remarks>
/// Creating it connects to nothing. Every HTTP client that SteamKit2 asks for is built on
/// <see cref="SocketsHttpHandler"/>, because the handler that .NET for iOS uses by default cannot
/// upgrade a connection to WebSocket (<c>avalonia_ui_layer.md</c> sections 2.3 and 3.4), and the
/// content downloads get clients of their own.
/// </remarks>
public sealed class SteamServices : IDisposable
{
    private const string LogSource = "SteamServices";

    private static readonly TimeSpan s_httpConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly SteamKitConnection _connection;
    private readonly SteamKitContent _content;
    private readonly Action<string, string>? _debugListener;

    private SteamServices(SteamServiceOptions options)
    {
        var log = new RedactingLog(options.Log);
        var configuration = SteamConfiguration.Create(builder =>
        {
            builder.WithHttpClientFactory(CreateHttpClient);
            builder.WithProtocolTypes(options.TcpOnly ? ProtocolTypes.Tcp : ProtocolTypes.Tcp | ProtocolTypes.WebSocket);
            if (options.MachineIdentity is { } identity)
            {
                builder.WithMachineInfoProvider(new InstallIdentityMachineInfo(identity));
            }
        });

        if (options.SteamKitDebugOutput)
        {
            _debugListener = (category, message) => log.Write(LogLevel.Debug, "SteamKit2", $"{category}: {message}");
            DebugLog.AddListener(_debugListener);
            DebugLog.Enabled = true;
        }

        _connection = new SteamKitConnection(configuration, options.DeviceName, log);
        _content = new SteamKitContent(_connection.Client);
        Service = new SteamService(
            _connection,
            new SteamKitAuthenticator(_connection.Client, options.DeviceName),
            options.Tokens,
            new SystemDelays(options.Time),
            ReconnectPolicy.Default,
            options.Time,
            log);
        Gate = new GateSteps(
            Service.Session,
            new SteamKitApps(_connection.Client),
            _content,
            new TestDownloadFolder(options.DownloadsFolder),
            options.Time,
            log);
        log.Write(
            LogLevel.Information,
            LogSource,
            $"The Steam service is ready: transports {(options.TcpOnly ? "TCP" : "WebSocket and TCP")}, machine identity {(options.MachineIdentity is null ? "SteamKit2's" : "the installation's")}, SteamKit2 debug output {(options.SteamKitDebugOutput ? "on" : "off")}.");
    }

    /// <summary>Gets the Steam service.</summary>
    public SteamService Service { get; }

    /// <summary>Gets the steps of the Steam gate's page.</summary>
    public ISteamGateSteps Gate { get; }

    /// <summary>Creates the service over a new SteamKit2 client. It connects to nothing yet.</summary>
    /// <param name="options">What the head gives the service.</param>
    /// <returns>The service and the gate's steps.</returns>
    public static SteamServices Create(SteamServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DeviceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DownloadsFolder);
        return new SteamServices(options);
    }

    /// <summary>Ends the connection and stops SteamKit2's callback thread.</summary>
    public void Dispose()
    {
        Service.Dispose();
        _content.Dispose();
        _connection.Dispose();
        if (_debugListener is not null)
        {
            DebugLog.RemoveListener(_debugListener);
        }
    }

    private static HttpClient CreateHttpClient(HttpClientPurpose purpose) => new(new SocketsHttpHandler
    {
        ConnectTimeout = s_httpConnectTimeout,
        AutomaticDecompression = DecompressionMethods.All,
    });
}
