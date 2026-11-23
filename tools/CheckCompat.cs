#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The compatibility check of C-0011 section 1, so far for the list of verified iOS builds:
// compat/ios-builds.json must match compat/schema/ios-builds.schema.json (C-0006 section 6),
// each build and device model may appear only once, and every evidence record that an entry
// names must exist. The validator is our own and implements only the JSON Schema keywords
// (draft 2020-12) that the schema uses; a schema with any other keyword is refused, so that no
// keyword goes unchecked. The title files of compat/titles/ join with M8.3, whose validator
// waits for decision D8 (C-0011 section 5).
//
//   dotnet run tools/CheckCompat.cs                 check the repository
//   dotnet run tools/CheckCompat.cs -- --self-test  check the validator and the rules on fixtures
//
// A problem names the file, the JSON pointer and the rule, never the value. Exit codes: 0 the
// data is valid, 1 it is not, 2 usage or configuration error. docs/design/repository-checks.md
// describes the check.

#:property PublishAot=false

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return args switch
    {
        [] => CompatCheck.Run(),
        ["--self-test"] => SelfTest.Run(),
        _ => Cli.Usage(),
    };
}
catch (Exception exception) when (
    exception is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

internal static class Cli
{
    public static int Usage()
    {
        Console.Error.WriteLine("usage: dotnet run tools/CheckCompat.cs [-- --self-test]");
        return 2;
    }
}

// One problem: the file, the JSON pointer of the value, the rule it breaks, and what the rule
// wants. The value itself is never part of it.
internal sealed record Problem(string File, string Pointer, string Rule, string Detail)
{
    public override string ToString() => $"{File}: {(Pointer.Length == 0 ? "/" : Pointer)}: {Rule}: {Detail}";
}

// The validator of the keywords that the schema uses. s_annotations are read and ignored.
internal sealed class SchemaValidator
{
    private static readonly HashSet<string> s_annotations = ["$schema", "$id", "title", "description", "$comment"];

    private static readonly HashSet<string> s_keywords =
    [
        "type", "required", "properties", "additionalProperties", "const", "enum", "items", "$ref", "$defs",
        "pattern", "minItems", "uniqueItems", "minLength",
    ];

    private readonly JsonElement _root;
    private readonly Dictionary<string, Regex> _patterns = [];

    // Reads the schema, and refuses it when it uses a keyword that this validator does not
    // implement, anywhere in it.
    public SchemaValidator(JsonElement root, string name)
    {
        _root = root;
        CheckKeywords(root, "", name);
    }

    private static void CheckKeywords(JsonElement schema, string pointer, string name)
    {
        if (schema.ValueKind == JsonValueKind.True || schema.ValueKind == JsonValueKind.False)
        {
            return;
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{name}: {pointer}: a schema must be an object or a boolean.");
        }
        foreach (var keyword in schema.EnumerateObject())
        {
            if (s_annotations.Contains(keyword.Name))
            {
                continue;
            }
            if (!s_keywords.Contains(keyword.Name))
            {
                throw new InvalidDataException(
                    $"{name}: {pointer}/{keyword.Name}: tools/CheckCompat.cs does not implement this keyword.");
            }
            var at = $"{pointer}/{keyword.Name}";
            switch (keyword.Name)
            {
                case "properties" or "$defs":
                    foreach (var child in keyword.Value.EnumerateObject())
                    {
                        CheckKeywords(child.Value, $"{at}/{child.Name}", name);
                    }
                    break;
                case "items" or "additionalProperties":
                    CheckKeywords(keyword.Value, at, name);
                    break;
                case "$ref" when !(keyword.Value.GetString() ?? "").StartsWith("#/$defs/", StringComparison.Ordinal):
                    throw new InvalidDataException($"{name}: {at}: only references into $defs are implemented.");
            }
        }
    }

    public void Validate(JsonElement instance, string file, List<Problem> problems) =>
        Validate(_root, instance, "", file, problems);

    private void Validate(JsonElement schema, JsonElement instance, string pointer, string file, List<Problem> problems)
    {
        if (schema.ValueKind == JsonValueKind.True)
        {
            return;
        }
        if (schema.ValueKind == JsonValueKind.False)
        {
            problems.Add(new Problem(file, pointer, "false", "no value is allowed here"));
            return;
        }
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var target = _root.GetProperty("$defs").GetProperty(reference.GetString()!["#/$defs/".Length..]);
            Validate(target, instance, pointer, file, problems);
        }
        if (schema.TryGetProperty("type", out var type) && !HasType(instance, type.GetString()!))
        {
            problems.Add(new Problem(file, pointer, "type", $"the value must be of type {type.GetString()}"));
            return;
        }
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(instance, constant))
        {
            problems.Add(new Problem(file, pointer, "const", $"the value must be {constant.GetRawText()}"));
        }
        if (schema.TryGetProperty("enum", out var choices)
            && !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(instance, choice)))
        {
            problems.Add(new Problem(file, pointer, "enum", "the value is not one of those the schema lists"));
        }
        switch (instance.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(schema, instance, pointer, file, problems);
                break;
            case JsonValueKind.Array:
                ValidateArray(schema, instance, pointer, file, problems);
                break;
            case JsonValueKind.String:
                ValidateString(schema, instance.GetString()!, pointer, file, problems);
                break;
        }
    }

    private void ValidateObject(JsonElement schema, JsonElement instance, string pointer, string file, List<Problem> problems)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()!))
            {
                if (!instance.TryGetProperty(name, out _))
                {
                    problems.Add(new Problem(file, pointer, "required", $"the member {name} is missing"));
                }
            }
        }
        var properties = schema.TryGetProperty("properties", out var declared) ? declared : default;
        foreach (var member in instance.EnumerateObject())
        {
            var at = $"{pointer}/{Escape(member.Name)}";
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(member.Name, out var memberSchema))
            {
                Validate(memberSchema, member.Value, at, file, problems);
            }
            else if (schema.TryGetProperty("additionalProperties", out var additional))
            {
                if (additional.ValueKind == JsonValueKind.False)
                {
                    problems.Add(new Problem(file, at, "additionalProperties", "the schema has no such member"));
                }
                else
                {
                    Validate(additional, member.Value, at, file, problems);
                }
            }
        }
    }

    private void ValidateArray(JsonElement schema, JsonElement instance, string pointer, string file, List<Problem> problems)
    {
        var items = instance.EnumerateArray().ToList();
        if (schema.TryGetProperty("minItems", out var minimum) && items.Count < minimum.GetInt32())
        {
            problems.Add(new Problem(file, pointer, "minItems", $"the list needs at least {minimum.GetInt32()} items"));
        }
        if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
        {
            for (var index = 1; index < items.Count; index++)
            {
                if (items.Take(index).Any(earlier => JsonElement.DeepEquals(earlier, items[index])))
                {
                    problems.Add(new Problem(file, $"{pointer}/{index}", "uniqueItems", "the item repeats an earlier one"));
                }
            }
        }
        if (schema.TryGetProperty("items", out var itemSchema))
        {
            for (var index = 0; index < items.Count; index++)
            {
                Validate(itemSchema, items[index], $"{pointer}/{index}", file, problems);
            }
        }
    }

    private void ValidateString(JsonElement schema, string value, string pointer, string file, List<Problem> problems)
    {
        if (schema.TryGetProperty("minLength", out var minimum) && value.Length < minimum.GetInt32())
        {
            problems.Add(new Problem(file, pointer, "minLength", $"the text needs at least {minimum.GetInt32()} characters"));
        }
        if (schema.TryGetProperty("pattern", out var pattern) && !Pattern(pattern.GetString()!).IsMatch(value))
        {
            problems.Add(new Problem(file, pointer, "pattern", $"the text must match {pattern.GetString()}"));
        }
    }

    // JSON Schema's patterns are ECMA-262 regular expressions, whose $ is the end of the text;
    // .NET's $ also matches before a final line break, so a closing $ becomes \z.
    private Regex Pattern(string pattern)
    {
        if (!_patterns.TryGetValue(pattern, out var regex))
        {
            var text = pattern.EndsWith('$') && !pattern.EndsWith(@"\$", StringComparison.Ordinal)
                ? pattern[..^1] + @"\z"
                : pattern;
            regex = new Regex(text, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            _patterns[pattern] = regex;
        }
        return regex;
    }

    private static bool HasType(JsonElement instance, string type) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        _ => throw new InvalidDataException($"the schema names the type {type}, which JSON Schema does not have."),
    };

    // A member name in a JSON pointer (RFC 6901).
    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

// The checks of the list of verified iOS builds: the schema, then the two rules beyond it.
internal static class IosBuilds
{
    public const string DataPath = "compat/ios-builds.json";
    public const string SchemaPath = "compat/schema/ios-builds.schema.json";

    public static List<Problem> Check(string dataText, string schemaText, Func<string, bool> evidenceExists)
    {
        using var schema = JsonDocument.Parse(schemaText);
        var validator = new SchemaValidator(schema.RootElement, SchemaPath);
        var problems = new List<Problem>();
        JsonDocument data;
        try
        {
            data = JsonDocument.Parse(dataText);
        }
        catch (JsonException exception)
        {
            problems.Add(new Problem(DataPath, "", "json", $"the file is not JSON: {exception.Message}"));
            return problems;
        }
        using (data)
        {
            validator.Validate(data.RootElement, DataPath, problems);
            if (problems.Count == 0)
            {
                CheckRules(data.RootElement, evidenceExists, problems);
            }
        }
        return problems;
    }

    // A build and a model appear together in one entry only, so that a second record joins the
    // entry's evidence; and every record named exists.
    private static void CheckRules(JsonElement root, Func<string, bool> evidenceExists, List<Problem> problems)
    {
        var seen = new HashSet<(string, string)>();
        var index = 0;
        foreach (var build in root.GetProperty("builds").EnumerateArray())
        {
            var key = (build.GetProperty("iosBuild").GetString()!, build.GetProperty("deviceModel").GetString()!);
            if (!seen.Add(key))
            {
                problems.Add(new Problem(
                    DataPath, $"/builds/{index}", "unique-build",
                    "an earlier entry holds the same build and model; add the record to its evidence instead"));
            }
            var record = 0;
            foreach (var evidence in build.GetProperty("evidence").EnumerateArray())
            {
                if (!evidenceExists(evidence.GetString()!))
                {
                    problems.Add(new Problem(
                        DataPath, $"/builds/{index}/evidence/{record}", "evidence-file",
                        "the evidence record does not exist in the repository"));
                }
                record++;
            }
            index++;
        }
    }
}

internal static class CompatCheck
{
    public static int Run()
    {
        var root = Git.TopLevel();
        var problems = IosBuilds.Check(
            File.ReadAllText(Path.Combine(root, IosBuilds.DataPath)),
            File.ReadAllText(Path.Combine(root, IosBuilds.SchemaPath)),
            path => File.Exists(Path.Combine(root, path)));
        foreach (var problem in problems)
        {
            Console.WriteLine(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
                ? $"::error file={problem.File},title=Compatibility data::{problem}"
                : problem.ToString());
        }
        if (problems.Count > 0)
        {
            Console.WriteLine($"{problems.Count} problem(s) in the compatibility data.");
            return 1;
        }
        Console.WriteLine($"{IosBuilds.DataPath} matches {IosBuilds.SchemaPath}, and its rules hold.");
        return 0;
    }
}

internal static class Git
{
    public static string TopLevel()
    {
        var start = new ProcessStartInfo("git", ["rev-parse", "--show-toplevel"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var git = Process.Start(start) ?? throw new IOException("git could not start.");
        var output = git.StandardOutput.ReadToEnd().Trim();
        git.WaitForExit();
        if (git.ExitCode != 0 || output.Length == 0)
        {
            throw new IOException("Run tools/CheckCompat.cs inside the repository.");
        }
        return output;
    }
}

// Each fixture breaks exactly one rule, which the check must name, and the valid fixtures pass.
internal static class SelfTest
{
    private const string Record = "docs/evidence/2026-12-01-l3-jit.md";
    private const string GateRecord = "docs/evidence/2026-12-20-phase-1-gate.md";

    private const string Entry =
        """{ "iosVersion": "26.1", "iosBuild": "23B85", "deviceModel": "iPhone18,1", "evidence": ["docs/evidence/2026-12-01-l3-jit.md"] }""";

    private static readonly (string Name, string Data, string? Rule)[] s_fixtures =
    [
        ("empty list", """{ "schemaVersion": 1, "builds": [] }""", null),
        ("one entry", $$"""{ "schemaVersion": 1, "builds": [{{Entry}}] }""", null),
        ("two records and two models", """
            { "schemaVersion": 1, "builds": [
              { "iosVersion": "26.0.1", "iosBuild": "23A355", "deviceModel": "iPhone18,1",
                "evidence": ["docs/evidence/2026-12-01-l3-jit.md", "docs/evidence/2026-12-20-phase-1-gate.md"] },
              { "iosVersion": "27.0", "iosBuild": "24A5279h", "deviceModel": "iPhone18,2",
                "evidence": ["docs/evidence/2026-12-01-l3-jit.md"] } ] }
            """, null),
        ("not JSON", """{ "schemaVersion": 1, "builds": [ """, "json"),
        ("no schema version", """{ "builds": [] }""", "required"),
        ("schema version 2", """{ "schemaVersion": 2, "builds": [] }""", "const"),
        ("a member the schema lacks", """{ "schemaVersion": 1, "builds": [], "notes": "x" }""", "additionalProperties"),
        ("builds not a list", """{ "schemaVersion": 1, "builds": {} }""", "type"),
        ("no device model", """
            { "schemaVersion": 1, "builds": [ { "iosVersion": "26.1", "iosBuild": "23B85",
              "evidence": ["docs/evidence/2026-12-01-l3-jit.md"] } ] }
            """, "required"),
        ("a build in lower case", Doc(Entry.Replace("23B85", "23b85", StringComparison.Ordinal)), "pattern"),
        ("a device name for a model", Doc(Entry.Replace("iPhone18,1", "The test iPhone", StringComparison.Ordinal)), "pattern"),
        ("a version with a line break", Doc(Entry.Replace("\"26.1\"", "\"26.1\\n\"", StringComparison.Ordinal)), "pattern"),
        ("no evidence", Doc(Entry.Replace("[\"docs/evidence/2026-12-01-l3-jit.md\"]", "[]", StringComparison.Ordinal)), "minItems"),
        ("a record twice", Doc(Entry.Replace("[\"docs/evidence/2026-12-01-l3-jit.md\"]",
            "[\"docs/evidence/2026-12-01-l3-jit.md\", \"docs/evidence/2026-12-01-l3-jit.md\"]", StringComparison.Ordinal)),
            "uniqueItems"),
        ("a record outside docs/evidence", Doc(Entry.Replace("docs/evidence/", "docs/research/", StringComparison.Ordinal)), "pattern"),
        ("a record that does not exist", Doc(Entry.Replace("2026-12-01-l3-jit", "2026-12-02-l3-jit", StringComparison.Ordinal)),
            "evidence-file"),
        ("a model as a number", Doc(Entry.Replace("\"iPhone18,1\"", "18", StringComparison.Ordinal)), "type"),
        ("the same build and model twice", $$"""{ "schemaVersion": 1, "builds": [{{Entry}}, {{Entry}}] }""", "unique-build"),
    ];

    // A list document around one entry.
    private static string Doc(string entry) => $$"""{ "schemaVersion": 1, "builds": [{{entry}}] }""";

    public static int Run()
    {
        var schemaText = File.ReadAllText(Path.Combine(Git.TopLevel(), IosBuilds.SchemaPath));
        var failures = 0;
        foreach (var (name, data, rule) in s_fixtures)
        {
            var problems = IosBuilds.Check(data, schemaText, path => path is Record or GateRecord);
            var rules = problems.Select(problem => problem.Rule).Distinct().ToList();
            var passed = rule is null ? problems.Count == 0 : rules.Count == 1 && rules[0] == rule;
            if (!passed)
            {
                failures++;
                Console.WriteLine($"FAIL {name}: expected {rule ?? "no problem"}, found {string.Join(", ", problems)}");
            }
        }

        // A schema with a keyword that the validator does not implement is refused.
        try
        {
            using var planted = JsonDocument.Parse("""{ "type": "object", "properties": { "a": { "minimum": 1 } } }""");
            _ = new SchemaValidator(planted.RootElement, "planted schema");
            failures++;
            Console.WriteLine("FAIL a schema with an unknown keyword was taken.");
        }
        catch (InvalidDataException exception) when (exception.Message.Contains("/properties/a/minimum", StringComparison.Ordinal))
        {
        }

        if (failures > 0)
        {
            Console.WriteLine($"{failures} self-test case(s) failed.");
            return 1;
        }
        Console.WriteLine($"All {s_fixtures.Length + 1} self-test cases passed.");
        return 0;
    }
}
