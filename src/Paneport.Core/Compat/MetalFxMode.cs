// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// How MetalFX upscales the internal resolution, the <c>graphics.metalFx</c> field
/// (C-0008 section 2).
/// </summary>
/// <remarks>The default value, <see cref="Off"/>, is also the schema default.</remarks>
public enum MetalFxMode
{
    /// <summary>No MetalFX upscaling, written <c>off</c>.</summary>
    Off,

    /// <summary>MetalFX spatial upscaling, written <c>spatial</c>.</summary>
    Spatial,
}
