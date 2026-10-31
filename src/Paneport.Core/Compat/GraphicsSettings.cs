// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// The <c>graphics</c> object of a title entry (C-0008 section 2). A null member is not set.
/// </summary>
/// <remarks>
/// Each member is a setting of its own, so a layer replaces single members: a user's render
/// scale leaves the reviewed frame-rate cap in place.
/// </remarks>
public sealed record GraphicsSettings
{
    /// <summary>
    /// Gets the internal resolution as a fraction of the output, greater than 0 and at most 1.
    /// </summary>
    public double? RenderScale { get; init; }

    /// <summary>Gets how MetalFX upscales the internal resolution.</summary>
    public MetalFxMode? MetalFx { get; init; }

    /// <summary>
    /// Gets the frame-rate cap. It must divide the display's refresh rate, which only the running
    /// session knows, so the session checks that.
    /// </summary>
    public int? FrameRateCap { get; init; }
}
