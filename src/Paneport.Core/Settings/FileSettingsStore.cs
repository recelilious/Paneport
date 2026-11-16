// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Paneport.Core.Logging;
using Paneport.Core.Storage;

namespace Paneport.Core.Settings;

/// <summary>
/// Keeps the app settings in <c>settings.json</c> in the settings folder of C-0004 section 4,
/// written with System.Text.Json source generation (C-0010 section 2).
/// </summary>
/// <remarks>
/// <para>
/// A save writes a temporary file in the same folder and then moves it over the settings file in
/// one step, so a save that fails or is interrupted leaves the previous file.
/// </para>
/// <para>
/// The store runs one operation at a time, in the order they were asked for: a load waits for a
/// pending save, and a save that is cancelled before its file replaces the old one stops without
/// replacing it (docs/design/app-shell.md section 10). A file that cannot be read gives the
/// defaults and a log line.
/// </para>
/// </remarks>
public sealed class FileSettingsStore : ISettingsStore, IDisposable
{
    /// <summary>The settings file's name.</summary>
    public const string FileName = "settings.json";

    private const string TemporaryName = FileName + ".tmp";
    private const string LogSource = nameof(FileSettingsStore);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IFileSystem _files;
    private readonly ILog _log;
    private readonly string _path;
    private readonly string _temporaryPath;

    /// <summary>Opens the store.</summary>
    /// <param name="folder">The settings folder, which exists.</param>
    /// <param name="files">The file system.</param>
    /// <param name="log">The log.</param>
    public FileSettingsStore(string folder, IFileSystem files, ILog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(log);
        _files = files;
        _log = log;
        _path = Path.Combine(folder, FileName);
        _temporaryPath = Path.Combine(folder, TemporaryName);
    }

    /// <inheritdoc />
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_files.FileExists(_path))
            {
                return AppSettings.Default;
            }

            byte[] bytes;
            try
            {
                bytes = await _files.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _log.Write(LogLevel.Warning, LogSource, $"Reading the settings file failed with {exception.GetType().Name}; the defaults apply.");
                return AppSettings.Default;
            }

            if (Parse(bytes) is { } settings)
            {
                return settings;
            }

            _log.Write(LogLevel.Warning, LogSource, "The settings file could not be read; the defaults apply.");
            return AppSettings.Default;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <exception cref="OperationCanceledException">The save was cancelled before the file was replaced.</exception>
    /// <exception cref="IOException">The file could not be written. Its message can hold a local path.</exception>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var document = new SettingsDocument { Version = SettingsDocument.CurrentVersion, Locale = settings.Locale };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SettingsJsonContext.Default.SettingsDocument);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await _files.WriteAllBytesAsync(_temporaryPath, bytes, cancellationToken).ConfigureAwait(false);

                // The last point at which a newer change can still stop this save.
                cancellationToken.ThrowIfCancellationRequested();
                _files.ReplaceFile(_temporaryPath, _path);
            }
            catch
            {
                RemoveTemporaryFile();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    // The settings in a file's bytes, or null when they are not a settings file of a known version
    // with valid values.
    private static AppSettings? Parse(byte[] bytes)
    {
        SettingsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(bytes, SettingsJsonContext.Default.SettingsDocument);
        }
        catch (JsonException)
        {
            return null;
        }

        if (document is not { Version: SettingsDocument.CurrentVersion } || string.IsNullOrWhiteSpace(document.Locale))
        {
            return null;
        }

        return new AppSettings { Locale = document.Locale };
    }

    private void RemoveTemporaryFile()
    {
        try
        {
            _files.DeleteFile(_temporaryPath);
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Warning, LogSource, $"Removing the temporary settings file failed with {exception.GetType().Name}.");
        }
    }
}
