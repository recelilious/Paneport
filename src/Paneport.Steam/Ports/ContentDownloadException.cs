// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Net;

namespace Paneport.Steam.Ports;

// A download from a content server that failed at the server or on the way, with the HTTP status
// when the server answered. Its message is never shown or logged: SteamKit2's can hold a URL with
// a server token in it.
internal sealed class ContentDownloadException : Exception
{
    public ContentDownloadException()
        : base("The download from the content server failed.")
    {
    }

    public ContentDownloadException(string message)
        : base(message)
    {
    }

    public ContentDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ContentDownloadException(HttpStatusCode? status, Exception? innerException)
        : base("The download from the content server failed.", innerException)
    {
        Status = status;
    }

    public HttpStatusCode? Status { get; }
}
