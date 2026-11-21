// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// eng/license-map.json, as tools/CheckLicenses.cs and tools/GenerateNotices.cs read it. Both
// include this file with #:include; a shared file carries no #: directive of its own
// (docs/design/repository-checks.md section 2).

using System.Text.Json;

// What the map names: a package ID, a pinned tool, an action or a submodule.
internal enum Kind
{
    Package,
    Tool,
    Action,
    Submodule,
}

// eng/license-map.json: for each package ID, tool, action and submodule, the register row that
// covers it. Package IDs compare without regard to case, as NuGet compares them.
internal sealed class LicenseMap(Dictionary<Kind, Dictionary<string, string>> sections)
{
    public const string FilePath = "eng/license-map.json";

    private static readonly Dictionary<string, Kind> s_sectionNames = new(StringComparer.Ordinal)
    {
        ["packages"] = Kind.Package,
        ["tools"] = Kind.Tool,
        ["actions"] = Kind.Action,
        ["submodules"] = Kind.Submodule,
    };

    public IEnumerable<(Kind Kind, string Name, string Component)> Entries =>
        sections.SelectMany(section => section.Value.Select(entry => (section.Key, entry.Key, entry.Value)));

    public static LicenseMap Parse(string json)
    {
        var sections = Enum.GetValues<Kind>()
            .ToDictionary(kind => kind, _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{FilePath} must be a JSON object.");
            }

            foreach (var section in document.RootElement.EnumerateObject())
            {
                if (!s_sectionNames.TryGetValue(section.Name, out var kind)
                    || section.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"{FilePath}: \"{section.Name}\" is not one of the sections "
                            + $"{string.Join(", ", s_sectionNames.Keys)}.");
                }

                foreach (var entry in section.Value.EnumerateObject())
                {
                    var component = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(component))
                    {
                        throw new InvalidDataException($"{FilePath}: \"{entry.Name}\" must name a register row.");
                    }

                    if (!sections[kind].TryAdd(entry.Name, component))
                    {
                        throw new InvalidDataException(
                            $"{FilePath}: \"{entry.Name}\" is listed twice in \"{section.Name}\".");
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }

        return new LicenseMap(sections);
    }

    public string? ComponentFor(Kind kind, string name) => sections[kind].GetValueOrDefault(name);
}
