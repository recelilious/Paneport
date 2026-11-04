// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Compat;

/// <summary>
/// A graphics API that a title uses, an item of <c>graphicsApi</c> (C-0008 section 2).
/// </summary>
public enum GraphicsApi
{
    /// <summary>GDI, written <c>gdi</c>.</summary>
    Gdi,

    /// <summary>DirectDraw, written <c>ddraw</c>.</summary>
    DirectDraw,

    /// <summary>Direct3D 8, written <c>d3d8</c>.</summary>
    Direct3D8,

    /// <summary>Direct3D 9, written <c>d3d9</c>.</summary>
    Direct3D9,

    /// <summary>Direct3D 10, written <c>d3d10</c>.</summary>
    Direct3D10,

    /// <summary>Direct3D 11, written <c>d3d11</c>.</summary>
    Direct3D11,

    /// <summary>Direct3D 12, written <c>d3d12</c>.</summary>
    Direct3D12,

    /// <summary>OpenGL, written <c>opengl</c>.</summary>
    OpenGL,

    /// <summary>Vulkan, written <c>vulkan</c>.</summary>
    Vulkan,
}
