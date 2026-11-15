// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Paneport.Core.Logging;

/// <summary>
/// The log of the device: one file per run, <c>paneport.log</c>, in the logs folder below
/// <c>Library/Caches/Paneport/</c>, which iOS may purge (C-0004 section 4). The previous run's
/// file is kept once, as <c>paneport.previous.log</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Write"/> returns at once from any thread: a line goes into a queue, and one
/// background task appends it to the file, flushing whenever the queue runs empty. Each line is
/// <c>time level source: message</c>, with the time in UTC.
/// </para>
/// <para>
/// A run's file stops growing at <see cref="MaxBytes"/>, with one last line that says so. The log
/// holds no password, token or other secret (C-0010 section 2); exporting it, which needs the
/// scrubbing of C-0004 section 4, comes with a later stage.
/// </para>
/// </remarks>
public sealed class FileLog : ILog, IDisposable
{
    /// <summary>The name of the current run's file.</summary>
    public const string FileName = "paneport.log";

    /// <summary>The name of the previous run's file.</summary>
    public const string PreviousFileName = "paneport.previous.log";

    /// <summary>The size at which a run's file stops growing.</summary>
    public const long MaxBytes = 4 * 1024 * 1024;

    private const string LimitLine = "The log reached its size limit; later lines are dropped.";

    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TimeProvider _time;
    private readonly Task _writer;

    /// <summary>Starts the log in a folder, which it creates if needed, and keeps the previous run's file once.</summary>
    /// <param name="folder">The logs folder.</param>
    /// <param name="time">The clock that stamps the lines.</param>
    /// <exception cref="IOException">The folder or the file cannot be opened. The message can hold a local path.</exception>
    public FileLog(string folder, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        Directory.CreateDirectory(folder);
        Path = System.IO.Path.Combine(folder, FileName);
        if (File.Exists(Path))
        {
            File.Move(Path, System.IO.Path.Combine(folder, PreviousFileName), overwrite: true);
        }

        var stream = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _writer = Task.Run(() => WriteLinesAsync(stream));
    }

    /// <summary>Gets the path of the current run's file.</summary>
    public string Path { get; }

    /// <inheritdoc />
    public void Write(LogLevel level, string source, string message)
    {
        var time = _time.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        _lines.Writer.TryWrite($"{time} {level} {source}: {message}");
    }

    /// <summary>Writes the lines in the queue, and closes the file.</summary>
    public void Dispose()
    {
        _lines.Writer.TryComplete();
        _writer.Wait(TimeSpan.FromSeconds(5));
    }

    private async Task WriteLinesAsync(FileStream stream)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var writer = new StreamWriter(stream, encoding) { NewLine = "\n" };
        await using var writerScope = writer.ConfigureAwait(false);
        long written = 0;
        var limitReached = false;
        await foreach (var line in _lines.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (limitReached)
            {
                continue;
            }

            var size = encoding.GetByteCount(line) + 1;
            if (written + size > MaxBytes)
            {
                limitReached = true;
                await writer.WriteLineAsync(LimitLine).ConfigureAwait(false);
            }
            else
            {
                written += size;
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }

            if (_lines.Reader.Count == 0)
            {
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
    }
}
