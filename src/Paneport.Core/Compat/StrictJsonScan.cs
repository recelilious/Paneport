// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Paneport.Core.Compat;

// A pass over a title file before the serializer reads it. It finds what the serializer would let
// through or report only in its own words: null, which no field takes; a field that appears twice;
// a field the schema does not define; and an entry of translator or environment that is not a
// string. It also reads the top-level schemaVersion, so that a file of a newer schema is reported
// as such and not by the fields it adds. Comments and trailing commas are not JSON and fail.
internal static class StrictJsonScan
{
    // The objects whose field names the scan knows. Free is anything else, whose type the
    // serializer then rejects.
    private enum Shape
    {
        Free,
        Root,
        Id,
        Executable,
        Drm,
        Graphics,
        HelperPolicy,
        Report,
        Map,
        Reports,
    }

    // The field names come from the generated metadata of the document classes, so that the scan
    // and the serializer never disagree on them.
    private static readonly FrozenDictionary<Shape, FrozenSet<string>> s_knownNames =
        new Dictionary<Shape, FrozenSet<string>>
        {
            [Shape.Root] = NamesOf(CompatJsonContext.Default.TitleDocument),
            [Shape.Id] = NamesOf(CompatJsonContext.Default.IdDocument),
            [Shape.Executable] = NamesOf(CompatJsonContext.Default.ExecutableDocument),
            [Shape.Drm] = NamesOf(CompatJsonContext.Default.DrmDocument),
            [Shape.Graphics] = NamesOf(CompatJsonContext.Default.GraphicsDocument),
            [Shape.HelperPolicy] = NamesOf(CompatJsonContext.Default.HelperPolicyDocument),
            [Shape.Report] = NamesOf(CompatJsonContext.Default.ReportDocument),
        }.ToFrozenDictionary();

    public static ScanResult Scan(ReadOnlySpan<byte> json)
    {
        var problems = new List<CompatProblem>();
        long? schemaVersion = null;
        var reader = new Utf8JsonReader(json, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return ScanResult.Failed(new CompatProblem(
                    CompatRule.Json, CompatPath.Root, "The file must hold one JSON object."));
            }

            var frames = new List<Frame> { new(Shape.Root, CompatPath.Root, isArray: false) };
            while (reader.Read())
            {
                var frame = frames[^1];
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        ReadName(ref reader, frame, problems);
                        continue;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        frames.RemoveAt(frames.Count - 1);
                        continue;
                    default:
                        break;
                }

                // Any other token starts a value: of the field just named, or the next item of an array.
                string path;
                Shape shape;
                if (frame.IsArray)
                {
                    path = CompatPath.Index(frame.Path, frame.NextIndex++);
                    shape = frame.Shape == Shape.Reports ? Shape.Report : Shape.Free;
                }
                else
                {
                    path = frame.ValuePath;
                    shape = ShapeOf(frame.Shape, frame.ValueName);
                }

                if (frame.Shape == Shape.Map && reader.TokenType != JsonTokenType.String)
                {
                    problems.Add(new CompatProblem(CompatRule.Type, path, "An entry of this object must be a string."));
                    shape = Shape.Free;
                }
                else if (reader.TokenType == JsonTokenType.Null)
                {
                    problems.Add(new CompatProblem(CompatRule.Type, path, "No field takes null."));
                }

                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        frames.Add(new Frame(shape == Shape.Reports ? Shape.Free : shape, path, isArray: false));
                        break;
                    case JsonTokenType.StartArray:
                        frames.Add(new Frame(shape == Shape.Reports ? Shape.Reports : Shape.Free, path, isArray: true));
                        break;
                    case JsonTokenType.Number when frame.Shape == Shape.Root && frame.ValueName == "schemaVersion":
                        schemaVersion = reader.TryGetInt64(out var version) ? version : null;
                        break;
                    default:
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // The reader's message holds no value, but the problem uses words of its own, as all do.
            return ScanResult.Failed(
                new CompatProblem(CompatRule.Json, CompatPath.Root, "The file is not valid JSON."));
        }

        return new ScanResult(null, schemaVersion, problems);
    }

    private static void ReadName(ref Utf8JsonReader reader, Frame frame, List<CompatProblem> problems)
    {
        var name = reader.GetString()!;
        frame.ValueName = name;
        frame.ValuePath = CompatPath.Property(frame.Path, name);
        if (!frame.Names.Add(name))
        {
            problems.Add(new CompatProblem(
                CompatRule.DuplicateField, frame.ValuePath, "The field appears twice in one object."));
        }
        else if (s_knownNames.TryGetValue(frame.Shape, out var known) && !known.Contains(name))
        {
            problems.Add(new CompatProblem(
                CompatRule.UnknownField, frame.ValuePath, "The title schema defines no such field here."));
        }
    }

    // The shape of the value of a field: the objects of the schema, the two maps whose keys the
    // allow-lists check later, and the reports.
    private static Shape ShapeOf(Shape parent, string name) => parent != Shape.Root ? Shape.Free : name switch
    {
        "id" => Shape.Id,
        "executable" => Shape.Executable,
        "drm" => Shape.Drm,
        "graphics" => Shape.Graphics,
        "helperPolicy" => Shape.HelperPolicy,
        "translator" or "environment" => Shape.Map,
        "reports" => Shape.Reports,
        _ => Shape.Free,
    };

    private static FrozenSet<string> NamesOf(JsonTypeInfo typeInfo) =>
        typeInfo.Properties.Select(property => property.Name).ToFrozenSet(StringComparer.Ordinal);

    internal readonly record struct ScanResult(
        CompatProblem? Fatal, long? SchemaVersion, IReadOnlyList<CompatProblem> Problems)
    {
        public static ScanResult Failed(CompatProblem problem) => new(problem, null, []);
    }

    private sealed class Frame(Shape shape, string path, bool isArray)
    {
        public Shape Shape { get; } = shape;

        public string Path { get; } = path;

        public bool IsArray { get; } = isArray;

        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public string ValueName { get; set; } = string.Empty;

        public string ValuePath { get; set; } = path;

        public int NextIndex { get; set; }
    }
}
