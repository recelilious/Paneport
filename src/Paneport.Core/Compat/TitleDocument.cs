// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace Paneport.Core.Compat;

// The JSON shape of a title file, which the source generator reads and writes (C-0010 section 2).
// Names are the schema's, written out so that a rename shows in review. Enumerations stay strings
// and integers stay 64-bit, so that TitleEntryJson checks them against exact names and ranges
// itself: the serializer's own enum handling ignores letter case and takes numbers. Every field
// may be missing here; TitleEntryJson reports which ones a file needs.
internal sealed class TitleDocument
{
    [JsonPropertyName("schemaVersion")]
    public long? SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public IdDocument? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("executable")]
    public ExecutableDocument? Executable { get; set; }

    [JsonPropertyName("architecture")]
    public string? Architecture { get; set; }

    [JsonPropertyName("drm")]
    public DrmDocument? Drm { get; set; }

    [JsonPropertyName("launch")]
    public string? Launch { get; set; }

    [JsonPropertyName("graphicsApi")]
    public List<string>? GraphicsApi { get; set; }

    [JsonPropertyName("runtimes")]
    public List<string>? Runtimes { get; set; }

    [JsonPropertyName("memoryEstimateMiB")]
    public long? MemoryEstimateMiB { get; set; }

    [JsonPropertyName("jitPoolMiB")]
    public long? JitPoolMiB { get; set; }

    [JsonPropertyName("translator")]
    public Dictionary<string, string>? Translator { get; set; }

    [JsonPropertyName("graphics")]
    public GraphicsDocument? Graphics { get; set; }

    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }

    [JsonPropertyName("helperPolicy")]
    public HelperPolicyDocument? HelperPolicy { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }

    [JsonPropertyName("reports")]
    public List<ReportDocument>? Reports { get; set; }

    internal sealed class IdDocument
    {
        [JsonPropertyName("steamAppId")]
        public long? SteamAppId { get; set; }

        [JsonPropertyName("exeSha256")]
        public List<string>? ExeSha256 { get; set; }

        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }

    internal sealed class ExecutableDocument
    {
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("workingDirectory")]
        public string? WorkingDirectory { get; set; }

        [JsonPropertyName("arguments")]
        public List<string>? Arguments { get; set; }
    }

    internal sealed class DrmDocument
    {
        [JsonPropertyName("steamApi")]
        public bool? SteamApi { get; set; }

        [JsonPropertyName("steamStub")]
        public bool? SteamStub { get; set; }

        [JsonPropertyName("steamAppIdFile")]
        public bool? SteamAppIdFile { get; set; }

        [JsonPropertyName("ceg")]
        public string? Ceg { get; set; }
    }

    internal sealed class GraphicsDocument
    {
        [JsonPropertyName("renderScale")]
        public double? RenderScale { get; set; }

        [JsonPropertyName("metalFx")]
        public string? MetalFx { get; set; }

        [JsonPropertyName("frameRateCap")]
        public long? FrameRateCap { get; set; }
    }

    internal sealed class HelperPolicyDocument
    {
        [JsonPropertyName("blockedExecutables")]
        public List<string>? BlockedExecutables { get; set; }
    }

    internal sealed class ReportDocument
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("device")]
        public string? Device { get; set; }

        [JsonPropertyName("iosVersion")]
        public string? IosVersion { get; set; }

        [JsonPropertyName("paneportVersion")]
        public string? PaneportVersion { get; set; }

        [JsonPropertyName("gameBuild")]
        public string? GameBuild { get; set; }

        [JsonPropertyName("rating")]
        public string? Rating { get; set; }

        [JsonPropertyName("evidence")]
        public string? Evidence { get; set; }

        [JsonPropertyName("fpsMedian")]
        public double? FpsMedian { get; set; }

        [JsonPropertyName("fpsLow1")]
        public double? FpsLow1 { get; set; }

        [JsonPropertyName("peakMemoryMiB")]
        public long? PeakMemoryMiB { get; set; }

        [JsonPropertyName("thermalState")]
        public string? ThermalState { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }
}
