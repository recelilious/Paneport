#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The compatibility check of C-0011 section 1, so far for the list of verified iOS builds:
// compat/ios-builds.json must match compat/schema/ios-builds.schema.json (C-0006 section 6),
// each build and device model may appear only once, and every evidence record that an entry
// names must exist. The validator is our own: it implements only the JSON Schema keywords
// (draft 2020-12) that the schema uses, each only in the forms that it implements, so that no
// keyword goes unchecked. It refuses a schema with any other keyword or another form of a
// keyword's value; with a $schema other than draft 2020-12, or a $schema or $id below the
// root; with a title, description or $id that is not a string; with a required list that names
// a member twice; and with a member that appears twice. The title files of compat/titles/ join
// with M8.3, which records decision D8 of 2026-11-23: a validator of our own, which M8.3
// extends (C-0011 section 5).
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

// The validator of the keywords that the schema uses. The annotations of s_annotations take no
// part in validation; CheckAnnotation checks their values.
internal sealed class SchemaValidator
{
    private const string DefinitionPrefix = "#/$defs/";

    // The dialect that the validator implements (C-0010 section 1).
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    private static readonly HashSet<string> s_annotations = ["$schema", "$id", "title", "description"];

    private static readonly HashSet<string> s_keywords =
    [
        "type", "required", "properties", "additionalProperties", "const", "items", "$ref", "$defs",
        "pattern", "minItems", "uniqueItems",
    ];

    private static readonly HashSet<string> s_types = ["object", "array", "string", "boolean", "null", "number", "integer"];

    private readonly JsonElement _root;
    private readonly Dictionary<string, Regex> _patterns = [];

    // Reads the schema, and refuses it when, anywhere in it, a keyword is one that this validator
    // does not implement, a keyword's value takes a form that it does not implement, or an
    // annotation breaks a rule of CheckAnnotation.
    public SchemaValidator(JsonElement root, string name)
    {
        _root = root;
        CheckKeywords(root, root, "", name);
    }

    private static void CheckKeywords(JsonElement root, JsonElement schema, string pointer, string name)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return;
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Refuse(name, pointer, "a schema must be an object or a boolean.");
        }
        foreach (var keyword in schema.EnumerateObject())
        {
            var at = $"{pointer}/{Escape(keyword.Name)}";
            var value = keyword.Value;
            if (s_annotations.Contains(keyword.Name))
            {
                CheckAnnotation(keyword.Name, value, pointer, at, name);
                continue;
            }
            if (!s_keywords.Contains(keyword.Name))
            {
                throw Refuse(name, at, "tools/CheckCompat.cs does not implement this keyword.");
            }
            switch (keyword.Name)
            {
                case "properties" or "$defs" when value.ValueKind != JsonValueKind.Object:
                    throw Refuse(name, at, "the keyword needs an object of schemas.");
                case "properties" or "$defs":
                    foreach (var child in value.EnumerateObject())
                    {
                        CheckKeywords(root, child.Value, $"{at}/{Escape(child.Name)}", name);
                    }
                    break;
                case "items" or "additionalProperties":
                    CheckKeywords(root, value, at, name);
                    break;
                case "type" when value.ValueKind != JsonValueKind.String || !s_types.Contains(value.GetString()!):
                    throw Refuse(name, at, "the keyword needs one of the seven type names of JSON Schema.");
                case "required" when value.ValueKind != JsonValueKind.Array
                    || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String):
                    throw Refuse(name, at, "the keyword needs a list of member names.");
                case "required" when value.EnumerateArray().Select(item => item.GetString())
                    .Distinct(StringComparer.Ordinal).Count() != value.GetArrayLength():
                    throw Refuse(name, at, "the keyword names each member once.");
                case "minItems" when value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var least) || least < 0:
                    throw Refuse(name, at, "the keyword needs a non-negative integer.");
                case "uniqueItems" when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False):
                    throw Refuse(name, at, "the keyword needs true or false.");
                case "pattern" when value.ValueKind != JsonValueKind.String || !Compiles(value.GetString()!):
                    throw Refuse(name, at, "the keyword needs a regular expression.");
                case "$ref" when !Resolves(root, value):
                    throw Refuse(name, at, "only a reference to a member of the root's $defs is implemented.");
            }
        }
    }

    // The annotations take no part in validation, but JSON Schema 2020-12 wants each to be a
    // string, $schema names the dialect, which must be the one that this validator implements,
    // and below the root $schema and $id would start a schema of their own, which it does not
    // implement.
    private static void CheckAnnotation(string keyword, JsonElement value, string pointer, string at, string name)
    {
        if (keyword is "$schema" or "$id" && pointer.Length > 0)
        {
            throw Refuse(name, at, "only the root may give $schema or $id.");
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Refuse(name, at, "the keyword needs a string.");
        }
        if (keyword == "$schema" && value.GetString() != Dialect)
        {
            throw Refuse(name, at, $"tools/CheckCompat.cs implements only the dialect {Dialect}.");
        }
    }

    private static InvalidDataException Refuse(string name, string pointer, string detail) =>
        new($"{name}: {(pointer.Length == 0 ? "/" : pointer)}: {detail}");

    // A reference names a member of the root's $defs, written plainly: #/$defs/name.
    private static bool Resolves(JsonElement root, JsonElement reference)
    {
        if (reference.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        var text = reference.GetString()!;
        if (!text.StartsWith(DefinitionPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        var definition = text[DefinitionPrefix.Length..];
        return definition.Length > 0
            && definition.IndexOfAny(['/', '~']) < 0
            && root.TryGetProperty("$defs", out var definitions)
            && definitions.ValueKind == JsonValueKind.Object
            && definitions.TryGetProperty(definition, out _);
    }

    private static bool Compiles(string pattern)
    {
        try
        {
            _ = Translate(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
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
            var target = _root.GetProperty("$defs").GetProperty(reference.GetString()![DefinitionPrefix.Length..]);
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
        if (schema.TryGetProperty("pattern", out var pattern) && !Pattern(pattern.GetString()!).IsMatch(value))
        {
            problems.Add(new Problem(file, pointer, "pattern", $"the text must match {pattern.GetString()}"));
        }
    }

    private Regex Pattern(string pattern)
    {
        if (!_patterns.TryGetValue(pattern, out var regex))
        {
            regex = Translate(pattern);
            _patterns[pattern] = regex;
        }
        return regex;
    }

    // JSON Schema's patterns are ECMA-262 regular expressions, whose $ is the end of the text;
    // .NET's $ also matches before a final line break, so a closing $ becomes \z.
    private static Regex Translate(string pattern)
    {
        var text = pattern.EndsWith('$') && !pattern.EndsWith(@"\$", StringComparison.Ordinal)
            ? pattern[..^1] + @"\z"
            : pattern;
        return new Regex(text, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
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

    // A member that appears twice in one object is refused, in the data and in the schema: a
    // reader that takes one copy would see a value that the check never saw.
    public static readonly JsonDocumentOptions Options = new() { AllowDuplicateProperties = false };

    // Loads the schema as the check reads it: parsed with Options, and refused when it is not
    // JSON, repeats a member or is refused by SchemaValidator. Each refusal names the file.
    public static SchemaValidator LoadSchema(string schemaText)
    {
        JsonDocument schema;
        try
        {
            schema = JsonDocument.Parse(schemaText, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{SchemaPath}: the schema is not JSON, or repeats a member: {exception.Message}", exception);
        }
        using (schema)
        {
            return new SchemaValidator(schema.RootElement.Clone(), SchemaPath);
        }
    }

    public static List<Problem> Check(string dataText, string schemaText, Func<string, bool> evidenceExists)
    {
        var validator = LoadSchema(schemaText);
        var problems = new List<Problem>();
        JsonDocument data;
        try
        {
            data = JsonDocument.Parse(dataText, Options);
        }
        catch (JsonException exception)
        {
            problems.Add(new Problem(DataPath, "", "json", $"the file is not JSON, or repeats a member: {exception.Message}"));
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

    // The entry with another model, and with another build. Declared before s_fixtures, which
    // uses them, because static fields start in the order of the text.
    private static readonly string s_otherModel = Entry.Replace("iPhone18,1", "iPhone18,2", StringComparison.Ordinal);
    private static readonly string s_otherBuild = Entry.Replace("23B85", "23C55", StringComparison.Ordinal);

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
        ("the same build on two models", $$"""{ "schemaVersion": 1, "builds": [{{Entry}}, {{s_otherModel}}] }""", null),
        ("the same model on two builds", $$"""{ "schemaVersion": 1, "builds": [{{Entry}}, {{s_otherBuild}}] }""", null),
        ("the same build and model apart",
            $$"""{ "schemaVersion": 1, "builds": [{{Entry}}, {{s_otherModel}}, {{Entry}}] }""", "unique-build"),
        ("a bad second entry", $$"""{ "schemaVersion": 1, "builds": [{{Entry}}, {{s_otherBuild.Replace("23C55", "23c55", StringComparison.Ordinal)}}] }""",
            "pattern"),
        ("a device name in an entry", Doc(Entry.Replace("\"deviceModel\"", "\"deviceName\": \"The test iPhone\", \"deviceModel\"",
            StringComparison.Ordinal)), "additionalProperties"),
        ("no builds", """{ "schemaVersion": 1 }""", "required"),
        ("no iOS version", Doc(Entry.Replace("\"iosVersion\": \"26.1\", ", "", StringComparison.Ordinal)), "required"),
        ("no iOS build", Doc(Entry.Replace("\"iosBuild\": \"23B85\", ", "", StringComparison.Ordinal)), "required"),
        ("no evidence member", Doc(Entry.Replace(", \"evidence\": [\"docs/evidence/2026-12-01-l3-jit.md\"]", "",
            StringComparison.Ordinal)), "required"),
        ("a record as a number", Doc(Entry.Replace("[\"docs/evidence/2026-12-01-l3-jit.md\"]", "[18]", StringComparison.Ordinal)),
            "type"),
        ("a record twice with another between", Doc(Entry.Replace("[\"docs/evidence/2026-12-01-l3-jit.md\"]",
            $"[\"{Record}\", \"{GateRecord}\", \"{Record}\"]", StringComparison.Ordinal)), "uniqueItems"),
        ("a member twice", Doc(Entry.Replace("\"evidence\":",
            "\"evidence\": [\"docs/evidence/2026-12-02-l3-jit.md\"], \"evidence\":", StringComparison.Ordinal)), "json"),
    ];

    // Schemas that the check refuses, each with the part of the refusal that names its place: an
    // unknown keyword anywhere, a keyword that the schema does not use, a keyword's value in a
    // form that the validator does not implement, an annotation that breaks its rules, and a
    // member that appears twice, which names the file.
    private static readonly (string Name, string Schema, string Refusal)[] s_plantedSchemas =
    [
        ("an unknown keyword in a property", """{ "type": "object", "properties": { "a": { "minimum": 1 } } }""",
            "/properties/a/minimum:"),
        ("an unknown keyword in a definition", """{ "$defs": { "a": { "maxLength": 1 } } }""", "/$defs/a/maxLength:"),
        ("an unknown keyword in items", """{ "type": "array", "items": { "format": "date" } }""", "/items/format:"),
        ("an unknown keyword in additionalProperties", """{ "type": "object", "additionalProperties": { "default": 1 } }""",
            "/additionalProperties/default:"),
        ("enum", """{ "enum": [1] }""", "/enum:"),
        ("minLength", """{ "type": "string", "minLength": 1 }""", "/minLength:"),
        ("$comment", """{ "$comment": "a note" }""", "/$comment:"),
        ("a reference to a missing definition", """{ "items": { "$ref": "#/$defs/missing" }, "$defs": {} }""", "/items/$ref:"),
        ("a reference into a definition",
            """{ "items": { "$ref": "#/$defs/a/properties/b" }, "$defs": { "a": { "properties": { "b": true } } } }""",
            "/items/$ref:"),
        ("uniqueItems as text", """{ "type": "array", "uniqueItems": "true" }""", "/uniqueItems:"),
        ("minItems as text", """{ "type": "array", "minItems": "1" }""", "/minItems:"),
        ("minItems below zero", """{ "type": "array", "minItems": -1 }""", "/minItems:"),
        ("type as a list", """{ "type": ["string"] }""", "/type:"),
        ("a type JSON Schema lacks", """{ "type": "text" }""", "/type:"),
        ("required as text", """{ "type": "object", "required": "a" }""", "/required:"),
        ("a pattern that does not compile", """{ "type": "string", "pattern": "[" }""", "/pattern:"),
        ("properties as a list", """{ "type": "object", "properties": [] }""", "/properties:"),
        ("a required name that is not text", """{ "type": "object", "required": [1] }""", "/required:"),
        ("a required name twice", """{ "type": "object", "required": ["a", "a"] }""", "/required:"),
        ("a pattern that is not text", """{ "type": "string", "pattern": 1 }""", "/pattern:"),
        ("a reference that is not text", """{ "items": { "$ref": 1 }, "$defs": {} }""", "/items/$ref:"),
        ("a reference to a name with a slash", """{ "items": { "$ref": "#/$defs/a/b" }, "$defs": { "a/b": true } }""",
            "/items/$ref:"),
        ("items that is not a schema", """{ "items": 1 }""", "/items:"),
        ("another dialect", """{ "$schema": "http://json-schema.org/draft-07/schema#" }""", "/$schema:"),
        ("$schema below the root", """{ "items": { "$schema": "https://json-schema.org/draft/2020-12/schema" } }""",
            "/items/$schema:"),
        ("$id below the root", """{ "$defs": { "a": { "$id": "https://example.com/a" } } }""", "/$defs/a/$id:"),
        ("an $id that is not text", """{ "$id": 5 }""", "/$id:"),
        ("a title that is not text", """{ "title": 5 }""", "/title:"),
        ("a description that is not text", """{ "type": "object", "properties": { "a": { "description": [] } } }""",
            "/properties/a/description:"),
        ("a member twice", """{ "type": "object", "type": "array" }""",
            $"{IosBuilds.SchemaPath}: the schema is not JSON, or repeats a member:"),
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

        foreach (var (name, schema, refusal) in s_plantedSchemas)
        {
            var found = Refusal(schema);
            if (found is null || !found.Contains(refusal, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"FAIL the planted schema with {name}: expected a refusal at {refusal}, found {found ?? "none"}");
            }
        }

        if (failures > 0)
        {
            Console.WriteLine($"{failures} self-test case(s) failed.");
            return 1;
        }
        Console.WriteLine($"All {s_fixtures.Length + s_plantedSchemas.Length} self-test cases passed.");
        return 0;
    }

    // Loads a schema through the check's own loader, and returns why it was refused, or null.
    private static string? Refusal(string schema)
    {
        try
        {
            _ = IosBuilds.LoadSchema(schema);
            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            // A rule that went missing lets a value through to a reader that cannot take it.
            return $"a crash: {exception.Message}";
        }
    }
}
