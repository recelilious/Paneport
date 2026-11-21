// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// A content server, with the adapter's own handle for it. The host name is a server of Steam's
// content network and holds no personal data.
internal sealed class ContentServer
{
    public ContentServer(string host, object handle)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        ArgumentNullException.ThrowIfNull(handle);
        Host = host;
        Handle = handle;
    }

    public string Host { get; }

    public object Handle { get; }

    public override string ToString() => Host;
}
