// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The compatibility database (C-0008): the bundled snapshot and the device layer, merged under the
/// precedence rules of C-0008 section 1.
/// </summary>
/// <remarks>Each app head creates it over its own folders.</remarks>
public sealed class CompatDatabase
{
    /// <summary>Creates the database over its two layers.</summary>
    /// <param name="bundled">The bundled snapshot.</param>
    /// <param name="device">The device layer, which the caller owns and disposes.</param>
    public CompatDatabase(BundledSnapshot bundled, DeviceLayerStore device)
    {
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(device);
        Bundled = bundled;
        Device = device;
    }

    /// <summary>Gets the bundled snapshot.</summary>
    public BundledSnapshot Bundled { get; }

    /// <summary>Gets the device layer, which also saves entries.</summary>
    public DeviceLayerStore Device { get; }

    /// <summary>Reads both layers of a title and merges them.</summary>
    /// <param name="id">The title's identity.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// Both files and the effective settings. A title with no entry runs with the schema defaults and
    /// shows as untested (C-0008 section 1).
    /// </returns>
    public async Task<CompatTitle> LoadAsync(TitleId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        var bundledLoad = Bundled.LoadAsync(id, cancellationToken);
        var deviceLoad = Device.LoadAsync(id, cancellationToken);
        await Task.WhenAll(bundledLoad, deviceLoad).ConfigureAwait(false);
        var bundled = await bundledLoad.ConfigureAwait(false);
        var device = await deviceLoad.ConfigureAwait(false);
        return new CompatTitle(bundled, device, CompatPrecedence.Merge(id, bundled.Entry, device.Entry));
    }
}
