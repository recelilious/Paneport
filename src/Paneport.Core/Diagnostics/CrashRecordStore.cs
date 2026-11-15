// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Storage;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// The crash record of the previous run: <c>crash-record.bin</c> in the crash record folder,
/// which the app reads at launch (C-0005 section 2). The record shows on Diagnostics until the
/// user dismisses it, and is then deleted. A malformed record is reported the same way and never
/// stops the app.
/// </summary>
/// <remarks>
/// A record that this run writes, such as the developer menu's test record, replaces the file but
/// not <see cref="Current"/>, which stays the launch's reading. Dismissing deletes the file only
/// while it still holds the bytes read at launch, so a newer record waits for the next launch.
/// </remarks>
public sealed class CrashRecordStore
{
    /// <summary>The crash record's file name, which <c>pp_init</c> composes in its folder.</summary>
    public const string FileName = "crash-record.bin";

    private const string LogSource = nameof(CrashRecordStore);

    private readonly IFileSystem _files;
    private readonly ILog _log;
    private readonly string _path;
    private byte[]? _launchBytes;

    /// <summary>Creates the store for a crash record folder.</summary>
    /// <param name="folder">The crash record folder, which <c>pp_init</c> receives too.</param>
    /// <param name="files">The file system.</param>
    /// <param name="log">The log.</param>
    public CrashRecordStore(string folder, IFileSystem files, ILog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(log);
        _files = files;
        _log = log;
        _path = Path.Combine(folder, FileName);
    }

    /// <summary>Gets what the launch found, or <see cref="CrashRecordReading.None"/> once dismissed.</summary>
    public CrashRecordReading Current { get; private set; } = CrashRecordReading.None;

    /// <summary>Reads the previous run's record. The app calls it once, at launch.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the file holds.</returns>
    public async Task<CrashRecordReading> ReadAtLaunchAsync(CancellationToken cancellationToken)
    {
        if (!_files.FileExists(_path))
        {
            Current = CrashRecordReading.None;
            return Current;
        }

        try
        {
            _launchBytes = await _files.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Write(LogLevel.Warning, LogSource, $"Reading the crash record failed with {exception.GetType().Name}.");
            Current = CrashRecordReading.Malformed("The crash record file could not be read.");
            return Current;
        }

        Current = CrashRecordReader.Parse(_launchBytes);
        if (Current.Status == CrashRecordStatus.Malformed)
        {
            _log.Write(LogLevel.Warning, LogSource, $"The crash record is malformed: {Current.Problem}");
        }
        else if (Current.Record is { } record)
        {
            _log.Write(LogLevel.Warning, LogSource, $"The previous run left a crash record: {record.SignalName}, {record.ThreadRoleName}{(record.IsTest ? ", a test record" : string.Empty)}.");
        }

        return Current;
    }

    /// <summary>
    /// Dismisses the record: deletes the file while it still holds what the launch read, and
    /// shows no record from then on.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check of the file.</param>
    /// <returns>A task that completes when the record is dismissed.</returns>
    public async Task DismissAsync(CancellationToken cancellationToken)
    {
        if (Current.Status == CrashRecordStatus.None)
        {
            return;
        }

        try
        {
            if (_launchBytes is null)
            {
                // The launch could not read the file, so nothing tells a newer record apart;
                // without the deletion an unreadable file would show at every launch.
                _files.DeleteFile(_path);
            }
            else if (_files.FileExists(_path))
            {
                var bytes = await _files.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
                if (bytes.AsSpan().SequenceEqual(_launchBytes))
                {
                    _files.DeleteFile(_path);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _log.Write(LogLevel.Warning, LogSource, $"Deleting the crash record failed with {exception.GetType().Name}.");
        }

        _launchBytes = null;
        Current = CrashRecordReading.None;
    }
}
