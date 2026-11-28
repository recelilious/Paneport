// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Storage;

namespace Paneport.Core.Prefixes;

/// <summary>
/// The prefix installer of docs/design/runtime-ios.md section 7. A first install copies the template
/// into a staging folder next to the target, writes the marker last and renames the folder. An update
/// replaces only the folders that the template's marker names, file by file, and rewrites the
/// installed marker last. A prefix without a valid marker is never touched.
/// </summary>
/// <remarks>
/// The log names paths relative to the app container only. A failure is logged and reported with the
/// exception's type, because a message can hold a local path.
/// </remarks>
public sealed class PrefixInstaller : IPrefixInstaller, IDisposable
{
    private const string LogSource = nameof(PrefixInstaller);
    private const string StagingPrefix = ".staging-";
    private const string TemporarySuffix = ".paneport-tmp";

    private readonly string _containerRoot;
    private readonly string _templateFolder;
    private readonly IPrefixFiles _files;
    private readonly IBackupMarks _marks;
    private readonly ILog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the installer for an app container.</summary>
    /// <param name="containerRoot">The app container's root folder.</param>
    /// <param name="templateFolder">The template's folder, <c>wine/prefix-template/</c> in the bundle. It may not exist.</param>
    /// <param name="files">The file operations.</param>
    /// <param name="marks">The backup marks, renewed on the prefixes folder after files in it changed.</param>
    /// <param name="log">The log.</param>
    public PrefixInstaller(string containerRoot, string templateFolder, IPrefixFiles files, IBackupMarks marks, ILog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateFolder);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(log);
        _containerRoot = containerRoot;
        _templateFolder = templateFolder;
        _files = files;
        _marks = marks;
        _log = log;
    }

    private string PrefixesPath => StorageLayout.PathOf(_containerRoot, StorageLayout.Prefixes);

    /// <inheritdoc />
    public string PathOf(string prefixName)
    {
        CheckName(prefixName);
        return Path.Combine(PrefixesPath, prefixName);
    }

    /// <inheritdoc />
    public async Task<PrefixInstallResult> InstallAsync(string prefixName, CancellationToken cancellationToken)
    {
        CheckName(prefixName);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Run(prefixName, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the lock that serializes the runs.</summary>
    public void Dispose() => _gate.Dispose();

    private static void CheckName(string prefixName)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefixName);
        if (!prefixName.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            throw new ArgumentException("A prefix name holds ASCII letters, digits and hyphens only.", nameof(prefixName));
        }
    }

    private static string Resolve(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private PrefixInstallResult Run(string prefixName, CancellationToken cancellationToken)
    {
        var shown = $"{StorageLayout.Prefixes.RelativePath}/{prefixName}";
        var markerPath = Path.Combine(_templateFolder, TemplateMarker.FileName);
        if (!_files.FileExists(markerPath))
        {
            _log.Write(LogLevel.Information, LogSource, "The app bundle holds no prefix template; no prefix is installed.");
            return new PrefixInstallResult(PrefixInstallOutcome.NoTemplate, shown, null, null);
        }

        try
        {
            var templateBytes = _files.ReadAllBytes(markerPath);
            if (!TemplateMarker.TryParse(templateBytes, out var template, out var problem))
            {
                _log.Write(LogLevel.Error, LogSource, $"The bundled template's marker is refused: {problem}.");
                return new PrefixInstallResult(PrefixInstallOutcome.Failed, shown, null, null, "template marker invalid");
            }

            var target = PathOf(prefixName);
            _files.CreateDirectory(PrefixesPath);
            var result = _files.DirectoryExists(target)
                ? Existing(shown, target, template!, templateBytes, cancellationToken)
                : FirstInstall(prefixName, shown, target, template!, templateBytes, cancellationToken);
            if (result.Outcome is PrefixInstallOutcome.Installed or PrefixInstallOutcome.Updated)
            {
                MarkPrefixesFolder();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _log.Write(LogLevel.Warning, LogSource, $"Installing {shown} was cancelled; the next run repeats it.");
            throw;
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Error, LogSource, $"Installing {shown} failed with {exception.GetType().Name}; the next run repeats it.");
            return new PrefixInstallResult(PrefixInstallOutcome.Failed, shown, null, null, exception.GetType().Name);
        }
    }

    private PrefixInstallResult FirstInstall(
        string prefixName,
        string shown,
        string target,
        TemplateMarker template,
        byte[] templateBytes,
        CancellationToken cancellationToken)
    {
        // A staging folder that is still there comes from a copy that was interrupted, whatever it
        // holds: the copy starts again from nothing.
        var staging = Path.Combine(PrefixesPath, StagingPrefix + prefixName);
        if (_files.DirectoryExists(staging))
        {
            _log.Write(LogLevel.Information, LogSource, $"Removing the staging folder of an interrupted copy of {shown}.");
            _files.DeleteDirectoryTree(staging);
        }

        _files.CreateDirectory(staging);
        foreach (var entry in _files.List(_templateFolder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Path == TemplateMarker.FileName)
            {
                continue;
            }

            CopyEntry(entry, Resolve(staging, entry.Path));
        }

        // The marker goes in last, then the folder gets its name in one step: a prefix under its
        // name is always whole.
        _files.WriteAllBytes(Path.Combine(staging, TemplateMarker.FileName), templateBytes);
        _files.MoveDirectory(staging, target);
        _log.Write(LogLevel.Information, LogSource, $"Installed {shown} from template version {template.Version}.");
        return new PrefixInstallResult(PrefixInstallOutcome.Installed, shown, template.Version, template.Version);
    }

    private PrefixInstallResult Existing(
        string shown,
        string target,
        TemplateMarker template,
        byte[] templateBytes,
        CancellationToken cancellationToken)
    {
        // A staging folder beside a finished prefix is a leftover; it only costs space.
        var staging = Path.Combine(PrefixesPath, StagingPrefix + Path.GetFileName(target));
        _files.DeleteDirectoryTree(staging);

        var installedMarkerPath = Path.Combine(target, TemplateMarker.FileName);
        TemplateMarker? installed = null;
        string? problem = "the prefix has no marker";
        if (_files.FileExists(installedMarkerPath)
            && TemplateMarker.TryParse(_files.ReadAllBytes(installedMarkerPath), out var parsed, out problem))
        {
            installed = parsed;
        }

        if (installed is null)
        {
            _log.Write(
                LogLevel.Warning,
                LogSource,
                $"{shown} was not made by the installer ({problem}); it is left as it is.");
            return new PrefixInstallResult(PrefixInstallOutcome.Unmanaged, shown, template.Version, null);
        }

        if (installed.Version == template.Version)
        {
            return new PrefixInstallResult(PrefixInstallOutcome.AlreadyCurrent, shown, template.Version, installed.Version);
        }

        if (installed.Version > template.Version)
        {
            _log.Write(
                LogLevel.Warning,
                LogSource,
                $"{shown} comes from template version {installed.Version}, newer than the bundled version {template.Version}; it is left as it is.");
            return new PrefixInstallResult(PrefixInstallOutcome.TemplateOlder, shown, template.Version, installed.Version);
        }

        Update(target, template, templateBytes, cancellationToken);
        _log.Write(
            LogLevel.Information,
            LogSource,
            $"Updated {shown} from template version {installed.Version} to {template.Version}.");
        return new PrefixInstallResult(PrefixInstallOutcome.Updated, shown, template.Version, template.Version);
    }

    // Replaces what lies under the marker's folders, then rewrites the installed marker. The marker
    // is the last step, so an update that stops earlier still shows the old version and repeats.
    private void Update(string target, TemplateMarker template, byte[] templateBytes, CancellationToken cancellationToken)
    {
        bool inScope(string path) => template.ReplaceOnUpdate.Any(folder => (path + "/").StartsWith(folder, StringComparison.Ordinal));
        var templateEntries = _files.List(_templateFolder).Where(entry => inScope(entry.Path)).ToList();

        foreach (var entry in templateEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Resolve(target, entry.Path);
            if (entry.Kind == PrefixEntryKind.File)
            {
                var temporary = destination + TemporarySuffix;
                _files.CopyFile(Resolve(_templateFolder, entry.Path), temporary);
                _files.ReplaceFile(temporary, destination);
            }
            else
            {
                CopyEntry(entry, destination);
            }
        }

        // What the template no longer has under those folders goes: dropped files, and the
        // temporary files of an interrupted update. A folder goes once it is empty.
        var kept = templateEntries.Select(entry => entry.Path).ToHashSet(StringComparer.Ordinal);
        var stale = _files.List(target).Where(entry => inScope(entry.Path) && !kept.Contains(entry.Path)).ToList();
        foreach (var entry in stale.Where(entry => entry.Kind != PrefixEntryKind.Directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.DeleteFile(Resolve(target, entry.Path));
        }

        foreach (var entry in stale.Where(entry => entry.Kind == PrefixEntryKind.Directory).OrderByDescending(entry => entry.Path, StringComparer.Ordinal))
        {
            _files.DeleteDirectoryIfEmpty(Resolve(target, entry.Path));
        }

        var installedMarker = Path.Combine(target, TemplateMarker.FileName);
        _files.WriteAllBytes(installedMarker + TemporarySuffix, templateBytes);
        _files.ReplaceFile(installedMarker + TemporarySuffix, installedMarker);
    }

    // Creates a folder or a link, or copies a file straight to its place (a first install's
    // staging folder is not visible under the prefix's name, so it needs no temporary file).
    private void CopyEntry(PrefixEntry entry, string destination)
    {
        switch (entry.Kind)
        {
            case PrefixEntryKind.Directory:
                _files.CreateDirectory(destination);
                break;
            case PrefixEntryKind.Symlink:
                _files.DeleteFile(destination);
                _files.CreateSymbolicLink(destination, entry.LinkTarget!);
                break;
            default:
                _files.CopyFile(Resolve(_templateFolder, entry.Path), destination);
                break;
        }
    }

    // C-0004 section 4: the mark is set again after files in the folder were replaced. iOS treats
    // it as guidance, so a failure is logged and the run still counts.
    private void MarkPrefixesFolder()
    {
        try
        {
            _marks.SetExcluded(PrefixesPath);
        }
        catch (Exception exception)
        {
            _log.Write(
                LogLevel.Error,
                LogSource,
                $"Marking {StorageLayout.Prefixes.RelativePath} failed with {exception.GetType().Name}.");
        }
    }
}
