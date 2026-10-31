// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Paneport.Core.Library;

namespace Paneport.Core.Compat;

// The names of compat/schema/title.schema.json for each enumeration of the model, in the schema's
// order. A test reads the schema and checks that both list the same names.
internal static class CompatNames
{
    public static JsonNames<TitleArchitecture> Architecture { get; } = new(
        (TitleArchitecture.X64, "x86_64"),
        (TitleArchitecture.X86, "x86"));

    public static JsonNames<CegStatus> Ceg { get; } = new(
        (CegStatus.Yes, "yes"),
        (CegStatus.No, "no"),
        (CegStatus.Unknown, "unknown"));

    public static JsonNames<LaunchPath> Launch { get; } = new(
        (LaunchPath.Direct, "direct"),
        (LaunchPath.Dock, "dock"));

    public static JsonNames<GraphicsApi> GraphicsApi { get; } = new(
        (Compat.GraphicsApi.Gdi, "gdi"),
        (Compat.GraphicsApi.DirectDraw, "ddraw"),
        (Compat.GraphicsApi.Direct3D8, "d3d8"),
        (Compat.GraphicsApi.Direct3D9, "d3d9"),
        (Compat.GraphicsApi.Direct3D10, "d3d10"),
        (Compat.GraphicsApi.Direct3D11, "d3d11"),
        (Compat.GraphicsApi.Direct3D12, "d3d12"),
        (Compat.GraphicsApi.OpenGL, "opengl"),
        (Compat.GraphicsApi.Vulkan, "vulkan"));

    public static JsonNames<MetalFxMode> MetalFx { get; } = new(
        (MetalFxMode.Off, "off"),
        (MetalFxMode.Spatial, "spatial"));

    public static JsonNames<PrefixMode> Prefix { get; } = new(
        (PrefixMode.Shared, "shared"),
        (PrefixMode.Isolated, "isolated"));

    public static JsonNames<CompatRating> Rating { get; } = new(
        (CompatRating.Perfect, "perfect"),
        (CompatRating.Playable, "playable"),
        (CompatRating.Runs, "runs"),
        (CompatRating.Boots, "boots"),
        (CompatRating.Broken, "broken"),
        (CompatRating.Untested, "untested"));

    public static JsonNames<ThermalState> ThermalState { get; } = new(
        (Hosting.ThermalState.Nominal, "nominal"),
        (Hosting.ThermalState.Fair, "fair"),
        (Hosting.ThermalState.Serious, "serious"),
        (Hosting.ThermalState.Critical, "critical"));
}
