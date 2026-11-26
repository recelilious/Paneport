#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The compatibility check of C-0011 section 1:
//
// - Every title file in compat/titles/ reads as JSON the way the app reads it, matches
//   compat/schema/title.schema.json, is named after its id, holds to the launch and allow-list rules of
//   C-0008 section 5 and the rating rule of C-0008 section 3, and names evidence records that exist. The
//   environment allow-list of the title schema holds no Steam variable (C-0008 section 5).
// - compat/ios-builds.json reads as JSON the way the app reads a title file, matches
//   compat/schema/ios-builds.schema.json (C-0006 section 6), each build and model appears only once, and every
//   evidence record that an entry names exists.
//
// The validator is our own (plan decision D8 of 2026-11-23): it implements only the JSON Schema keywords (draft
// 2020-12) that the schemas use, each only in the forms that it implements, so that no keyword goes unchecked.
// Each schema may use only the keywords and annotations that it uses now, which the check lists for it. The
// validator refuses a schema with any other keyword or another form of a keyword's value; with a $schema other
// than draft 2020-12, or a $schema or $id below the root; with a title, description, $comment or $id that is not
// a string; with a required list that names a member twice; with a member that appears twice; with half of a
// surrogate pair; and with a reference that leads back to itself without a step into the value.
//
//   dotnet run tools/CheckCompat.cs                 check the repository
//   dotnet run tools/CheckCompat.cs -- --self-test  check the validator and the rules on fixtures
//
// A problem names the file, the JSON pointer and the rule, never the value. Exit codes: 0 the
// data is valid, 1 it is not, 2 usage or configuration error. docs/design/repository-checks.md
// describes the check, and docs/design/compat-db.md the rules of the title files.

#:property PublishAot=false

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

// One problem: the file, the JSON pointer of the value, the rule it breaks, and what the rule wants. The value
// itself is never part of it. A problem that the schema finds also knows where in the schema its keyword sits.
internal sealed record Problem(string File, string Pointer, string Rule, string Detail)
{
    public string Location { get; init; } = "";

    public override string ToString() => $"{File}: {(Pointer.Length == 0 ? "/" : Pointer)}: {Rule}: {Detail}";
}

// The segment of a member name in a JSON pointer (RFC 6901). A name of the data that is not a plain one might
// hold a local path or other personal data, so it shows as <name not shown>, as the app and the personal-data
// scanner print such names. A plain name holds neither ~ nor /, so it needs no escape. A name of a schema, which
// is our own, shows in full, escaped as RFC 6901 escapes it.
internal static class JsonPointer
{
    public const string NameNotShown = "<name not shown>";

    public static string Segment(string name) =>
        name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            ? name
            : NameNotShown;

    public static string Escape(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}

// The validator of the JSON Schema keywords (draft 2020-12) that the schemas in compat/schema use, and of no
// other. Each schema has a vocabulary, the keywords and annotations that it uses now, and may use no other, so
// that a keyword it starts to use comes with self-test cases of its own. The validator refuses a schema with a
// keyword outside its vocabulary, with a keyword's value in a form that it does not implement, with a format
// other than date, with a part of a pattern that EcmaPattern does not translate, or with an annotation that
// breaks a rule of CheckAnnotation. Annotations take no part in validation. Where the app's reader reads a value
// more strictly than JSON Schema, the validator follows the reader (docs/design/compat-db.md section 9): an
// integer is written without a fraction or an exponent, a number is finite, a number above an exclusive minimum
// is above it also as the double that the reader reads, a number at the top of a const or an enum matches it
// only as the same text, and format date is asserted. Numbers nested in a list or an object of a const or an
// enum compare by value, as JSON Schema compares them. Below not, each of these stricter readings is looser than
// JSON Schema, and in an if condition or a oneOf branch, each can go either way. No schema has one in those
// places, which the self-test checks.
internal sealed class SchemaValidator
{
    private const string DefinitionPrefix = "#/$defs/";

    // The dialect that the validator implements (C-0010 section 1).
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";

    // What the validator implements: the annotations and keywords of the schemas' vocabularies, and no more.
    private static readonly HashSet<string> s_annotations =
        ["$schema", "$id", "title", "description", "$comment", "default"];

    private static readonly HashSet<string> s_keywords =
    [
        "type", "required", "properties", "additionalProperties", "const", "enum", "items", "$ref", "$defs",
        "pattern", "minItems", "uniqueItems", "maxLength", "minimum", "maximum", "exclusiveMinimum", "format",
        "allOf", "anyOf", "oneOf", "not", "if", "then", "else",
    ];

    private static readonly HashSet<string> s_types =
        ["object", "array", "string", "boolean", "null", "number", "integer"];

    // Everything that the validator implements, the vocabulary of its own cases in the self-test.
    public static readonly IReadOnlySet<string> Everything = s_annotations.Union(s_keywords).ToHashSet(StringComparer.Ordinal);

    // A member that appears twice in one object is refused in a schema, as JsonText refuses it in the data: a
    // reader that takes one copy would see a value that the check never saw.
    private static readonly JsonDocumentOptions s_options = new() { AllowDuplicateProperties = false };

    private readonly JsonElement _root;
    private readonly string _name;
    private readonly IReadOnlySet<string> _vocabulary;
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Regex> _patterns = new(StringComparer.Ordinal);

    // Reads the schema, and refuses it when, anywhere in it, a keyword is outside its vocabulary, a keyword's
    // value takes a form that this validator does not implement, or an annotation breaks a rule of
    // CheckAnnotation.
    private SchemaValidator(JsonElement root, string name, IReadOnlySet<string> vocabulary)
    {
        _root = root;
        _name = name;
        _vocabulary = vocabulary;
        CheckSchema(root, "");
        CheckReferences();
    }

    // The schema's root, which outlives the text that it was read from.
    public JsonElement Root => _root;

    // The keywords and annotations that the schema uses, which the self-test compares with its vocabulary.
    public IReadOnlySet<string> Used => _used;

    // Loads a schema as the check reads it: parsed with s_options, and refused when it is not JSON, repeats a
    // member, escapes half of a surrogate pair in a name or a text, which no reader can take, or is refused by
    // the validator. Each refusal names the file.
    public static SchemaValidator LoadSchema(string text, string name, IReadOnlySet<string> vocabulary)
    {
        JsonDocument schema;
        try
        {
            schema = JsonDocument.Parse(text, s_options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"{name}: the schema is not JSON, or repeats a member: {exception.Message}", exception);
        }
        catch (InvalidOperationException exception)
        {
            // The search for a member twice reads every member name, and cannot read one that escapes half of a
            // surrogate pair. Read without that search, the schema shows where the name is.
            using var lenient = JsonDocument.Parse(text);
            throw HalfPairIn(lenient.RootElement, name)
                ?? new InvalidDataException($"{name}: the schema cannot be read: {exception.Message}", exception);
        }
        using (schema)
        {
            if (HalfPairIn(schema.RootElement, name) is { } refusal)
            {
                throw refusal;
            }
            return new SchemaValidator(schema.RootElement.Clone(), name, vocabulary);
        }
    }

    // Half of a surrogate pair in a member name or a text of a schema, found as JsonText finds it in the data. The
    // refusal names its place in full, since a name of a schema is our own.
    private static InvalidDataException? HalfPairIn(JsonElement root, string name)
    {
        var found = new List<Problem>();
        JsonText.Scan(root, "", name, found, JsonPointer.Escape);
        return found is [var half, ..]
            ? new InvalidDataException($"{name}: {(half.Pointer.Length == 0 ? "/" : half.Pointer)}: {half.Detail}.")
            : null;
    }

    private InvalidDataException Refuse(string pointer, string reason) =>
        new($"{_name}: {(pointer.Length == 0 ? "/" : pointer)}: {reason}");

    private void CheckSchema(JsonElement schema, string pointer)
    {
        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return;
        }
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Refuse(pointer, "a schema must be an object or a boolean.");
        }
        foreach (var keyword in schema.EnumerateObject())
        {
            var at = $"{pointer}/{JsonPointer.Escape(keyword.Name)}";
            if (!Everything.Contains(keyword.Name))
            {
                throw Refuse(at, "tools/CheckCompat.cs does not implement this keyword.");
            }
            if (!_vocabulary.Contains(keyword.Name))
            {
                throw Refuse(at, "tools/CheckCompat.cs takes only the keywords that this schema uses, and this is "
                    + "not one of them.");
            }
            _used.Add(keyword.Name);
            if (s_annotations.Contains(keyword.Name))
            {
                CheckAnnotation(keyword.Name, keyword.Value, pointer, at);
                continue;
            }
            CheckKeyword(keyword.Name, keyword.Value, at);
        }
    }

    private void CheckKeyword(string keyword, JsonElement value, string at)
    {
        switch (keyword)
        {
            case "properties" or "$defs":
                Expect(value, JsonValueKind.Object, at, "the keyword needs an object of schemas.");
                foreach (var child in value.EnumerateObject())
                {
                    CheckSchema(child.Value, $"{at}/{JsonPointer.Escape(child.Name)}");
                }
                break;
            case "items" or "additionalProperties" or "not" or "if" or "then" or "else":
                CheckSchema(value, at);
                break;
            case "allOf" or "anyOf" or "oneOf":
                Expect(value, JsonValueKind.Array, at, "the keyword needs a list of schemas.");
                if (value.GetArrayLength() == 0)
                {
                    throw Refuse(at, "the list of schemas is empty.");
                }
                var index = 0;
                foreach (var branch in value.EnumerateArray())
                {
                    CheckSchema(branch, $"{at}/{index++}");
                }
                break;
            case "$ref":
                _ = Definition(value, at);
                break;
            case "type":
                if (value.ValueKind != JsonValueKind.String || !s_types.Contains(value.GetString()!))
                {
                    throw Refuse(at, "the keyword needs one of the seven type names of JSON Schema.");
                }
                break;
            case "format":
                if (value.ValueKind != JsonValueKind.String || value.GetString() != "date")
                {
                    throw Refuse(at, "only the format date is implemented.");
                }
                break;
            case "pattern":
                Expect(value, JsonValueKind.String, at, "the keyword needs a regular expression.");
                _ = Pattern(value.GetString()!, at);
                break;
            case "required":
                if (value.ValueKind != JsonValueKind.Array
                    || value.EnumerateArray().Any(name => name.ValueKind != JsonValueKind.String))
                {
                    throw Refuse(at, "the keyword needs a list of member names.");
                }
                if (value.EnumerateArray().Select(name => name.GetString()).Distinct(StringComparer.Ordinal).Count()
                    != value.GetArrayLength())
                {
                    throw Refuse(at, "the keyword names each member once.");
                }
                break;
            case "enum":
                Expect(value, JsonValueKind.Array, at, "the keyword needs a list of values.");
                break;
            case "minItems" or "maxLength":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var limit) || limit < 0)
                {
                    throw Refuse(at, "the keyword needs a non-negative integer.");
                }
                break;
            case "minimum" or "maximum" or "exclusiveMinimum":
                Expect(value, JsonValueKind.Number, at, "the keyword needs a number.");
                break;
            case "uniqueItems":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw Refuse(at, "the keyword needs true or false.");
                }
                break;
        }
    }

    // The annotations take no part in validation, but JSON Schema 2020-12 wants $schema, $id, title,
    // description and $comment each to be a string; default may be any value. $schema names the dialect, which
    // must be the one that this validator implements, and below the root $schema and $id would start a schema of
    // their own, whose references this validator does not resolve.
    private void CheckAnnotation(string keyword, JsonElement value, string pointer, string at)
    {
        if (keyword is "$schema" or "$id" && pointer.Length > 0)
        {
            throw Refuse(at, "only the root may give $schema or $id.");
        }
        if (keyword != "default" && value.ValueKind != JsonValueKind.String)
        {
            throw Refuse(at, "the keyword needs a string.");
        }
        if (keyword == "$schema" && value.GetString() != Dialect)
        {
            throw Refuse(at, $"tools/CheckCompat.cs implements only the dialect {Dialect}.");
        }
    }

    private void Expect(JsonElement value, JsonValueKind kind, string at, string reason)
    {
        if (value.ValueKind != kind)
        {
            throw Refuse(at, reason);
        }
    }

    // A reference names a member of the root's $defs, written plainly: #/$defs/name, with neither / nor ~ in the
    // name, which a JSON pointer would read as a step deeper or as an escape.
    private JsonElement Definition(JsonElement reference, string at)
    {
        var text = reference.ValueKind == JsonValueKind.String ? reference.GetString()! : "";
        var name = text.StartsWith(DefinitionPrefix, StringComparison.Ordinal) ? text[DefinitionPrefix.Length..] : "";
        if (name.Length == 0
            || name.IndexOfAny(['/', '~']) >= 0
            || !_root.TryGetProperty("$defs", out var definitions)
            || definitions.ValueKind != JsonValueKind.Object
            || !definitions.TryGetProperty(name, out var target))
        {
            throw Refuse(at, "only a reference to a member of the root's $defs is implemented.");
        }
        return target;
    }

    // A reference that leads back to itself, through $ref and the applicators that apply to the same value
    // (allOf, anyOf, oneOf, not, if, then and else), without a step into the value through properties, items or
    // additionalProperties, would be followed without end. JSON Schema 2020-12 leaves such a schema undefined, and
    // the check refuses it.
    private void CheckReferences()
    {
        // A schema of true or false refers to nothing.
        if (_root.ValueKind != JsonValueKind.Object || !_root.TryGetProperty("$defs", out var definitions))
        {
            return;
        }
        // A definition on the path being followed is false, and one whose references all end is true.
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);
        void follow(string name, string at)
        {
            if (state.TryGetValue(name, out var ends))
            {
                if (!ends)
                {
                    throw Refuse(at, "a reference leads back to itself without a step into the value, so the "
                        + "validation would never end.");
                }
                return;
            }
            state[name] = false;
            foreach (var (next, nextAt) in InPlaceReferences(
                definitions.GetProperty(name), $"/$defs/{JsonPointer.Escape(name)}"))
            {
                follow(next, nextAt);
            }
            state[name] = true;
        }
        foreach (var definition in definitions.EnumerateObject())
        {
            follow(definition.Name, $"/$defs/{JsonPointer.Escape(definition.Name)}");
        }
    }

    // The definitions that a schema refers to for the same value, with the place of each $ref.
    private static IEnumerable<(string Name, string At)> InPlaceReferences(JsonElement schema, string pointer)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }
        foreach (var keyword in schema.EnumerateObject())
        {
            var at = $"{pointer}/{JsonPointer.Escape(keyword.Name)}";
            if (keyword.Name == "$ref")
            {
                yield return (keyword.Value.GetString()![DefinitionPrefix.Length..], at);
            }
            IEnumerable<(JsonElement Branch, string At)> branches = keyword.Name switch
            {
                "not" or "if" or "then" or "else" => [(keyword.Value, at)],
                "allOf" or "anyOf" or "oneOf" =>
                    keyword.Value.EnumerateArray().Select((branch, index) => (branch, $"{at}/{index}")),
                _ => [],
            };
            foreach (var (branch, branchAt) in branches)
            {
                foreach (var reference in InPlaceReferences(branch, branchAt))
                {
                    yield return reference;
                }
            }
        }
    }

    private Regex Pattern(string pattern, string at)
    {
        if (!_patterns.TryGetValue(pattern, out var regex))
        {
            try
            {
                regex = new Regex(
                    EcmaPattern.Translate(pattern), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException exception)
            {
                throw Refuse(
                    at, $"tools/CheckCompat.cs does not implement a part of this pattern: {exception.Message}.");
            }
            _patterns[pattern] = regex;
        }
        return regex;
    }

    // A pattern that does not finish matching a text within its timeout stops the validation of the file, which
    // fails with pattern there: under not, or in a branch of anyOf, a failed match would otherwise count as a
    // result.
    public void Validate(JsonElement instance, string file, List<Problem> problems)
    {
        try
        {
            Validate(_root, instance, "", "", file, problems);
        }
        catch (PatternTimeout timeout)
        {
            problems.Add(new Problem(file, timeout.Pointer, "pattern", "the text could not be matched within 1 s")
            {
                Location = timeout.Location,
            });
        }
    }

    private sealed class PatternTimeout(string pointer, string location) : Exception("a pattern timed out")
    {
        public string Pointer { get; } = pointer;

        public string Location { get; } = location;
    }

    // Whether the instance matches the schema, for the subschemas of anyOf, oneOf, not and if, whose own
    // problems are never reported. They are found at the value's own pointer and the subschema's location, so
    // that a pattern that times out there names its place.
    private bool Passes(JsonElement schema, JsonElement instance, string pointer, string location)
    {
        var problems = new List<Problem>();
        Validate(schema, instance, pointer, location, "", problems);
        return problems.Count == 0;
    }

    private void Validate(
        JsonElement schema, JsonElement instance, string pointer, string location, string file, List<Problem> problems)
    {
        void add(string keyword, string detail) =>
            problems.Add(new Problem(file, pointer, keyword, detail) { Location = $"{location}/{keyword}" });

        if (schema.ValueKind == JsonValueKind.True)
        {
            return;
        }
        if (schema.ValueKind == JsonValueKind.False)
        {
            problems.Add(new Problem(file, pointer, "false", "no value is allowed here") { Location = location });
            return;
        }
        if (schema.TryGetProperty("$ref", out var reference))
        {
            Validate(Definition(reference, location), instance, pointer, $"{location}/$ref", file, problems);
        }
        if (schema.TryGetProperty("type", out var type) && !HasType(instance, type.GetString()!))
        {
            add("type", type.GetString() == "number" && instance.ValueKind == JsonValueKind.Number
                ? "the number is too large to read"
                : $"the value must be of type {type.GetString()}");
            return;
        }
        if (schema.TryGetProperty("const", out var constant) && !Same(instance, constant))
        {
            add("const", $"the value must be {constant.GetRawText()}");
        }
        if (schema.TryGetProperty("enum", out var choices)
            && !choices.EnumerateArray().Any(choice => Same(instance, choice)))
        {
            add("enum", "the value is not one of those that the schema lists");
        }
        switch (instance.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(schema, instance, pointer, location, file, problems);
                break;
            case JsonValueKind.Array:
                ValidateArray(schema, instance, pointer, location, file, problems);
                break;
            case JsonValueKind.String:
                ValidateString(schema, instance.GetString()!, pointer, location, add);
                break;
            case JsonValueKind.Number:
                ValidateNumber(schema, instance, add);
                break;
        }
        ValidateApplicators(schema, instance, pointer, location, file, problems, add);
    }

    private void ValidateObject(
        JsonElement schema, JsonElement instance, string pointer, string location, string file, List<Problem> problems)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()!))
            {
                if (!instance.TryGetProperty(name, out _))
                {
                    problems.Add(new Problem(file, pointer, "required", $"the member {name} is missing")
                    {
                        Location = $"{location}/required",
                    });
                }
            }
        }
        var properties = schema.TryGetProperty("properties", out var declared) ? declared : default;
        foreach (var member in instance.EnumerateObject())
        {
            var at = $"{pointer}/{JsonPointer.Segment(member.Name)}";
            if (properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty(member.Name, out var memberSchema))
            {
                Validate(
                    memberSchema, member.Value, at, $"{location}/properties/{JsonPointer.Escape(member.Name)}", file,
                    problems);
            }
            else if (schema.TryGetProperty("additionalProperties", out var additional))
            {
                if (additional.ValueKind == JsonValueKind.False)
                {
                    problems.Add(new Problem(file, at, "additionalProperties", "the schema has no such member")
                    {
                        Location = $"{location}/additionalProperties",
                    });
                }
                else
                {
                    Validate(additional, member.Value, at, $"{location}/additionalProperties", file, problems);
                }
            }
        }
    }

    private void ValidateArray(
        JsonElement schema, JsonElement instance, string pointer, string location, string file, List<Problem> problems)
    {
        var items = instance.EnumerateArray().ToList();
        if (schema.TryGetProperty("minItems", out var minimum) && items.Count < minimum.GetInt32())
        {
            problems.Add(new Problem(file, pointer, "minItems", $"the list needs at least {minimum.GetInt32()} items")
            {
                Location = $"{location}/minItems",
            });
        }
        if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
        {
            // Every item goes through one set of keys, which two items share where JSON Schema counts them equal,
            // so that 1, 1.0 and 1e0 meet, and so do two objects of the same members in another order. A long list
            // takes no quadratic time.
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < items.Count; index++)
            {
                if (!keys.Add(ValueKey.Of(items[index])))
                {
                    problems.Add(new Problem(
                        file, $"{pointer}/{index}", "uniqueItems", "the item repeats an earlier one")
                    {
                        Location = $"{location}/uniqueItems",
                    });
                }
            }
        }
        if (schema.TryGetProperty("items", out var itemSchema))
        {
            for (var index = 0; index < items.Count; index++)
            {
                Validate(itemSchema, items[index], $"{pointer}/{index}", $"{location}/items", file, problems);
            }
        }
    }

    // JSON Schema counts the length of a text in characters, as code points, not in UTF-16 units.
    private void ValidateString(
        JsonElement schema, string value, string pointer, string location, Action<string, string> add)
    {
        if (schema.TryGetProperty("maxLength", out var maximum) && value.EnumerateRunes().Count() > maximum.GetInt32())
        {
            add("maxLength", $"the text may have at most {maximum.GetInt32()} characters");
        }
        if (schema.TryGetProperty("pattern", out var pattern))
        {
            bool matches;
            try
            {
                matches = Pattern(pattern.GetString()!, location).IsMatch(value);
            }
            catch (RegexMatchTimeoutException)
            {
                throw new PatternTimeout(pointer, $"{location}/pattern");
            }
            if (!matches)
            {
                add("pattern", $"the text must match {pattern.GetString()}");
            }
        }
        if (schema.TryGetProperty("format", out _) && !IsDate(value))
        {
            add("format", "the text must be a calendar date, YYYY-MM-DD (RFC 3339 full-date)");
        }
    }

    private static void ValidateNumber(JsonElement schema, JsonElement instance, Action<string, string> add)
    {
        if (schema.TryGetProperty("minimum", out var minimum) && Compare(instance, minimum) < 0)
        {
            add("minimum", $"the number must be at least {minimum.GetRawText()}");
        }
        if (schema.TryGetProperty("maximum", out var maximum) && Compare(instance, maximum) > 0)
        {
            add("maximum", $"the number must be at most {maximum.GetRawText()}");
        }
        // The reader reads a number with a fraction as a double, so a positive number too small for one, such as
        // 1e-400, reads as 0 and is not above an exclusive minimum of 0 there.
        if (schema.TryGetProperty("exclusiveMinimum", out var exclusive)
            && (Compare(instance, exclusive) <= 0 || instance.GetDouble() <= exclusive.GetDouble()))
        {
            add("exclusiveMinimum", $"the number must be greater than {exclusive.GetRawText()}");
        }
    }

    private void ValidateApplicators(
        JsonElement schema,
        JsonElement instance,
        string pointer,
        string location,
        string file,
        List<Problem> problems,
        Action<string, string> add)
    {
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            var index = 0;
            foreach (var branch in allOf.EnumerateArray())
            {
                Validate(branch, instance, pointer, $"{location}/allOf/{index++}", file, problems);
            }
        }
        if (schema.TryGetProperty("anyOf", out var anyOf)
            && !anyOf.EnumerateArray().Select((branch, index) => (branch, index))
                .Any(part => Passes(part.branch, instance, pointer, $"{location}/anyOf/{part.index}")))
        {
            add("anyOf", "the value has none of the forms that the schema allows here");
        }
        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            var matches = oneOf.EnumerateArray().Select((branch, index) => (branch, index))
                .Count(part => Passes(part.branch, instance, pointer, $"{location}/oneOf/{part.index}"));
            if (matches != 1)
            {
                add("oneOf", "the value must have exactly one of the forms that the schema lists here, "
                    + $"and has {matches}");
            }
        }
        if (schema.TryGetProperty("not", out var not) && Passes(not, instance, pointer, $"{location}/not"))
        {
            var why = not.ValueKind == JsonValueKind.Object && not.TryGetProperty("$comment", out var comment)
                ? $": {comment.GetString()}"
                : "";
            add("not", $"the value has a form that the schema forbids here{why}");
        }
        if (schema.TryGetProperty("if", out var condition))
        {
            var branch = Passes(condition, instance, pointer, $"{location}/if") ? "then" : "else";
            if (schema.TryGetProperty(branch, out var consequence))
            {
                Validate(consequence, instance, pointer, $"{location}/{branch}", file, problems);
            }
        }
    }

    // An integer is written without a fraction or an exponent, as the app's reader requires, and a number is
    // finite, as the reader checks with double.IsFinite.
    private static bool HasType(JsonElement instance, string type) => type switch
    {
        "object" => instance.ValueKind == JsonValueKind.Object,
        "array" => instance.ValueKind == JsonValueKind.Array,
        "string" => instance.ValueKind == JsonValueKind.String,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => instance.ValueKind == JsonValueKind.Null,
        "number" => instance.ValueKind == JsonValueKind.Number && double.IsFinite(instance.GetDouble()),
        "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
        _ => throw new InvalidDataException($"the schema names the type {type}, which JSON Schema does not have."),
    };

    // A number equals another only as the same text, so that 1.0 does not stand for the 1 of a const, which the
    // app reads as an integer. Other values compare as JSON Schema compares them, and so do the numbers nested
    // in them, by value.
    private static bool Same(JsonElement value, JsonElement expected) =>
        value.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number
            ? value.GetRawText() == expected.GetRawText()
            : ValueKey.Of(value) == ValueKey.Of(expected);

    // Numbers compare exactly, from their text, as JSON Schema compares them.
    private static int Compare(JsonElement value, JsonElement limit) =>
        ExactNumber.Compare(ExactNumber.Parse(value.GetRawText()), ExactNumber.Parse(limit.GetRawText()));

    // RFC 3339's full-date, which format date names: four digits of year, two of month and two of day, for a
    // day that the calendar has.
    private static bool IsDate(string value) =>
        value.Length == 10
        && value[4] == '-'
        && value[7] == '-'
        && value.Where((_, index) => index is not (4 or 7)).All(char.IsAsciiDigit)
        && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

// A JSON number as its text gives it, for an exact comparison: its sign, its significant digits without leading
// and trailing zeros, and the place of the decimal point before the first of them, so that the number is the
// sign times 0.<digits> times ten to the power of the place. An exponent beyond 2^60 in either direction, or
// beyond a long, is read as 2^60 with its sign, so that the place cannot overflow: such a number stands beyond
// every limit, or next to zero, and two such numbers of the same digits compare as equal.
internal readonly record struct ExactNumber(int Sign, string Digits, long Point)
{
    private const long Far = 1L << 60;

    public static ExactNumber Parse(string text)
    {
        var negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;
        var mark = body.IndexOfAny(['e', 'E']);
        var mantissa = mark < 0 ? body : body[..mark];
        long exponent = 0;
        if (mark >= 0
            && !long.TryParse(
                body[(mark + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
        {
            exponent = body[mark + 1] == '-' ? -Far : Far;
        }
        exponent = Math.Clamp(exponent, -Far, Far);
        var dot = mantissa.IndexOf('.');
        var whole = dot < 0 ? mantissa : mantissa[..dot];
        var all = dot < 0 ? whole : whole + mantissa[(dot + 1)..];
        var significant = all.TrimStart('0');
        var digits = significant.TrimEnd('0');
        return digits.Length == 0
            ? new ExactNumber(0, "", 0)
            : new ExactNumber(negative ? -1 : 1, digits, whole.Length - (all.Length - significant.Length) + exponent);
    }

    public static int Compare(ExactNumber left, ExactNumber right)
    {
        if (left.Sign != right.Sign || left.Sign == 0)
        {
            return left.Sign.CompareTo(right.Sign);
        }
        var magnitude = left.Point != right.Point
            ? left.Point.CompareTo(right.Point)
            : Math.Sign(string.CompareOrdinal(left.Digits, right.Digits));
        return left.Sign * magnitude;
    }
}

// A JSON value as a text that two values share where JSON Schema counts them equal: a number by its exact value,
// as ExactNumber reads it, so that two numbers of the same digits beyond its limit of the exponent share one too;
// true, false and null by their kind; a text by its UTF-16 units; a list by the keys of its items, in order; and an
// object by its members, in the ordinal order of their names, each name with its value's key. Each part ends where
// its length or its kind says, so that no two values run together into one key. uniqueItems puts every item
// through one set of these keys, and a const or an enum compares by them.
internal static class ValueKey
{
    public static string Of(JsonElement value)
    {
        var key = new StringBuilder();
        Append(value, key);
        return key.ToString();
    }

    private static void Append(JsonElement value, StringBuilder key)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var members = value.EnumerateObject().OrderBy(member => member.Name, StringComparer.Ordinal).ToList();
                key.Append('{').Append(members.Count.ToString(CultureInfo.InvariantCulture)).Append(':');
                foreach (var member in members)
                {
                    AppendText(member.Name, key);
                    Append(member.Value, key);
                }
                break;
            case JsonValueKind.Array:
                key.Append('[').Append(value.GetArrayLength().ToString(CultureInfo.InvariantCulture)).Append(':');
                foreach (var item in value.EnumerateArray())
                {
                    Append(item, key);
                }
                break;
            case JsonValueKind.String:
                AppendText(value.GetString()!, key);
                break;
            case JsonValueKind.Number:
                var number = ExactNumber.Parse(value.GetRawText());
                key.Append('n').Append(number.Sign switch { < 0 => '-', 0 => '0', _ => '+' }).Append(number.Digits)
                    .Append('e').Append(number.Point.ToString(CultureInfo.InvariantCulture)).Append(';');
                break;
            case JsonValueKind.True:
                key.Append('t');
                break;
            case JsonValueKind.False:
                key.Append('f');
                break;
            default:
                key.Append('z');
                break;
        }
    }

    private static void AppendText(string text, StringBuilder key) =>
        key.Append('s').Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text);
}

// JSON Schema's patterns are ECMA-262 regular expressions, which .NET reads differently in places: its $ also
// matches before a final line break, its . also matches a carriage return and the line and paragraph
// separators, its \s, \d and \w take in other characters than ECMA-262's, and in a class it reads \- as a
// character that cannot start a range. The translation gives each construct its ECMA-262 meaning, and refuses
// a construct whose meaning differs and that the schemas do not use: a word boundary, an escape other than
// those below, \- outside a class, a group other than (?:, (?= and (?!, an empty class, a [ inside a class,
// which .NET reads as a subtraction, \S, \D or \W inside a class, and a range in a class with \s, \d or \w at
// one end, which .NET reads as a range from or to the shorthand's last character.
//
// The meaning is that of ECMA-262 without the u flag, which JSON Schema 2020-12 recommends but does not
// require: a pattern matches by UTF-16 code units, as .NET and the app's reader match, so . or a negated
// class matches half of a character outside the Basic Multilingual Plane. Only such characters read
// differently with the u flag, and no pattern of the schemas gives them another answer with it: none limits how
// many units . or a negated class matches.
internal static class EcmaPattern
{
    // ECMA-262's white space and line terminators, the characters of \s, as escapes for .NET.
    private static readonly string s_space =
        Escapes(0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x20, 0xA0, 0x1680) + Escape(0x2000) + "-" + Escape(0x200A)
        + Escapes(0x2028, 0x2029, 0x202F, 0x205F, 0x3000, 0xFEFF);

    private static readonly string s_lineTerminators = Escapes(0x0A, 0x0D, 0x2028, 0x2029);

    private const string Digit = "0-9";
    private const string Word = "A-Za-z0-9_";

    public static string Translate(string pattern)
    {
        var text = new StringBuilder(pattern.Length + 16);
        var inClass = false;
        var classStart = false;
        // In a class: whether the last part was \s, \d or \w, and whether the last part was a - that makes a range.
        var shorthand = false;
        var range = false;
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '\\')
            {
                if (++index == pattern.Length)
                {
                    throw new ArgumentException("the pattern ends in a backslash");
                }
                var isShorthand = inClass && pattern[index] is 's' or 'd' or 'w';
                if (isShorthand && range)
                {
                    throw new ArgumentException("a range in a class that ends in \\s, \\d or \\w");
                }
                text.Append(TranslateEscape(pattern, ref index, inClass));
                classStart = false;
                shorthand = isShorthand;
                range = false;
                continue;
            }
            if (inClass)
            {
                if (current == ']' && classStart)
                {
                    throw new ArgumentException("an empty class, which .NET reads otherwise");
                }
                if (current == '[')
                {
                    throw new ArgumentException("a [ inside a class, which .NET reads as a subtraction");
                }
                var isRange = current == '-' && !classStart && index + 1 < pattern.Length && pattern[index + 1] != ']';
                if (isRange && shorthand)
                {
                    throw new ArgumentException("a range in a class that starts at \\s, \\d or \\w");
                }
                inClass = current != ']';
                classStart = false;
                shorthand = false;
                range = isRange;
                text.Append(current);
                continue;
            }
            switch (current)
            {
                case '[':
                    inClass = true;
                    classStart = true;
                    text.Append('[');
                    if (index + 1 < pattern.Length && pattern[index + 1] == '^')
                    {
                        text.Append('^');
                        index++;
                    }
                    break;
                case '$':
                    text.Append("\\z");
                    break;
                case '.':
                    text.Append("[^").Append(s_lineTerminators).Append(']');
                    break;
                case '(' when index + 1 < pattern.Length && pattern[index + 1] == '?'
                    && !(index + 2 < pattern.Length && pattern[index + 2] is ':' or '=' or '!'):
                    throw new ArgumentException("a group other than (?:, (?= and (?!");
                default:
                    text.Append(current);
                    break;
            }
        }
        if (inClass)
        {
            throw new ArgumentException("a class that is not closed");
        }
        return text.ToString();
    }

    private static string TranslateEscape(string pattern, ref int index, bool inClass)
    {
        var letter = pattern[index];
        switch (letter)
        {
            case 's':
                return inClass ? s_space : $"[{s_space}]";
            case 'd':
                return inClass ? Digit : $"[{Digit}]";
            case 'w':
                return inClass ? Word : $"[{Word}]";
            case 'S' or 'D' or 'W' when inClass:
                throw new ArgumentException($"the escape \\{letter} inside a class");
            case 'S':
                return $"[^{s_space}]";
            case 'D':
                return $"[^{Digit}]";
            case 'W':
                return $"[^{Word}]";
            case 'u':
                if (index + 4 >= pattern.Length || !pattern.Substring(index + 1, 4).All(char.IsAsciiHexDigit))
                {
                    throw new ArgumentException("an escape of a code unit without four hexadecimal digits");
                }
                index += 4;
                return $"\\u{pattern.Substring(index - 3, 4)}";
            case '-' when inClass:
                // A hyphen that may start a range, as in ECMA-262. Outside a class, the u flag refuses \-, so the
                // translation refuses it there too.
                return Escape('-');
            case 't' or 'n' or 'r' or 'f' or 'v':
                return $"\\{letter}";
            case '^' or '$' or '\\' or '.' or '*' or '+' or '?' or '(' or ')' or '[' or ']' or '{' or '}' or '|' or '/':
                return $"\\{letter}";
            default:
                throw new ArgumentException($"the escape \\{letter}");
        }
    }

    private static string Escape(int code) => "\\u" + code.ToString("x4", CultureInfo.InvariantCulture);

    private static string Escapes(params int[] codes) => string.Concat(codes.Select(Escape));
}

// Reads a file of the compatibility data, a title file or the list of verified iOS builds, the way the app's
// reader reads a title file (docs/design/compat-db.md section 6): at most 1 MiB of UTF-8, with or without a
// byte-order mark, holding one JSON value without comments or trailing commas, with no string or member name
// that escapes half of a surrogate pair and no member twice in one object. A file that breaks one of these
// breaks the json rule, and nothing else of it is checked. A syntax error is named by its line and byte, never
// by the text there, which the message of System.Text.Json would quote.
internal static class JsonText
{
    public const int MaxBytes = 1 << 20;

    private static readonly UTF8Encoding s_utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static JsonDocument? Read(byte[] bytes, string file, List<Problem> problems)
    {
        if (bytes.Length > MaxBytes)
        {
            problems.Add(new Problem(file, "", "json", "the file holds more than 1 MiB, which the app does not read"));
            return null;
        }
        var start = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        string text;
        try
        {
            text = s_utf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            problems.Add(new Problem(file, "", "json", "the file is not UTF-8 text"));
            return null;
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            problems.Add(new Problem(
                file, "", "json",
                "the file is not one JSON value without comments and trailing commas "
                + $"(line {exception.LineNumber + 1}, byte {exception.BytePositionInLine + 1})"));
            return null;
        }
        var count = problems.Count;
        Scan(document.RootElement, "", file, problems);
        if (problems.Count > count)
        {
            document.Dispose();
            return null;
        }
        return document;
    }

    // Finds a member twice, and half of a surrogate pair in a member name or a text, which the loader of the
    // schemas refuses too. The pointer's segment of a member name is made as the data's are, unless the caller
    // gives another rule, as the loader of the schemas does.
    public static void Scan(
        JsonElement element, string pointer, string file, List<Problem> problems, Func<string, string>? segment = null)
    {
        segment ??= JsonPointer.Segment;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var member in element.EnumerateObject())
                {
                    string name;
                    try
                    {
                        name = member.Name;
                    }
                    catch (InvalidOperationException)
                    {
                        problems.Add(new Problem(
                            file, pointer, "json", "a member name escapes half of a surrogate pair"));
                        continue;
                    }
                    var at = $"{pointer}/{segment(name)}";
                    if (!names.Add(name))
                    {
                        problems.Add(new Problem(file, at, "json", "the member appears twice in one object"));
                    }
                    Scan(member.Value, at, file, problems, segment);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, $"{pointer}/{index++}", file, problems, segment);
                }
                break;
            case JsonValueKind.String:
                try
                {
                    _ = element.GetString();
                }
                catch (InvalidOperationException)
                {
                    problems.Add(new Problem(file, pointer, "json", "the text escapes half of a surrogate pair"));
                }
                break;
        }
    }
}

// The checks of the list of verified iOS builds: the schema, then the two rules beyond it.
internal static class IosBuilds
{
    public const string DataPath = "compat/ios-builds.json";
    public const string SchemaPath = "compat/schema/ios-builds.schema.json";

    // The vocabulary of the schema: the keywords and annotations that it uses (SchemaValidator).
    public static readonly IReadOnlySet<string> Vocabulary = new HashSet<string>(StringComparer.Ordinal)
    {
        "$schema", "$id", "title", "description",
        "type", "required", "properties", "additionalProperties", "const", "items", "$ref", "$defs", "pattern",
        "minItems", "uniqueItems",
    };

    // Loads the schema as the check reads it, with its vocabulary.
    public static SchemaValidator LoadSchema(string schemaText) =>
        SchemaValidator.LoadSchema(schemaText, SchemaPath, Vocabulary);

    // The list is read as the app reads a title file (JsonText), so that a member twice, half a surrogate pair or
    // a syntax error is a problem of the data with the json rule, and no message quotes the file.
    public static List<Problem> Check(byte[] data, string schemaText, Func<string, bool> evidenceExists)
    {
        var validator = LoadSchema(schemaText);
        var problems = new List<Problem>();
        using var document = JsonText.Read(data, DataPath, problems);
        if (document is null)
        {
            return problems;
        }
        validator.Validate(document.RootElement, DataPath, problems);
        if (problems.Count == 0)
        {
            CheckRules(document.RootElement, evidenceExists, problems);
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

// The title schema, and what the rules read from it: the model identifiers of the target devices, whose reports
// count for the rating (C-0008 section 3), and the allow-lists of the translator options and the environment
// (C-0008 sections 2 and 5).
internal sealed class TitleSchema
{
    public const string SchemaPath = "compat/schema/title.schema.json";

    // The vocabulary of the schema: the keywords and annotations that it uses (SchemaValidator).
    public static readonly IReadOnlySet<string> Vocabulary = new HashSet<string>(StringComparer.Ordinal)
    {
        "$schema", "title", "description", "$comment", "default",
        "type", "required", "properties", "additionalProperties", "const", "enum", "items", "$ref", "$defs",
        "pattern", "minItems", "uniqueItems", "maxLength", "minimum", "maximum", "exclusiveMinimum", "format",
        "allOf", "anyOf", "oneOf", "not", "if", "then", "else",
    };

    public TitleSchema(string text)
    {
        Validator = SchemaValidator.LoadSchema(text, SchemaPath, Vocabulary);
        var root = Validator.Root;
        var devices = Find(root, "$defs", "targetDevices", "enum");
        if (devices.ValueKind != JsonValueKind.Array
            || devices.EnumerateArray().Any(device => device.ValueKind != JsonValueKind.String))
        {
            throw new InvalidDataException(
                $"{SchemaPath}: /$defs/targetDevices/enum lists a value that is not a model identifier.");
        }
        TargetDevices = devices.EnumerateArray().Select(device => device.GetString()!).ToHashSet(StringComparer.Ordinal);
        if (TargetDevices.Count == 0)
        {
            throw new InvalidDataException($"{SchemaPath}: /$defs/targetDevices/enum lists no device.");
        }
        EnvironmentAllowList = Find(root, "properties", "environment");
        TranslatorAllowList = Find(root, "properties", "translator");
        EnvironmentNames = Names(EnvironmentAllowList);
        TranslatorNames = Names(TranslatorAllowList);
    }

    public SchemaValidator Validator { get; }

    public HashSet<string> TargetDevices { get; }

    // The names that the allow-lists of the environment and the translator options hold (C-0008 section 2).
    public HashSet<string> EnvironmentNames { get; }

    public HashSet<string> TranslatorNames { get; }

    private JsonElement EnvironmentAllowList { get; }

    private JsonElement TranslatorAllowList { get; }

    // An allow-list that is not an object lists no name, and CheckAllowList reports it.
    private static HashSet<string> Names(JsonElement allowList) =>
        allowList.ValueKind == JsonValueKind.Object
        && allowList.TryGetProperty("properties", out var names)
        && names.ValueKind == JsonValueKind.Object
            ? names.EnumerateObject().Select(member => member.Name).ToHashSet(StringComparer.Ordinal)
            : [];

    private static JsonElement Find(JsonElement root, params string[] path)
    {
        var element = root;
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                throw new InvalidDataException($"{SchemaPath}: /{string.Join('/', path)} is missing.");
            }
        }
        return element;
    }

    // C-0008 section 2: the keys of translator and environment come from allow-lists in the schema, so both are
    // closed: the schema lists their names under properties and allows no other. C-0008 section 5: the
    // environment allow-list never holds SteamAppId, SteamGameId, SteamAppPath or another variable that
    // steam_api or a DRM wrapper reads. Windows reads environment names in any letter case, so a name that
    // starts with Steam in any case is refused, as the app's schema test does (compat-db.md section 2).
    public List<Problem> CheckAllowList()
    {
        var problems = new List<Problem>();
        var allowLists = new[] { ("environment", EnvironmentAllowList), ("translator", TranslatorAllowList) };
        foreach (var (name, allowList) in allowLists)
        {
            if (allowList.ValueKind != JsonValueKind.Object
                || !allowList.TryGetProperty("additionalProperties", out var others)
                || others.ValueKind != JsonValueKind.False)
            {
                problems.Add(new Problem(
                    SchemaPath, $"/properties/{name}", "allow-list",
                    $"{name} is an allow-list: the schema names its keys under properties and sets "
                    + "additionalProperties to false (C-0008 section 2)"));
            }
        }
        var steamVariables = EnvironmentNames.Where(IsSteamVariable).Order(StringComparer.Ordinal);
        problems.AddRange(steamVariables.Select(name => new Problem(
            SchemaPath, $"/properties/environment/properties/{JsonPointer.Segment(name)}", "allow-list",
            "the environment allow-list never holds a variable that steam_api or a DRM wrapper reads "
            + "(C-0008 section 5)")));
        return problems;
    }

    public static bool IsSteamVariable(string name) => name.StartsWith("Steam", StringComparison.OrdinalIgnoreCase);
}

// The title files of compat/titles/ (C-0008): each reads as the app reads it, matches the title schema, is named
// after its id, and holds to the rules of C-0008 sections 3 and 5 that the check states in code.
internal static class Titles
{
    public const string FolderPath = "compat/titles";

    // Where the title schema states a rule of C-0008 section 5. A problem found there is that rule's, so that a
    // file that breaks the rule fails it by name, whichever keyword finds it (docs/design/compat-db.md section 5).
    private static readonly (string Location, string Rule, string Why)[] s_ruleLocations =
    [
        ("/allOf/0/", "launch", "as the schema states the launch rule of C-0008 section 5"),
        ("/allOf/1/", "launch", "as the schema states the launch rule of C-0008 section 5"),
        ("/allOf/2/then/properties/", "launch", "as the schema states the launch rule of C-0008 section 5"),
        ("/properties/translator/additionalProperties", "allow-list", "as the name is not on the schema's allow-list "
            + "(C-0008 section 2)"),
        ("/properties/environment/additionalProperties", "allow-list", "as the name is not on the schema's allow-list "
            + "(C-0008 sections 2 and 5)"),
    ];

    private static readonly Regex s_titleName =
        new(@"^(steam-[1-9][0-9]*|local-[a-z0-9]+(-[a-z0-9]+)*)\.json\z", RegexOptions.CultureInvariant);

    private static readonly Regex s_slug = new(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant);

    // Checks every entry of a folder as C-0008 section 1 lays out compat/titles/: one file per title, and the
    // README.md, which is not a title file. A file whose name ends in .json in any letter case is read as a
    // title file, as the app's loader reads it, and must then have its exact name.
    public static (List<Problem> Problems, int Files) CheckFolder(
        string folder, string shownFolder, TitleSchema schema, Func<string, bool> evidenceExists)
    {
        var problems = new List<Problem>();
        foreach (var directory in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal))
        {
            problems.Add(new Problem(
                $"{shownFolder}/{Path.GetFileName(directory)}", "", "file-name",
                "the folder of the title files holds no folder (C-0008 section 1)"));
        }
        var files = 0;
        foreach (var path in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var file = $"{shownFolder}/{name}";
            if (name == "README.md")
            {
                continue;
            }
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(new Problem(
                    file, "", "file-name",
                    "the folder of the title files holds only its README.md and title files (C-0008 section 1)"));
                continue;
            }
            files++;
            problems.AddRange(CheckFile(file, name, File.ReadAllBytes(path), schema, evidenceExists));
        }
        return (problems, files);
    }

    // The schema comes first. The rules in code run only on a file that matches it, because they read its
    // values; the file name is checked in any case, from what the id gives.
    public static List<Problem> CheckFile(
        string file, string name, byte[] bytes, TitleSchema schema, Func<string, bool> evidenceExists)
    {
        var problems = new List<Problem>();
        using var document = JsonText.Read(bytes, file, problems);
        if (document is null)
        {
            return problems;
        }
        var root = document.RootElement;
        var found = new List<Problem>();
        schema.Validator.Validate(root, file, found);
        problems.AddRange(found.Select(Attribute));
        CheckName(file, name, root, problems);
        if (found.Count == 0)
        {
            CheckLaunch(file, root, problems);
            CheckAllowLists(file, root, schema, problems);
            CheckRating(file, root, schema.TargetDevices, problems);
            CheckEvidence(file, root, evidenceExists, problems);
        }
        return problems;
    }

    private static Problem Attribute(Problem problem)
    {
        foreach (var (location, rule, why) in s_ruleLocations)
        {
            if (problem.Location.StartsWith(location, StringComparison.Ordinal))
            {
                return problem with { Rule = rule, Detail = $"{problem.Detail}, {why}" };
            }
        }
        return problem;
    }

    // C-0008 section 1: a Steam title is named steam-<appid>.json and a local title local-<slug>.json. Where the
    // id cannot give the name, because the schema rejects it, the name only has to have one of the two forms.
    private static void CheckName(string file, string name, JsonElement root, List<Problem> problems)
    {
        var expected = ExpectedName(root);
        if (expected is null ? !s_titleName.IsMatch(name) : name != expected)
        {
            problems.Add(new Problem(
                file, expected is null ? "" : "/id", "file-name",
                "a Steam title's file is named steam-<steamAppId>.json and a local title's local-<slug>.json, "
                + "after its id (C-0008 section 1)"));
        }
    }

    private static string? ExpectedName(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (id.TryGetProperty("steamAppId", out var appId))
        {
            return appId.ValueKind == JsonValueKind.Number && appId.TryGetInt64(out var number) && number > 0
                ? $"steam-{number.ToString(CultureInfo.InvariantCulture)}.json"
                : null;
        }
        return id.TryGetProperty("exeSha256", out _)
            && id.TryGetProperty("slug", out var slug)
            && slug.ValueKind == JsonValueKind.String
            && s_slug.IsMatch(slug.GetString()!)
                ? $"local-{slug.GetString()}.json"
                : null;
    }

    // A member of the file, or Undefined where the file lacks it or where what holds it is not an object. The
    // rules in code rely on the types that the schema gives the members they read, and test each type, so that
    // a change of the schema that drops a type makes the rule report the member instead of throwing.
    private static JsonElement Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // C-0008 section 5, stated again in code, so that a change of the schema cannot drop it unnoticed. An entry
    // states direct only where its own drm opens the Direct path and it names the program to start (C-0007
    // section 5); it states dock only where its own drm keeps that path closed, which is narrower than the text
    // of the contract on purpose (docs/design/compat-db.md section 5); and a local title states no other path
    // than direct and shows no Steam marker. A member that the rule reads, of another type than the schema
    // gives it, fails the rule.
    private static void CheckLaunch(string file, JsonElement root, List<Problem> problems)
    {
        void add(string pointer, string detail) => problems.Add(new Problem(file, pointer, "launch", detail));
        var drm = Member(root, "drm");
        var read = new (string Pointer, JsonElement Value, JsonValueKind[] Kinds)[]
        {
            ("/drm", drm, [JsonValueKind.Object]),
            ("/drm/steamApi", Member(drm, "steamApi"), [JsonValueKind.True, JsonValueKind.False]),
            ("/drm/steamStub", Member(drm, "steamStub"), [JsonValueKind.True, JsonValueKind.False]),
            ("/drm/ceg", Member(drm, "ceg"), [JsonValueKind.String]),
            ("/launch", Member(root, "launch"), [JsonValueKind.String]),
            ("/executable", Member(root, "executable"), [JsonValueKind.Object]),
        };
        foreach (var (pointer, value, kinds) in read)
        {
            if (value.ValueKind != JsonValueKind.Undefined && !kinds.Contains(value.ValueKind))
            {
                add(pointer, "the launch rule reads this member as the schema types it, and it has another type "
                    + "(C-0008 section 5)");
            }
        }
        bool? marker(string name) => Member(drm, name).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
        var steamApi = marker("steamApi");
        var steamStub = marker("steamStub");
        var ceg = Text(Member(drm, "ceg"));
        var launch = Text(Member(root, "launch"));
        var local = Member(Member(root, "id"), "exeSha256").ValueKind != JsonValueKind.Undefined;

        if (launch == "direct" && !(steamApi == false && steamStub == false && ceg == "no"))
        {
            add("/launch", "an entry states direct only where its own drm sets steamApi and steamStub to false "
                + "and ceg to no (C-0008 section 5)");
        }
        if (launch == "direct" && Member(root, "executable").ValueKind == JsonValueKind.Undefined)
        {
            add("/launch", "an entry that states direct names the program to start in executable (C-0007 section 5)");
        }
        if (launch == "dock" && drm.ValueKind == JsonValueKind.Object && ceg == "no"
            && steamApi != true && steamStub != true)
        {
            add("/launch", "an entry states dock only where its own drm keeps the Direct path closed: "
                + "steamApi or steamStub true, or ceg other than no (C-0008 section 5)");
        }
        if (local && launch == "dock")
        {
            add("/launch", "a local title is launched only on the Direct path (C-0008 section 5)");
        }
        if (local && (steamApi == true || steamStub == true || ceg == "yes"))
        {
            add("/drm", "a local title shows no Steam marker: steamApi or steamStub true, or ceg yes "
                + "(C-0008 section 5)");
        }
    }

    // C-0008 sections 2 and 5, stated again in code for the same reason: the keys of translator and environment
    // are on the schema's allow-lists, and the environment never holds a variable that steam_api or a DRM
    // wrapper reads.
    private static void CheckAllowLists(string file, JsonElement root, TitleSchema schema, List<Problem> problems)
    {
        var allowLists = new[] { ("translator", schema.TranslatorNames), ("environment", schema.EnvironmentNames) };
        foreach (var (member, allowed) in allowLists)
        {
            var map = Member(root, member);
            if (map.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }
            if (map.ValueKind != JsonValueKind.Object)
            {
                problems.Add(new Problem(
                    file, $"/{member}", "allow-list",
                    $"{member} is an object of names on the schema's allow-list (C-0008 section 2)"));
                continue;
            }
            foreach (var name in map.EnumerateObject().Select(key => key.Name))
            {
                if (!allowed.Contains(name))
                {
                    problems.Add(new Problem(
                        file, $"/{member}/{JsonPointer.Segment(name)}", "allow-list",
                        $"the name is not on the schema's allow-list of {member} (C-0008 section 2)"));
                }
                else if (member == "environment" && TitleSchema.IsSteamVariable(name))
                {
                    problems.Add(new Problem(
                        file, $"/environment/{JsonPointer.Segment(name)}", "allow-list",
                        "the environment never holds a variable that steam_api or a DRM wrapper reads "
                        + "(C-0008 section 5)"));
                }
            }
        }
    }

    // C-0008 section 3: the rating is computed from the reports, and a file whose rating differs is rejected. A
    // file without a rating has the schema's default, untested. A report whose members the rating cannot read
    // fails the rule, and does not count.
    private static void CheckRating(
        string file, JsonElement root, HashSet<string> targetDevices, List<Problem> problems)
    {
        void add(string pointer, string detail) => problems.Add(new Problem(file, pointer, "rating", detail));
        var list = Member(root, "reports");
        var reports = new List<TestReport>();
        if (list.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var report in list.EnumerateArray())
            {
                var (date, device, version, said) = (Text(Member(report, "date")), Text(Member(report, "device")),
                    Text(Member(report, "paneportVersion")), Text(Member(report, "rating")));
                if (date is null || device is null || version is null || said is null || !Rating.IsObserved(said))
                {
                    add($"/reports/{index}", "the rating reads the texts date, device, paneportVersion and rating "
                        + "of each report, whose rating is one of broken, boots, runs, playable and perfect "
                        + "(C-0008 sections 3 and 4)");
                }
                else
                {
                    reports.Add(new TestReport(index, date, device, version, said));
                }
                index++;
            }
        }
        else if (list.ValueKind != JsonValueKind.Undefined)
        {
            add("/reports", "the rating reads reports as a list (C-0008 section 3)");
        }
        var computed = Rating.Compute(reports, targetDevices);
        var rating = Member(root, "rating");
        var stated = rating.ValueKind == JsonValueKind.Undefined ? Rating.Untested : Text(rating);
        if (stated != computed)
        {
            problems.Add(new Problem(
                file, "/rating", "rating",
                $"the rating that the reports give is {computed} (C-0008 section 3); "
                + "the rating is computed, not edited"));
        }
    }

    // Every report names its evidence record (C-0008 sections 4 and 5), which must exist (C-0012 section 4).
    private static void CheckEvidence(
        string file, JsonElement root, Func<string, bool> evidenceExists, List<Problem> problems)
    {
        // Reports of another type than a list fail the rating rule.
        var reports = Member(root, "reports");
        if (reports.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        var index = 0;
        foreach (var report in reports.EnumerateArray())
        {
            var evidence = Text(Member(report, "evidence"));
            if (evidence is null || !evidenceExists(evidence))
            {
                problems.Add(new Problem(
                    file, $"/reports/{index}/evidence", "evidence-file",
                    evidence is null
                        ? "the report names its evidence record by its path (C-0008 section 4)"
                        : "the evidence record does not exist in the repository"));
            }
            index++;
        }
    }
}

// What the rating reads of a report, with its place in the file's list.
internal sealed record TestReport(int Index, string Date, string Device, string Version, string Rating);

// The rating of C-0008 section 3, computed from the reports.
internal static class Rating
{
    public const string Untested = "untested";

    // The observed ratings, from the lowest to the highest.
    private static readonly string[] s_order = ["broken", "boots", "runs", "playable", "perfect"];

    // The target devices of C-0001 section 2 by their model identifiers, as the table of
    // docs/design/compat-db.md section 2.1 gives them. The self-test compares this map with the table and with
    // the schema's $defs/targetDevices.
    public static readonly IReadOnlyDictionary<string, string> Devices =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["iPhone18,1"] = "iPhone 17 Pro",
            ["iPhone18,2"] = "iPhone 17 Pro Max",
            ["iPhone19,2"] = "iPhone 18 Pro",
            ["iPhone19,3"] = "iPhone 18 Pro Max",
            ["iPhone19,7"] = "iPhone 18 Pro Max",
        };

    public static bool IsObserved(string rating) => s_order.Contains(rating);

    // 1. Only reports whose paneportVersion is a release tag and whose device is a target device count.
    // 2. Among those, the reports for the highest release count.
    // 3. If target devices disagree on that release, the lower rating wins; otherwise the latest report wins.
    //
    // A target device is one of the four devices of C-0001 section 2, which C-0008 section 3 counts: a report's
    // model identifier names its device through Devices, so the reports of the two identifiers of the iPhone 18
    // Pro Max are those of one device. What a device says on the release is its latest report for it: the one
    // with the latest date, and of two on one date the later in the list, since a new report is added at its
    // end. The devices disagree when what they say differs. The rating is then the lowest of what they say,
    // which is also the latest report's rating when they agree (docs/design/compat-db.md section 2.2).
    public static string Compute(IEnumerable<TestReport> reports, HashSet<string> targetDevices)
    {
        var counting = reports
            .Where(report => targetDevices.Contains(report.Device))
            .Select(report => (
                Report: report,
                Release: ReleaseVersion.TryParse(report.Version, out var release) ? release : null))
            .Where(item => item.Release is not null)
            .ToList();
        if (counting.Count == 0)
        {
            return Untested;
        }
        var highest = counting.Select(item => item.Release!).Max()!;
        return counting
            .Where(item => item.Release!.CompareTo(highest) == 0)
            .GroupBy(
                item => Devices.TryGetValue(item.Report.Device, out var device) ? device : item.Report.Device,
                StringComparer.Ordinal)
            .Select(device => device
                .Select(item => item.Report)
                .OrderBy(report => report.Date, StringComparer.Ordinal)
                .ThenBy(report => report.Index)
                .Last())
            .MinBy(report => Array.IndexOf(s_order, report.Rating))!
            .Rating;
    }
}

// A release tag: v and a Semantic Version without build metadata, as the title schema's pattern has it (C-0009
// section 6), ordered by the precedence of Semantic Versioning 2.0.0, its section 11. A pre-release, such as
// v0.1.0-rc.1, is a release tag too, and comes before its release.
internal sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex s_tag = new(
        @"^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z",
        RegexOptions.CultureInvariant);

    private readonly string[] _numbers;
    private readonly string[] _preRelease;

    private ReleaseVersion(string[] numbers, string[] preRelease)
    {
        _numbers = numbers;
        _preRelease = preRelease;
    }

    // A numeric identifier of a pre-release has no leading zero (Semantic Versioning 2.0.0, item 9), so a tag such
    // as v1.0.0-01, which the schema's pattern lets through, is no release tag, and its report does not count.
    public static bool TryParse(string text, [NotNullWhen(true)] out ReleaseVersion? version)
    {
        var match = s_tag.Match(text);
        string[] preRelease = match.Groups[4].Success ? match.Groups[4].Value[1..].Split('.') : [];
        var leadingZero = preRelease.Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit));
        version = match.Success && !leadingZero
            ? new ReleaseVersion([match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], preRelease)
            : null;
        return version is not null;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }
        for (var index = 0; index < 3; index++)
        {
            var order = CompareNumbers(_numbers[index], other._numbers[index]);
            if (order != 0)
            {
                return order;
            }
        }
        // A release comes after each of its pre-releases.
        if (_preRelease.Length == 0 || other._preRelease.Length == 0)
        {
            return other._preRelease.Length.CompareTo(0) - _preRelease.Length.CompareTo(0);
        }
        for (var index = 0; index < Math.Min(_preRelease.Length, other._preRelease.Length); index++)
        {
            var order = CompareIdentifiers(_preRelease[index], other._preRelease[index]);
            if (order != 0)
            {
                return order;
            }
        }
        return _preRelease.Length.CompareTo(other._preRelease.Length);
    }

    // Identifiers of digits only compare as numbers and come before the others, which compare in ASCII order.
    private static int CompareIdentifiers(string left, string right)
    {
        var leftNumber = left.All(char.IsAsciiDigit);
        var rightNumber = right.All(char.IsAsciiDigit);
        return (leftNumber, rightNumber) switch
        {
            (true, true) => CompareNumbers(left, right),
            (true, false) => -1,
            (false, true) => 1,
            _ => Math.Sign(string.CompareOrdinal(left, right)),
        };
    }

    // Numbers of any length: without leading zeros, the longer is the larger.
    private static int CompareNumbers(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        return left.Length != right.Length
            ? left.Length.CompareTo(right.Length)
            : Math.Sign(string.CompareOrdinal(left, right));
    }
}

internal static class CompatCheck
{
    public static int Run()
    {
        var root = Git.TopLevel();
        bool evidenceExists(string path) => File.Exists(Path.Combine(root, path));
        var problems = IosBuilds.Check(
            File.ReadAllBytes(Path.Combine(root, IosBuilds.DataPath)),
            File.ReadAllText(Path.Combine(root, IosBuilds.SchemaPath)),
            evidenceExists);
        var titleSchema = new TitleSchema(File.ReadAllText(Path.Combine(root, TitleSchema.SchemaPath)));
        problems.AddRange(titleSchema.CheckAllowList());
        var (titleProblems, titleFiles) = Titles.CheckFolder(
            Path.Combine(root, Titles.FolderPath), Titles.FolderPath, titleSchema, evidenceExists);
        problems.AddRange(titleProblems);
        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        foreach (var problem in problems)
        {
            Console.WriteLine(annotate
                ? $"::error file={Annotation(problem.File, true)},title=Compatibility data::"
                    + Annotation(problem.ToString(), false)
                : problem.ToString());
        }
        if (problems.Count > 0)
        {
            Console.WriteLine($"{problems.Count} problem(s) in the compatibility data.");
            return 1;
        }
        Console.WriteLine($"{IosBuilds.DataPath} matches {IosBuilds.SchemaPath}, and its rules hold.");
        Console.WriteLine(
            $"{Titles.FolderPath}: {titleFiles} title file(s); each matches {TitleSchema.SchemaPath}, "
            + "is named after its id, and holds to the launch, allow-list, rating and evidence rules.");
        return 0;
    }

    // A workflow command ends at a line break, so its message escapes them, and a property also escapes , and :.
    private static string Annotation(string value, bool property)
    {
        var escaped = value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
        return property
            ? escaped.Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal)
            : escaped;
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

// Counts the self-test's cases and prints each failure.
internal sealed class Tally
{
    public int Cases { get; private set; }

    public int Failures { get; private set; }

    public void Expect(bool passed, string name, string found)
    {
        Cases++;
        if (!passed)
        {
            Failures++;
            Console.WriteLine($"FAIL {name}: {found}");
        }
    }
}

// Each fixture breaks exactly one rule, which the check must name, and the valid fixtures pass.
internal static class SelfTest
{
    private const string Record = "docs/evidence/2026-12-01-l3-jit.md";
    private const string GateRecord = "docs/evidence/2026-12-20-phase-1-gate.md";

    // How a reference names a definition, as the validator reads it.
    private const string DefinitionPrefix = "#/$defs/";

    private const string Entry =
        """{ "iosVersion": "26.1", "iosBuild": "23B85", "deviceModel": "iPhone18,1", "evidence": ["docs/evidence/2026-12-01-l3-jit.md"] }""";

    // A device name, which a fixture of the list puts where a problem might show it, and which no problem shows.
    private const string DeviceName = "The test iPhone";

    // The JSON escape of the first half of a surrogate pair, U+D800.
    private const string HalfPair = "\\" + "ud800";

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
        ("half a surrogate pair", Doc(Entry.Replace("\"26.1\"", "\"26.1\\ud800\"", StringComparison.Ordinal)),
            "json"),
        ("a syntax error before a device name", $$"""{ "schemaVersion": 1, "builds": [ tr, "{{DeviceName}}" ] }""",
            "json"),
    ];

    // Schemas that the check refuses in place of the list's schema, each with the part of the refusal that
    // names its place: an unknown keyword anywhere, a keyword outside the schema's vocabulary, which the title
    // schema may use, a keyword's value in a form that the validator does not implement, an annotation that
    // breaks its rules, and a member that appears twice, which names the file.
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

    // The fixtures of the title files are in tests/compat (docs/design/repository-checks.md section 9). The
    // evidence records that their reports name exist for the self-test only.
    private const string Fixtures = "tests/compat";

    private static readonly HashSet<string> s_fixtureRecords =
    [
        "docs/evidence/2026-12-01-fixture-first-run.md",
        "docs/evidence/2026-12-08-fixture-second-run.md",
        "docs/evidence/2026-12-15-fixture-third-run.md",
    ];

    // Each folder of tests/compat/invalid holds one title file that breaks one rule, and the check must find that
    // rule and no other (S8.3.4). A rule of the schema is named by its keyword.
    private static readonly (string Case, string Rule)[] s_invalidTitles =
    [
        ("schema-architecture-arm64", "enum"),
        ("schema-render-scale-above-one", "maximum"),
        ("schema-report-on-february-30", "format"),
        ("schema-missing-name", "required"),
        ("schema-unknown-member", "additionalProperties"),
        ("schema-both-ids", "oneOf"),
        ("schema-path-to-parent-folder", "not"),
        ("schema-integer-with-fraction", "type"),
        ("schema-version-without-v", "anyOf"),
        ("schema-runs-without-frame-rates", "required"),
        ("json-member-twice", "json"),
        ("json-comment", "json"),
        ("json-half-a-surrogate-pair", "json"),
        ("file-name-of-another-app-id", "file-name"),
        ("file-name-of-another-slug", "file-name"),
        ("file-name-steam-title-named-as-local", "file-name"),
        ("file-name-app-id-with-leading-zero", "file-name"),
        ("launch-direct-with-steam-api", "launch"),
        ("launch-direct-with-steam-stub", "launch"),
        ("launch-direct-with-ceg-unknown", "launch"),
        ("launch-direct-without-ceg", "launch"),
        ("launch-direct-without-steam-api", "launch"),
        ("launch-direct-without-steam-stub", "launch"),
        ("launch-direct-without-executable", "launch"),
        ("launch-dock-where-direct-is-open", "launch"),
        ("launch-local-with-steam-api", "launch"),
        ("launch-local-with-steam-stub", "launch"),
        ("launch-local-with-ceg-yes", "launch"),
        ("launch-local-on-the-dock-path", "launch"),
        ("allow-list-environment-steam-app-id", "allow-list"),
        ("allow-list-environment-name-not-listed", "allow-list"),
        ("allow-list-translator-option-not-listed", "allow-list"),
        ("rating-higher-of-a-tie", "rating"),
        ("rating-earlier-report-of-a-device", "rating"),
        ("rating-earlier-report-of-one-day", "rating"),
        ("rating-lower-release", "rating"),
        ("rating-pre-release-over-release", "rating"),
        ("rating-commit-report-counted", "rating"),
        ("rating-other-device-counted", "rating"),
        ("rating-left-out", "rating"),
        ("evidence-record-missing", "evidence-file"),
    ];

    private const string IfThenElse =
        """{ "if": { "minimum": 10 }, "then": { "maximum": 20 }, "else": { "maximum": 5 } }""";

    private const string RefWithSibling = """{ "$ref": "#/$defs/a", "minimum": 5, "$defs": { "a": { "maximum": 10 } } }""";

    // A schema that refers to itself through properties, so that each step goes one level into the value.
    private const string Tree = """
        { "$ref": "#/$defs/node",
          "$defs": { "node": { "properties": { "child": { "$ref": "#/$defs/node" } }, "additionalProperties": false } } }
        """;

    // A text that the pattern ^(a+)+$ cannot finish matching within its timeout of 1 s.
    private static readonly string s_slowText = $"\"{new string('a', 40)}!\"";

    // Patterns that time out below an applicator: the problem names the value's own pointer and the pattern's
    // place in the schema, as it does where the pattern is reached directly.
    private static readonly (string Name, string Schema, string Instance, string Pointer, string Location)[]
        s_timeoutPlaces =
    [
        ("below not, in a property", """{ "properties": { "a": { "not": { "pattern": "^(a+)+$" } } } }""",
            $$"""{ "a": {{s_slowText}} }""", "/a", "/properties/a/not/pattern"),
        ("in an item below anyOf", """{ "properties": { "list": { "anyOf": [{ "items": { "pattern": "^(a+)+$" } }] } } }""",
            $$"""{ "list": ["x", {{s_slowText}}] }""", "/list/1", "/properties/list/anyOf/0/items/pattern"),
        ("in a oneOf branch", """{ "properties": { "a": { "oneOf": [{ "pattern": "^(a+)+$" }, { "type": "number" }] } } }""",
            $$"""{ "a": {{s_slowText}} }""", "/a", "/properties/a/oneOf/0/pattern"),
        ("in an if condition", """{ "properties": { "a": { "if": { "pattern": "^(a+)+$" }, "then": true } } }""",
            $$"""{ "a": {{s_slowText}} }""", "/a", "/properties/a/if/pattern"),
    ];

    // Schemas that the validator must load with everything that it implements: a schema of true or false, and
    // references that reach one definition twice in place without a cycle.
    private static readonly (string Name, string Schema)[] s_loadedSchemas =
    [
        ("a schema of true", "true"),
        ("a schema of false", "false"),
        ("two definitions that share a third in place", """
            { "$defs": { "a": { "$ref": "#/$defs/c" }, "b": { "allOf": [{ "$ref": "#/$defs/c" }] }, "c": { "type": "string" } } }
            """),
        ("a diamond of references in place", """
            { "$ref": "#/$defs/top",
              "$defs": { "top": { "anyOf": [{ "$ref": "#/$defs/left" }, { "$ref": "#/$defs/right" }] },
                "left": { "$ref": "#/$defs/bottom" }, "right": { "not": { "$ref": "#/$defs/bottom" } },
                "bottom": { "minimum": 0 } } }
            """),
    ];

    // Cases of the validator's keywords: a schema, a value, and the rule it must find, or none.
    private static readonly (string Name, string Schema, string Instance, string? Rule)[] s_keywordCases =
    [
        ("a number under minimum", """{ "minimum": 1 }""", "0", "minimum"),
        ("a number at minimum", """{ "minimum": 1 }""", "1", null),
        ("a number just over maximum, compared exactly", """{ "maximum": 1 }""", "1.0000000000000001", "maximum"),
        ("a number at an exclusive minimum", """{ "exclusiveMinimum": 0 }""", "0", "exclusiveMinimum"),
        ("a number over an exclusive minimum", """{ "exclusiveMinimum": 0 }""", "0.5", null),
        ("a number next to zero, over an exclusive minimum", """{ "exclusiveMinimum": 0 }""", "1e-29", null),
        ("a number past the 28th decimal, over maximum", """{ "maximum": 1 }""", "1.00000000000000000000000000001",
            "maximum"),
        ("a number past the 28th decimal, under minimum", """{ "minimum": 1 }""", "0.99999999999999999999999999999",
            "minimum"),
        ("a number with an exponent, at maximum", """{ "maximum": 500 }""", "5e2", null),
        ("leading zeros and an exponent, under maximum", """{ "maximum": 8 }""", "0.0007e4", null),
        ("leading zeros and an exponent, over minimum", """{ "minimum": 8 }""", "0.0009e4", null),
        ("a negative number under minimum", """{ "minimum": -1 }""", "-1.5", "minimum"),
        ("an exponent too long for a long", """{ "maximum": 1 }""", "1e99999999999999999999", "maximum"),
        ("an exponent too long for a long, next to zero, under minimum", """{ "minimum": 1 }""",
            "1e-99999999999999999999", "minimum"),
        ("an exponent too long for a long, next to zero, at an exclusive minimum of 0",
            """{ "exclusiveMinimum": 0 }""", "1e-99999999999999999999", "exclusiveMinimum"),
        ("a number too small for a double, at an exclusive minimum of 0", """{ "exclusiveMinimum": 0 }""", "1e-400",
            "exclusiveMinimum"),
        ("the largest exponent of a long, over maximum", """{ "maximum": 1 }""", "1e9223372036854775807", "maximum"),
        ("the smallest exponent of a long, under minimum", """{ "minimum": 1 }""", "0.01e-9223372036854775808",
            "minimum"),
        ("trailing zeros, at maximum", """{ "maximum": 1 }""", "1.000", null),
        ("two characters outside the BMP, at maxLength 2", """{ "maxLength": 2 }""",
            "\"\\ud83d\\ude00\\ud83d\\ude00\"", null),
        ("three characters, over maxLength 2", """{ "maxLength": 2 }""", "\"abc\"", "maxLength"),
        ("an integer with a fraction", """{ "type": "integer" }""", "512.0", "type"),
        ("an integer with an exponent", """{ "type": "integer" }""", "5e2", "type"),
        ("an integer", """{ "type": "integer" }""", "512", null),
        ("a number too large to read", """{ "type": "number" }""", "1e400", "type"),
        ("1.0 for a const of 1", """{ "const": 1 }""", "1.0", "const"),
        ("an escaped letter for an enum of the letter", """{ "enum": ["a"] }""", "\"\\u0061\"", null),
        ("allOf, its second part broken", """{ "allOf": [{ "minimum": 1 }, { "maximum": 3 }] }""", "5", "maximum"),
        ("anyOf, no part matched", """{ "anyOf": [{ "const": "a" }, { "const": "b" }] }""", "\"c\"", "anyOf"),
        ("anyOf, one part matched", """{ "anyOf": [{ "const": "a" }, { "const": "b" }] }""", "\"b\"", null),
        ("oneOf, both parts matched", """{ "oneOf": [{ "minimum": 1 }, { "maximum": 5 }] }""", "3", "oneOf"),
        ("oneOf, one part matched", """{ "oneOf": [{ "minimum": 1 }, { "maximum": 5 }] }""", "7", null),
        ("not, its part matched", """{ "not": { "const": "x" } }""", "\"x\"", "not"),
        ("if and then", IfThenElse, "25", "maximum"),
        ("if and else", IfThenElse, "7", "maximum"),
        ("if, else passed", IfThenElse, "3", null),
        ("a failed if, never reported", """{ "if": { "type": "string" } }""", "5", null),
        ("a leap day", """{ "format": "date" }""", "\"2024-02-29\"", null),
        ("the 30th of February", """{ "format": "date" }""", "\"2026-02-30\"", "format"),
        ("a month of one digit", """{ "format": "date" }""", "\"2026-2-01\"", "format"),
        ("the year 0000, which the reader rejects", """{ "format": "date" }""", "\"0000-01-01\"", "format"),
        ("the year 0001", """{ "format": "date" }""", "\"0001-01-01\"", null),
        ("a text before a final line break, against $", """{ "pattern": "^a$" }""", "\"a\\n\"", "pattern"),
        ("a text before a final line break, against $ in an alternative", """{ "pattern": "^a$|^b$" }""",
            "\"b\\n\"", "pattern"),
        ("a carriage return, against .", """{ "pattern": "^.$" }""", "\"\\r\"", "pattern"),
        ("U+0085, against \\s", """{ "pattern": "^\\s$" }""", "\"\\u0085\"", "pattern"),
        ("U+FEFF, against \\s", """{ "pattern": "^\\s$" }""", "\"\\ufeff\"", null),
        ("U+00A0, against \\s", """{ "pattern": "^\\s$" }""", "\"\\u00a0\"", null),
        ("an Arabic-Indic digit, against \\d", """{ "pattern": "^\\d$" }""", "\"\\u0660\"", "pattern"),
        ("a letter with an accent, against \\w", """{ "pattern": "^\\w$" }""", "\"\\u00e9\"", "pattern"),
        ("U+FEFF, against \\S", """{ "pattern": "^\\S$" }""", $"\"{(char)0xFEFF}\"", "pattern"),
        ("an Arabic-Indic digit, against \\D", """{ "pattern": "^\\D$" }""", $"\"{(char)0x0660}\"", null),
        ("a letter with an accent, against \\W", """{ "pattern": "^\\W$" }""", $"\"{(char)0x00E9}\"", null),
        ("an escaped hyphen that starts a range in a class", """{ "pattern": "^[\\--/]$" }""", "\".\"", null),
        ("U+1F600, against . by UTF-16 units", """{ "pattern": "^.$" }""", $"\"{char.ConvertFromUtf32(0x1F600)}\"",
            "pattern"),
        ("a pattern that does not finish within its timeout", """{ "pattern": "^(a+)+$" }""", s_slowText, "pattern"),
        ("a pattern that does not finish within its timeout, below not", """{ "not": { "pattern": "^(a+)+$" } }""",
            s_slowText, "pattern"),
        ("an escaped hyphen between two characters of a class, against a letter between them",
            """{ "pattern": "^[a\\-z]$" }""", "\"m\"", "pattern"),
        ("an escaped hyphen between two characters of a class, against a hyphen", """{ "pattern": "^[a\\-z]$" }""",
            "\"-\"", null),
        ("an item twice", """{ "uniqueItems": true }""", "[\"a\", \"a\"]", "uniqueItems"),
        ("a number twice, a text between", """{ "uniqueItems": true }""", "[1, \"1\", 1]", "uniqueItems"),
        ("a number twice, written two ways", """{ "uniqueItems": true }""", "[1, 1.0]", "uniqueItems"),
        ("true twice", """{ "uniqueItems": true }""", "[true, false, true]", "uniqueItems"),
        ("true and false", """{ "uniqueItems": true }""", "[true, false]", null),
        ("two numbers of one double", """{ "uniqueItems": true }""", "[0.1, 0.10000000000000001]", null),
        ("two numbers too large for a double", """{ "uniqueItems": true }""", "[1e400, 1e401]", null),
        ("an object twice", """{ "uniqueItems": true }""", """[{ "a": 1 }, { "a": 1 }]""", "uniqueItems"),
        ("an object twice, its members in another order", """{ "uniqueItems": true }""",
            """[{ "a": 1, "b": 2 }, { "b": 2, "a": 1 }]""", "uniqueItems"),
        ("a list twice, its number written two ways", """{ "uniqueItems": true }""", "[[1], [1.0]]", "uniqueItems"),
        ("two lists of the same items in another order", """{ "uniqueItems": true }""", "[[1, 2], [2, 1]]", null),
        ("two objects whose names and texts would run together alike", """{ "uniqueItems": true }""",
            """[{ "a": "bsc" }, { "asb": "c" }]""", null),
        ("lists of zero and of a number past every limit", """{ "uniqueItems": true }""",
            "[[0], [1e99999999999999999999]]", null),
        ("objects of a number next to zero and of zero", """{ "uniqueItems": true }""",
            """[{ "a": 1e-99999999999999999999 }, { "a": 0 }]""", null),
        ("a const list of a number past every limit, against itself", """{ "const": [1e99999999999999999999] }""",
            "[1e99999999999999999999]", null),
        ("a const list of a number past every limit, against a list of zero",
            """{ "const": [1e99999999999999999999] }""", "[0]", "const"),
        ("two texts that differ in letter case", """{ "uniqueItems": true }""", "[\"a\", \"A\"]", null),
        ("a - after \\w at the end of a class", """{ "pattern": "^[\\w-]$" }""", "\"-\"", null),
        ("a missing member", """{ "required": ["a"] }""", "{}", "required"),
        ("a number for type null", """{ "type": "null" }""", "1", "type"),
        ("null for type null", """{ "type": "null" }""", "null", null),
        ("additionalProperties as a schema", """{ "additionalProperties": { "type": "string" } }""", """{ "a": 1 }""",
            "type"),
        ("a reference beside another keyword, the other broken", RefWithSibling, "3", "minimum"),
        ("a reference beside another keyword, the reference broken", RefWithSibling, "11", "maximum"),
        ("a reference that steps into the value", Tree, """{ "child": { "child": {} } }""", null),
        ("a reference that steps into the value, deeper broken", Tree, """{ "child": { "other": 1 } }""",
            "additionalProperties"),
    ];

    // Schemas that the validator must refuse with everything that it implements, and the place in the schema that
    // its message must name.
    private static readonly (string Name, string Schema, string Place)[] s_refusedSchemas =
    [
        ("a keyword that is not implemented", """{ "type": "object", "properties": { "a": { "multipleOf": 2 } } }""",
            "/properties/a/multipleOf"),
        ("a format other than date", """{ "format": "email" }""", "/format"),
        ("a reference outside $defs", """{ "$ref": "https://example.com/schema" }""", "/$ref"),
        ("a reference to a definition that does not exist", """{ "$ref": "#/$defs/missing" }""", "/$ref"),
        ("a list of types", """{ "type": ["string", "null"] }""", "/type"),
        ("a word boundary in a pattern", """{ "pattern": "\\bword" }""", "/pattern"),
        ("an empty class in a pattern", """{ "pattern": "[]" }""", "/pattern"),
        ("inline options in a pattern", """{ "pattern": "(?i)a" }""", "/pattern"),
        ("a class subtraction in a pattern", """{ "pattern": "[a-z-[aeiou]]" }""", "/pattern"),
        ("\\S inside a class", """{ "pattern": "[\\S]" }""", "/pattern"),
        ("a range that starts at \\w", """{ "pattern": "[\\w-z]" }""", "/pattern"),
        ("a range that ends in \\d", """{ "pattern": "[!-\\d]" }""", "/pattern"),
        ("an $id inside the schema", """{ "properties": { "a": { "$id": "https://example.com/a" } } }""",
            "/properties/a/$id"),
        ("a $comment that is not text", """{ "not": { "$comment": 5 } }""", "/not/$comment"),
        ("a keyword that is not implemented, in anyOf", """{ "anyOf": [{ "minLength": 1 }] }""", "/anyOf/0/minLength"),
        ("a keyword that is not implemented, in oneOf", """{ "oneOf": [{ "minLength": 1 }] }""", "/oneOf/0/minLength"),
        ("a keyword that is not implemented, in if", """{ "if": { "minLength": 1 } }""", "/if/minLength"),
        ("a keyword that is not implemented, in then", """{ "then": { "minLength": 1 } }""", "/then/minLength"),
        ("a keyword that is not implemented, in else", """{ "else": { "minLength": 1 } }""", "/else/minLength"),
        ("an enum that is not a list", """{ "enum": "a" }""", "/enum"),
        ("a minimum that is not a number", """{ "minimum": "1" }""", "/minimum"),
        ("a maxLength below zero", """{ "maxLength": -1 }""", "/maxLength"),
        ("allOf that is not a list", """{ "allOf": {} }""", "/allOf"),
        ("an empty anyOf", """{ "anyOf": [] }""", "/anyOf"),
        ("half a surrogate pair in a pattern", $$"""{ "pattern": "{{HalfPair}}" }""", "/pattern"),
        ("half a surrogate pair in a required name", $$"""{ "required": ["{{HalfPair}}"] }""", "/required/0"),
        ("half a surrogate pair in a member name", $$"""{ "properties": { "{{HalfPair}}": true } }""", "/properties"),
        ("a reference to itself", """{ "$ref": "#/$defs/a", "$defs": { "a": { "$ref": "#/$defs/a" } } }""",
            "/$defs/a/$ref"),
        ("references that lead back through allOf and not",
            """{ "$defs": { "a": { "allOf": [{ "$ref": "#/$defs/b" }] }, "b": { "not": { "$ref": "#/$defs/a" } } } }""",
            "/$defs/b/not/$ref"),
        ("a reference that leads back through anyOf", """{ "$defs": { "a": { "anyOf": [{ "$ref": "#/$defs/a" }] } } }""",
            "/$defs/a/anyOf/0/$ref"),
        ("a reference that leads back through oneOf", """{ "$defs": { "a": { "oneOf": [{ "$ref": "#/$defs/a" }] } } }""",
            "/$defs/a/oneOf/0/$ref"),
        ("a reference that leads back through if", """{ "$defs": { "a": { "if": { "$ref": "#/$defs/a" } } } }""",
            "/$defs/a/if/$ref"),
        ("a reference that leads back through then",
            """{ "$defs": { "a": { "if": true, "then": { "$ref": "#/$defs/a" } } } }""", "/$defs/a/then/$ref"),
        ("a reference that leads back through else",
            """{ "$defs": { "a": { "if": false, "else": { "$ref": "#/$defs/a" } } } }""", "/$defs/a/else/$ref"),
        ("half a surrogate pair in a pattern of a definition", $$"""{ "$defs": { "a": { "pattern": "{{HalfPair}}" } } }""",
            "/$defs/a/pattern"),
        ("an escaped hyphen outside a class in a pattern, which the u flag refuses", """{ "pattern": "^a\\-b$" }""",
            "/pattern"),
        ("a named group in a pattern", """{ "pattern": "(?<n>a)" }""", "/pattern"),
        ("an atomic group in a pattern", """{ "pattern": "(?>a)" }""", "/pattern"),
        ("a back-reference in a pattern", """{ "pattern": "(a)\\1" }""", "/pattern"),
        ("a property escape in a pattern", """{ "pattern": "\\p{L}" }""", "/pattern"),
        ("a hexadecimal escape in a pattern", """{ "pattern": "\\x41" }""", "/pattern"),
        ("an empty class before a ] in a pattern", """{ "pattern": "[]a]" }""", "/pattern"),
    ];

    // Release tags in ascending order of precedence, the chain of Semantic Versioning 2.0.0 section 11 among them.
    private static readonly string[] s_releaseOrder =
    [
        "v0.0.9", "v0.1.0", "v0.9.0", "v0.10.0", "v1.0.0-0a", "v1.0.0-RC.1", "v1.0.0-alpha", "v1.0.0-alpha.1",
        "v1.0.0-alpha.beta", "v1.0.0-beta", "v1.0.0-beta.2", "v1.0.0-beta.11", "v1.0.0-rc.1", "v1.0.0", "v2.0.0",
        "v2.1.0", "v2.1.1", "v10.0.0", "v99999999999999999999.0.0", "v100000000000000000000.0.0",
    ];

    private static readonly Regex s_modelIdentifier = new("`(iPhone[0-9]+,[0-9]+)`", RegexOptions.CultureInvariant);

    private static readonly string[] s_notReleaseTags =
        [
            "0.1.0", "v0.1", "v01.0.0", "v1.0.0+build.5", "v1.0.0-", "v1.0.0-01", "v1.0.0-rc.01",
            "3a44fa2c92a67a2d0c3223bf247361c77937be76",
        ];

    public static int Run()
    {
        var root = Git.TopLevel();
        var tally = new Tally();
        CheckIosBuilds(root, tally);
        CheckPlantedSchemas(tally);
        CheckKeywords(tally);
        CheckReading(tally);
        CheckReleases(tally);
        var schemaText = File.ReadAllText(Path.Combine(root, TitleSchema.SchemaPath));
        var schema = new TitleSchema(schemaText);
        tally.Expect(
            schema.CheckAllowList().Count == 0,
            "the environment allow-list of the title schema",
            "a Steam variable");
        CheckVocabularies(root, schema, tally);
        CheckStricterReadings(root, tally);
        CheckTargetDevices(root, schema, tally);
        CheckTitleFixtures(root, schema, tally);
        CheckFolderLayout(schema, tally);
        CheckLoaderFixtures(root, schema, tally);
        CheckDrift(root, schemaText, tally);
        if (tally.Failures > 0)
        {
            Console.WriteLine($"{tally.Failures} of {tally.Cases} self-test cases failed.");
            return 1;
        }
        Console.WriteLine($"All {tally.Cases} self-test cases passed.");
        return 0;
    }

    // Each list must fail for its rule alone, and no problem may show the device name that a list holds.
    private static void CheckIosBuilds(string root, Tally tally)
    {
        var schemaText = File.ReadAllText(Path.Combine(root, IosBuilds.SchemaPath));
        foreach (var (name, data, rule) in s_fixtures)
        {
            List<Problem> problems;
            try
            {
                problems = IosBuilds.Check(Encoding.UTF8.GetBytes(data), schemaText, path => path is Record or GateRecord);
            }
            catch (InvalidOperationException exception)
            {
                // A value that the reading let through to a reader that cannot take it.
                tally.Expect(false, $"ios builds, {name}", $"a crash: {exception.Message}");
                continue;
            }
            var shown = problems.Any(problem => problem.ToString().Contains(DeviceName, StringComparison.Ordinal));
            tally.Expect(
                OnlyRule(problems, rule) && !shown,
                $"ios builds, {name}",
                $"{Found(rule, problems)}{(shown ? "; a problem shows the device name" : "")}");
        }
    }

    // The planted schemas go through the loader that the check uses for the list's schema.
    private static void CheckPlantedSchemas(Tally tally)
    {
        foreach (var (name, schema, refusal) in s_plantedSchemas)
        {
            var found = Refusal(() => IosBuilds.LoadSchema(schema));
            tally.Expect(
                found is not null && found.Contains(refusal, StringComparison.Ordinal),
                $"the planted schema with {name}",
                $"expected a refusal at {refusal}, found {found ?? "none"}");
        }
    }

    private static void CheckKeywords(Tally tally)
    {
        foreach (var (name, schemaText, instanceText, rule) in s_keywordCases)
        {
            using var instance = JsonDocument.Parse(instanceText);
            var problems = new List<Problem>();
            SchemaValidator.LoadSchema(schemaText, "keyword case", SchemaValidator.Everything)
                .Validate(instance.RootElement, "case", problems);
            tally.Expect(OnlyRule(problems, rule), $"keyword, {name}", Found(rule, problems));
        }

        // A member whose name is not a plain one is not shown, and a plain name has at most 64 characters.
        var longest = new string('a', 64);
        var names = new (string Name, string Member, string Shown)[]
        {
            ("a name that is not plain", "a b", JsonPointer.NameNotShown),
            ("a plain name of 64 characters", longest, longest),
            ("a plain name of 65 characters", longest + "a", JsonPointer.NameNotShown),
        };
        foreach (var (name, member, shown) in names)
        {
            using var instance = JsonDocument.Parse($$"""{ "{{member}}": 1 }""");
            var problems = new List<Problem>();
            SchemaValidator.LoadSchema("""{ "additionalProperties": false }""", "keyword case", SchemaValidator.Everything)
                .Validate(instance.RootElement, "case", problems);
            tally.Expect(
                problems is [{ Pointer: var pointer }] && pointer == "/" + shown,
                $"keyword, {name}", string.Join(", ", problems));
        }

        foreach (var (name, schemaText, place) in s_refusedSchemas)
        {
            var found = Refusal(() => SchemaValidator.LoadSchema(schemaText, "refused schema", SchemaValidator.Everything));
            tally.Expect(
                found is not null && found.StartsWith($"refused schema: {place}: ", StringComparison.Ordinal),
                $"refused schema, {name}",
                found ?? "the schema was taken");
        }

        foreach (var (name, schemaText) in s_loadedSchemas)
        {
            var refusal = Refusal(() => SchemaValidator.LoadSchema(schemaText, "loaded schema", SchemaValidator.Everything));
            tally.Expect(refusal is null, $"loaded schema, {name}", refusal ?? "");
        }

        foreach (var (name, schemaText, instanceText, pointer, location) in s_timeoutPlaces)
        {
            using var instance = JsonDocument.Parse(instanceText);
            var problems = new List<Problem>();
            SchemaValidator.LoadSchema(schemaText, "keyword case", SchemaValidator.Everything)
                .Validate(instance.RootElement, "case", problems);
            tally.Expect(
                problems is [{ Rule: "pattern", Pointer: var at, Location: var where }] && at == pointer && where == location,
                $"keyword, a pattern that times out {name}",
                $"expected pattern at {pointer}, from {location}, found "
                + string.Join(", ", problems.Select(problem => $"{problem}, from {problem.Location}")));
        }

        // A problem found before a pattern times out stays, beside the timeout's own.
        using var earlier = JsonDocument.Parse($$"""{ "a": 1, "b": {{s_slowText}} }""");
        var kept = new List<Problem>();
        SchemaValidator.LoadSchema(
                """{ "properties": { "a": { "type": "string" }, "b": { "pattern": "^(a+)+$" } } }""", "keyword case",
                SchemaValidator.Everything)
            .Validate(earlier.RootElement, "case", kept);
        tally.Expect(
            kept is [{ Rule: "type", Pointer: "/a" }, { Rule: "pattern", Pointer: "/b" }],
            "keyword, a problem found before a pattern that times out",
            string.Join(", ", kept));
    }

    // The validator's stricter readings: a type of integer or number, exclusiveMinimum, format, and a number at the
    // top of a const or an enum (SchemaValidator's comment). Below not, in an if condition and in a oneOf branch, a
    // match of the subschema fails the value or picks the branch, so there each would be looser than JSON Schema or
    // could go either way. No schema has one there: both schemas are walked, with the definitions that a reference
    // there leads to, and so is a copy of each with such readings planted, which the walk must find.
    private static void CheckStricterReadings(string root, Tally tally)
    {
        var schemas = new (string Path, Action<JsonObject> Plant, string[] Planted)[]
        {
            (TitleSchema.SchemaPath, schema =>
            {
                var definitions = schema["$defs"]!;
                definitions["report"]!["if"]!["properties"]!["date"] = new JsonObject { ["format"] = "date" };
                definitions["id"]!["oneOf"]![0]!["properties"] =
                    new JsonObject { ["steamAppId"] = new JsonObject { ["type"] = "integer" } };
                definitions["relativePath"]!["allOf"]![1]!["not"]!["$ref"] = "#/$defs/planted";
                definitions["planted"] = new JsonObject { ["exclusiveMinimum"] = 0 };
            },
            [
                "/$defs/report/if/properties/date/format", "/$defs/id/oneOf/0/properties/steamAppId/type",
                "/$defs/planted/exclusiveMinimum",
            ]),
            (IosBuilds.SchemaPath, schema => schema["not"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["schemaVersion"] = new JsonObject { ["const"] = 2 },
                    ["builds"] = new JsonObject
                    {
                        ["items"] = new JsonObject
                        {
                            ["properties"] = new JsonObject
                            {
                                ["iosVersion"] = new JsonObject { ["type"] = "number" },
                                ["deviceModel"] = new JsonObject { ["enum"] = new JsonArray("iPhone18,1", 18) },
                            },
                        },
                    },
                },
            },
            [
                "/not/properties/schemaVersion/const", "/not/properties/builds/items/properties/iosVersion/type",
                "/not/properties/builds/items/properties/deviceModel/enum",
            ]),
        };
        foreach (var (path, plant, planted) in schemas)
        {
            var text = File.ReadAllText(Path.Combine(root, path));
            using var schema = JsonDocument.Parse(text);
            var found = StricterReadingsInBranches(schema.RootElement);
            tally.Expect(found.Count == 0, $"stricter readings, none in a branch of {path}", string.Join(", ", found));
            var copy = JsonNode.Parse(text)!.AsObject();
            plant(copy);
            using var plantedSchema = JsonDocument.Parse(copy.ToJsonString());
            var foundPlanted = StricterReadingsInBranches(plantedSchema.RootElement);
            tally.Expect(
                foundPlanted.Order(StringComparer.Ordinal).SequenceEqual(planted.Order(StringComparer.Ordinal)),
                $"stricter readings, those planted in a branch of {path}",
                $"found {string.Join(", ", foundPlanted)}");
        }
    }

    // The places below not, in an if condition and in a oneOf branch where one of the validator's stricter
    // readings sits, also in a definition that a reference there leads to.
    private static List<string> StricterReadingsInBranches(JsonElement root)
    {
        var found = new List<string>();
        var followed = new HashSet<string>(StringComparer.Ordinal);
        void walk(JsonElement schema, string pointer, bool inBranch)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return;
            }
            foreach (var keyword in schema.EnumerateObject())
            {
                var at = $"{pointer}/{JsonPointer.Escape(keyword.Name)}";
                var value = keyword.Value;
                var stricter = keyword.Name switch
                {
                    "type" => value.ValueKind == JsonValueKind.String && value.GetString() is "integer" or "number",
                    "exclusiveMinimum" or "format" => true,
                    "const" => value.ValueKind == JsonValueKind.Number,
                    "enum" => value.ValueKind == JsonValueKind.Array
                        && value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Number),
                    _ => false,
                };
                if (inBranch && stricter)
                {
                    found.Add(at);
                }
                switch (keyword.Name)
                {
                    case "properties" or "$defs" when value.ValueKind == JsonValueKind.Object:
                        foreach (var child in value.EnumerateObject())
                        {
                            walk(child.Value, $"{at}/{JsonPointer.Escape(child.Name)}", inBranch);
                        }
                        break;
                    case "items" or "additionalProperties" or "then" or "else":
                        walk(value, at, inBranch);
                        break;
                    case "not" or "if":
                        walk(value, at, true);
                        break;
                    case "allOf" or "anyOf" or "oneOf" when value.ValueKind == JsonValueKind.Array:
                        var index = 0;
                        foreach (var branch in value.EnumerateArray())
                        {
                            walk(branch, $"{at}/{index++}", inBranch || keyword.Name == "oneOf");
                        }
                        break;
                    case "$ref" when inBranch
                        && value.ValueKind == JsonValueKind.String
                        && value.GetString()!.StartsWith(DefinitionPrefix, StringComparison.Ordinal):
                        var name = value.GetString()![DefinitionPrefix.Length..];
                        if (followed.Add(name)
                            && root.TryGetProperty("$defs", out var definitions)
                            && definitions.TryGetProperty(name, out var target))
                        {
                            walk(target, $"/$defs/{JsonPointer.Escape(name)}", true);
                        }
                        break;
                }
            }
        }
        walk(root, "", false);
        return found;
    }

    // Each schema uses exactly its vocabulary, and the validator implements exactly what the vocabularies hold,
    // so that it implements no keyword that no schema uses.
    private static void CheckVocabularies(string root, TitleSchema titleSchema, Tally tally)
    {
        var iosBuilds = IosBuilds.LoadSchema(File.ReadAllText(Path.Combine(root, IosBuilds.SchemaPath)));
        var cases = new (string Name, IReadOnlySet<string> Used, IReadOnlySet<string> Vocabulary)[]
        {
            (IosBuilds.SchemaPath, iosBuilds.Used, IosBuilds.Vocabulary),
            (TitleSchema.SchemaPath, titleSchema.Validator.Used, TitleSchema.Vocabulary),
        };
        foreach (var (name, used, vocabulary) in cases)
        {
            tally.Expect(
                used.SetEquals(vocabulary),
                $"vocabulary, {name} uses all of its vocabulary",
                $"unused: {string.Join(", ", vocabulary.Except(used).Order(StringComparer.Ordinal))}");
        }
        var unused = SchemaValidator.Everything.Except(IosBuilds.Vocabulary).Except(TitleSchema.Vocabulary);
        tally.Expect(
            SchemaValidator.Everything.SetEquals(IosBuilds.Vocabulary.Union(TitleSchema.Vocabulary)),
            "vocabulary, the validator implements only what the schemas use",
            $"used by no schema: {string.Join(", ", unused.Order(StringComparer.Ordinal))}");
    }

    // Loads a schema through one of the check's loaders, and returns why it was refused, or null.
    private static string? Refusal(Func<SchemaValidator> load)
    {
        try
        {
            _ = load();
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

    // The json rule: what the app's reader rejects before it looks at any field.
    private static void CheckReading(Tally tally)
    {
        var valid = """{ "a": "b" }"""u8.ToArray();
        var cases = new (string Name, byte[] Bytes, bool Valid)[]
        {
            ("a plain object", valid, true),
            ("a byte-order mark", [.. Encoding.UTF8.Preamble, .. valid], true),
            ("more than 1 MiB", Encoding.UTF8.GetBytes($$"""{ "a": "{{new string('x', JsonText.MaxBytes)}}" }"""),
                false),
            ("exactly 1 MiB", Sized(JsonText.MaxBytes), true),
            ("1 MiB and one byte", Sized(JsonText.MaxBytes + 1), false),
            ("a byte-order mark and 1 MiB in all",
                [.. Encoding.UTF8.Preamble, .. Sized(JsonText.MaxBytes - Encoding.UTF8.Preamble.Length)], true),
            ("a byte-order mark and 1 MiB besides", [.. Encoding.UTF8.Preamble, .. Sized(JsonText.MaxBytes)], false),
            ("two byte-order marks", [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.Preamble, .. valid], false),
            ("bytes that are not UTF-8", [(byte)'"', 0xC3, (byte)'"'], false),
            ("a member twice", """{ "a": 1, "a": 2 }"""u8.ToArray(), false),
            ("a member twice, deeper", """{ "a": [{ "b": 1, "b": 1 }] }"""u8.ToArray(), false),
            ("half a surrogate pair in a name", Encoding.UTF8.GetBytes("{ \"\\ud800\": 1 }"), false),
            ("half a surrogate pair in a text", Encoding.UTF8.GetBytes("{ \"a\": \"x\\udc00\" }"), false),
            ("a comment", "{ /* a */ }"u8.ToArray(), false),
            ("a trailing comma", """{ "a": 1, }"""u8.ToArray(), false),
            ("two values", "{} {}"u8.ToArray(), false),
            ("nothing", [], false),
        };
        foreach (var (name, bytes, isValid) in cases)
        {
            var problems = new List<Problem>();
            using var document = JsonText.Read(bytes, "case", problems);
            tally.Expect(
                isValid ? document is not null && problems.Count == 0 : document is null && OnlyRule(problems, "json"),
                $"reading, {name}", string.Join(", ", problems));
        }
    }

    // An object of one text member that takes the given number of bytes, 11 of them around the text.
    private static byte[] Sized(int bytes) =>
        Encoding.UTF8.GetBytes($$"""{ "a": "{{new string('x', bytes - 11)}}" }""");

    private static void CheckReleases(Tally tally)
    {
        var releases = new List<ReleaseVersion>();
        foreach (var text in s_releaseOrder)
        {
            var isTag = ReleaseVersion.TryParse(text, out var release);
            tally.Expect(
                isTag && release!.CompareTo(release) == 0, $"release, {text} is a release tag", "not read as one");
            if (isTag)
            {
                releases.Add(release!);
            }
        }
        for (var index = 0; index < releases.Count; index++)
        {
            for (var later = index + 1; later < releases.Count; later++)
            {
                tally.Expect(
                    releases[index].CompareTo(releases[later]) < 0 && releases[later].CompareTo(releases[index]) > 0,
                    $"release, {s_releaseOrder[index]} before {s_releaseOrder[later]}", "in the wrong order");
            }
        }
        foreach (var text in s_notReleaseTags)
        {
            tally.Expect(!ReleaseVersion.TryParse(text, out _), $"release, {text} is no release tag", "read as one");
        }
        var targets = new HashSet<string>(["iPhone18,1", "iPhone19,2"], StringComparer.Ordinal);
        tally.Expect(Rating.Compute([], targets) == Rating.Untested, "rating, no report", "not untested");
        tally.Expect(
            Rating.Compute(
                [
                    new TestReport(0, "2026-12-01", "iPhone18,1", "v0.3.0", "perfect"),
                    new TestReport(1, "2026-12-01", "iPhone19,2", "v0.3.0", "boots"),
                ],
                targets) == "boots",
            "rating, two target devices that disagree", "not the lower rating");
        tally.Expect(
            Rating.Compute(
                [
                    new TestReport(0, "2026-12-01", "iPhone19,3", "v0.3.0", "boots"),
                    new TestReport(1, "2026-12-08", "iPhone19,7", "v0.3.0", "perfect"),
                ],
                new HashSet<string>(["iPhone19,3", "iPhone19,7"], StringComparer.Ordinal)) == "perfect",
            "rating, the two model identifiers of one device", "not the device's latest report");
    }

    // The map of target devices that the rating uses is the table of docs/design/compat-db.md section 2.1, which
    // the app's TitleSchemaTests.TargetDevicesMatchTheDesignDocument compares with the schema, and it holds the
    // identifiers that the schema lists.
    private static void CheckTargetDevices(string root, TitleSchema schema, Tally tally)
    {
        var lines = File.ReadAllLines(Path.Combine(root, "docs", "design", "compat-db.md"));
        var start = Array.FindIndex(
            lines, line => line.StartsWith('#') && line.EndsWith("Target devices", StringComparison.Ordinal));
        var table = start < 0
            ? []
            : lines.Skip(start + 1)
                .TakeWhile(line => !line.StartsWith('#'))
                .Where(line => line.StartsWith('|'))
                .Select(line => line.Split('|'))
                .Where(cells => cells.Length > 2)
                .SelectMany(cells => s_modelIdentifier.Matches(cells[2])
                    .Select(match => $"{match.Groups[1].Value} = {cells[1].Trim()}"))
                .Order(StringComparer.Ordinal)
                .ToList();
        var map = Rating.Devices.Select(pair => $"{pair.Key} = {pair.Value}").Order(StringComparer.Ordinal).ToList();
        tally.Expect(
            table.Count > 0 && table.SequenceEqual(map),
            "target devices, the map of the rating is the table of compat-db.md section 2.1",
            $"the table has {string.Join("; ", table)}, the map {string.Join("; ", map)}");
        tally.Expect(
            schema.TargetDevices.SetEquals(Rating.Devices.Keys),
            "target devices, the map of the rating holds the identifiers of the schema",
            $"the schema lists {string.Join(", ", schema.TargetDevices.Order(StringComparer.Ordinal))}");
    }

    // The fixtures of tests/compat: the valid folder passes as a whole, and each invalid folder fails for its one
    // rule. Every folder there is listed above, and every listed folder is there.
    private static void CheckTitleFixtures(string root, TitleSchema schema, Tally tally)
    {
        var (valid, files) = Titles.CheckFolder(
            Path.Combine(root, Fixtures, "valid"), $"{Fixtures}/valid", schema, s_fixtureRecords.Contains);
        tally.Expect(valid.Count == 0 && files > 0, $"{Fixtures}/valid", string.Join(", ", valid));
        Console.WriteLine($"{Fixtures}/valid: {files} title files, {valid.Count} problems.");

        var invalid = Path.Combine(root, Fixtures, "invalid");
        var present = Directory.EnumerateDirectories(invalid)
            .Select(directory => Path.GetFileName(directory))
            .Order(StringComparer.Ordinal)
            .ToList();
        var listed = s_invalidTitles.Select(fixture => fixture.Case).Order(StringComparer.Ordinal).ToList();
        tally.Expect(
            present.SequenceEqual(listed), $"{Fixtures}/invalid lists every fixture",
            $"not listed: {string.Join(", ", present.Except(listed))}; "
            + $"missing: {string.Join(", ", listed.Except(present))}");
        foreach (var (name, rule) in s_invalidTitles.Where(fixture => present.Contains(fixture.Case)))
        {
            var (problems, count) = Titles.CheckFolder(
                Path.Combine(invalid, name), $"{Fixtures}/invalid/{name}", schema, s_fixtureRecords.Contains);
            var passed = count == 1 && OnlyRule(problems, rule);
            tally.Expect(passed, $"{Fixtures}/invalid/{name}", Found(rule, problems));
            if (passed)
            {
                Console.WriteLine($"{Fixtures}/invalid/{name}: fails {rule}, as it should.");
            }
        }
    }

    // What the folder of the title files may hold, in a temporary folder: its README.md, which is skipped, and
    // title files; a file of another kind, a readme.md in another letter case, a title file whose extension is
    // not in lower case, and a folder fail. A file whose id the schema rejects and whose name has neither form
    // fails file-name beside the schema's problems.
    private static void CheckFolderLayout(TitleSchema schema, Tally tally)
    {
        var folder = Directory.CreateTempSubdirectory("check-compat-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "README.md"), "# Titles\n");
            var (empty, none) = Titles.CheckFolder(folder.FullName, "case", schema, _ => false);
            tally.Expect(empty.Count == 0 && none == 0, "folder, a README.md only", string.Join(", ", empty));
            var cases = new (string Name, bool Alone, Action<string> Make)[]
            {
                ("a file of another kind", true, path => File.WriteAllText(Path.Combine(path, "notes.txt"), "x\n")),
                ("a readme.md in lower case", true, path => File.WriteAllText(Path.Combine(path, "readme.md"), "x\n")),
                ("an extension in upper case", true, path => File.WriteAllText(
                    Path.Combine(path, "steam-4100000701.JSON"),
                    """
                    { "schemaVersion": 1, "id": { "steamAppId": 4100000701, "slug": "upper-case" },
                      "name": "Upper Case" }
                    """)),
                ("a folder", true, path => Directory.CreateDirectory(Path.Combine(path, "drafts"))),
                ("a name of neither form, with an id that the schema rejects", false, path => File.WriteAllText(
                    Path.Combine(path, "notes.json"),
                    """{ "schemaVersion": 1, "id": { "steamAppId": 0, "slug": "notes" }, "name": "Notes" }""")),
            };
            foreach (var (name, alone, make) in cases)
            {
                var sub = folder.CreateSubdirectory(name.Replace(' ', '-').Replace(",", "", StringComparison.Ordinal));
                make(sub.FullName);
                var (problems, _) = Titles.CheckFolder(sub.FullName, "case", schema, _ => false);
                var named = problems.Count(problem => problem.Rule == "file-name");
                tally.Expect(
                    alone ? problems.Count == 1 && named == 1 : named == 1 && problems.Count > 1,
                    $"folder, {name}",
                    Found("file-name", problems));
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // The fixtures that M6.4 wrote for the app's loader agree with the schema check: each valid reviewed file
    // matches the schema, and each invalid one is rejected, by the json rule or by the schema.
    private static void CheckLoaderFixtures(string root, TitleSchema schema, Tally tally)
    {
        const string Loader = "tests/Paneport.Core.Tests/Compat/Fixtures";
        var counts = new Dictionary<bool, int> { [true] = 0, [false] = 0 };
        foreach (var valid in new[] { true, false })
        {
            var folder = Path.Combine(root, Loader, valid ? "valid" : "invalid", "bundled");
            foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
            {
                var file = $"{Loader}/{(valid ? "valid" : "invalid")}/bundled/{Path.GetFileName(path)}";
                var problems = new List<Problem>();
                using var document = JsonText.Read(File.ReadAllBytes(path), file, problems);
                if (document is not null)
                {
                    schema.Validator.Validate(document.RootElement, file, problems);
                }
                tally.Expect(
                    valid == (problems.Count == 0), file, valid ? string.Join(", ", problems) : "no problem found");
                counts[valid]++;
            }
        }
        Console.WriteLine(
            $"{Loader}: {counts[true]} valid reviewed files match the schema, {counts[false]} invalid ones fail it.");
    }

    // The rules in code hold where a change of the schema drops them: without the schema's launch conditions,
    // each launch fixture still fails its rule; with translator and environment open to any key, the check fails
    // the schema and the fixtures of keys that the allow-lists lack; and with a Steam variable in the environment
    // allow-list, the check fails the schema and the fixture that sets it.
    private static void CheckDrift(string root, string schemaText, Tally tally)
    {
        var open = JsonNode.Parse(schemaText)!.AsObject();
        open["properties"]!["environment"]!.AsObject().Remove("additionalProperties");
        open["properties"]!["translator"]!.AsObject().Remove("additionalProperties");
        var opened = new TitleSchema(open.ToJsonString());
        var openProblems = opened.CheckAllowList();
        var pointers = openProblems.Where(problem => problem.Rule == "allow-list").Select(problem => problem.Pointer);
        tally.Expect(
            openProblems.Count == 2 && pointers.SequenceEqual(["/properties/environment", "/properties/translator"]),
            "drift, allow-lists open to any key",
            string.Join(", ", openProblems));
        var typed = new TitleSchema(OpenWithType(schemaText));
        tally.Expect(
            typed.CheckAllowList() is [{ Rule: "allow-list", Pointer: "/properties/environment" }],
            "drift, an environment that allows any key of one type",
            string.Join(", ", typed.CheckAllowList()));
        foreach (var name in new[] { "allow-list-environment-name-not-listed", "allow-list-translator-option-not-listed" })
        {
            var (fixture, _) = Titles.CheckFolder(
                Path.Combine(root, Fixtures, "invalid", name), name, opened, s_fixtureRecords.Contains);
            tally.Expect(
                OnlyRule(fixture, "allow-list"),
                $"drift, {name} with any key allowed",
                Found("allow-list", fixture));
        }

        var withoutLaunch = JsonNode.Parse(schemaText)!.AsObject();
        withoutLaunch.Remove("allOf");
        var unconditioned = new TitleSchema(withoutLaunch.ToJsonString());
        foreach (var (name, _) in s_invalidTitles.Where(fixture => fixture.Rule == "launch"))
        {
            var (problems, _) = Titles.CheckFolder(
                Path.Combine(root, Fixtures, "invalid", name), name, unconditioned, s_fixtureRecords.Contains);
            tally.Expect(
                OnlyRule(problems, "launch"),
                $"drift, {name} without the schema's conditions",
                Found("launch", problems));
        }

        // Each Steam variable of C-0008 section 5, and one in another letter case.
        foreach (var variable in new[] { "SteamAppId", "SteamGameId", "SteamAppPath", "steamappid" })
        {
            var withSteamVariable = JsonNode.Parse(schemaText)!.AsObject();
            withSteamVariable["properties"]!["environment"]!["properties"]![variable] =
                new JsonObject { ["type"] = "string" };
            var steamAllowed = new TitleSchema(withSteamVariable.ToJsonString());
            var steamProblems = steamAllowed.CheckAllowList();
            tally.Expect(
                steamProblems is [{ Rule: "allow-list", Pointer: var pointer }]
                    && pointer == $"/properties/environment/properties/{variable}",
                $"drift, {variable} in the environment allow-list", string.Join(", ", steamProblems));
            if (variable == "SteamAppId")
            {
                const string Case = "allow-list-environment-steam-app-id";
                var (steamFixture, _) = Titles.CheckFolder(
                    Path.Combine(root, Fixtures, "invalid", Case), Case, steamAllowed, s_fixtureRecords.Contains);
                tally.Expect(
                    OnlyRule(steamFixture, "allow-list"),
                    $"drift, {Case} with the variable allowed",
                    Found("allow-list", steamFixture));
            }
        }

        // An allow-list that is a schema of true, open to any key, fails; a target device that is no text is
        // refused.
        var openTrue = JsonNode.Parse(schemaText)!.AsObject();
        openTrue["properties"]!["environment"] = true;
        var openTrueProblems = new TitleSchema(openTrue.ToJsonString()).CheckAllowList();
        tally.Expect(
            openTrueProblems is [{ Rule: "allow-list", Pointer: "/properties/environment" }],
            "drift, an environment of true",
            string.Join(", ", openTrueProblems));
        var numbered = JsonNode.Parse(schemaText)!.AsObject();
        numbered["$defs"]!["targetDevices"]!["enum"]!.AsArray().Add(5);
        var refusal = Refusal(() => new TitleSchema(numbered.ToJsonString()).Validator);
        tally.Expect(
            refusal?.StartsWith($"{TitleSchema.SchemaPath}: /$defs/targetDevices/enum", StringComparison.Ordinal)
                == true,
            "drift, a target device that is a number",
            refusal ?? "the schema was taken");

        CheckUntyped(schemaText, tally);
    }

    // Without the schema's launch conditions and with the types of the members that the rules in code read
    // dropped, a member of another type fails the rule that reads it, and nothing throws. Each case states only
    // what makes the member's type the one problem, so that no other rule fails it.
    private static void CheckUntyped(string schemaText, Tally tally)
    {
        var untyped = JsonNode.Parse(schemaText)!.AsObject();
        untyped.Remove("allOf");
        var definitions = untyped["$defs"]!;
        definitions["drm"]!.AsObject().Remove("type");
        definitions["drm"]!["properties"]!["steamApi"] = new JsonObject();
        definitions["drm"]!["properties"]!["steamStub"]!.AsObject().Remove("type");
        definitions["drm"]!["properties"]!["ceg"]!.AsObject().Remove("enum");
        definitions["executable"]!.AsObject().Remove("type");
        untyped["properties"]!["launch"] = new JsonObject();
        untyped["properties"]!["environment"]!.AsObject().Remove("type");
        untyped["properties"]!["rating"] = new JsonObject();
        untyped["properties"]!["reports"]!.AsObject().Remove("type");
        foreach (var member in new[] { "date", "device", "paneportVersion" })
        {
            definitions["report"]!["properties"]![member]!.AsObject().Remove("type");
        }
        definitions["report"]!["properties"]!["rating"] = new JsonObject();
        definitions["report"]!["properties"]!["evidence"] = new JsonObject();
        var schema = new TitleSchema(untyped.ToJsonString());

        const string Title = """
            "schemaVersion": 1, "id": { "steamAppId": 4100000702, "slug": "untyped" }, "name": "Untyped"
            """;
        const string Report = """
            "date": "2026-12-01", "device": "iPhone18,1", "iosVersion": "27.2 (24B119)", "paneportVersion": "v0.3.0",
            "gameBuild": "20261201"
            """;
        const string Evidence = "docs/evidence/2026-12-01-fixture-first-run.md";
        var dateNumber = Report.Replace("\"date\": \"2026-12-01\"", "\"date\": 5", StringComparison.Ordinal);
        var deviceNumber = Report.Replace("\"device\": \"iPhone18,1\"", "\"device\": 5", StringComparison.Ordinal);
        var versionNumber =
            Report.Replace("\"paneportVersion\": \"v0.3.0\"", "\"paneportVersion\": 5", StringComparison.Ordinal);
        var cases = new (string Name, string Members, string Rule)[]
        {
            ("a drm marker that is a text", """ "drm": { "steamApi": "yes" } """, "launch"),
            ("a drm that is a list", """ "drm": [false] """, "launch"),
            ("a steamStub that is a text", """ "drm": { "steamStub": "no" } """, "launch"),
            ("a ceg that is a number", """ "drm": { "ceg": 0 } """, "launch"),
            ("an executable that is a number",
                """ "executable": 5, "drm": { "steamApi": false, "steamStub": false, "ceg": "no" }, "launch": "direct" """,
                "launch"),
            ("a launch that is a number", """ "launch": 5 """, "launch"),
            ("an environment that is a text", """ "environment": "x" """, "allow-list"),
            ("a rating that is a number", """ "rating": 5 """, "rating"),
            ("reports that are a text", """ "reports": "x" """, "rating"),
            ("a report whose rating is a number",
                $$""" "reports": [{ {{Report}}, "rating": 5, "evidence": "{{Evidence}}" }] """,
                "rating"),
            // The file states the rating that the report would give if it counted, so that a rule that counted it
            // would find no problem.
            ("a report whose date is a number",
                $$""" "rating": "boots", "reports": [{ {{dateNumber}}, "rating": "boots", "evidence": "{{Evidence}}" }] """,
                "rating"),
            ("a report whose device is a number",
                $$""" "reports": [{ {{deviceNumber}}, "rating": "boots", "evidence": "{{Evidence}}" }] """,
                "rating"),
            ("a report whose paneportVersion is a number",
                $$""" "reports": [{ {{versionNumber}}, "rating": "boots", "evidence": "{{Evidence}}" }] """,
                "rating"),
            ("a report whose evidence is a number",
                $$""" "rating": "boots", "reports": [{ {{Report}}, "rating": "boots", "evidence": 5 }] """,
                "evidence-file"),
        };
        foreach (var (name, members, rule) in cases)
        {
            List<Problem> problems;
            try
            {
                problems = Titles.CheckFile(
                    "case", "steam-4100000702.json", Encoding.UTF8.GetBytes($"{{ {Title}, {members} }}"), schema,
                    s_fixtureRecords.Contains);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
            {
                tally.Expect(false, $"drift, {name}", $"a crash: {exception.Message}");
                continue;
            }
            tally.Expect(OnlyRule(problems, rule), $"drift, {name}", Found(rule, problems));
        }
    }

    // The title schema with an environment that takes any key whose value is a text.
    private static string OpenWithType(string schemaText)
    {
        var schema = JsonNode.Parse(schemaText)!.AsObject();
        schema["properties"]!["environment"]!["additionalProperties"] = new JsonObject { ["type"] = "string" };
        return schema.ToJsonString();
    }

    private static bool OnlyRule(List<Problem> problems, string? rule) =>
        rule is null ? problems.Count == 0 : problems.Count > 0 && problems.All(problem => problem.Rule == rule);

    private static string Found(string? rule, List<Problem> problems) =>
        $"expected {rule ?? "no problem"}, found {(problems.Count == 0 ? "none" : string.Join(", ", problems))}";
}
