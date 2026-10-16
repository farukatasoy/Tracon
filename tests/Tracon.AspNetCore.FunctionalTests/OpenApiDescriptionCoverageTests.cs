using System.Text.Json.Nodes;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Fails when the shipped OpenAPI document loses the description of a schema or of a
/// property.
/// </summary>
/// <remarks>
/// <para>
/// HTTP request and response DTOs are <c>internal</c> (Phase 212). The document generator
/// reads XML documentation only from a public type, so these types carry their text in
/// <c>[Description]</c>. A DTO that loses the attribute compiles, passes every
/// behavior test, and ships a schema without text. The older floor in
/// <see cref="OpenApiDocumentTests"/> (more than 500 described properties) cannot see
/// that: the 81 DTOs hold 298 of the document's directly described properties, and
/// without them 866 are left.
/// </para>
/// <para>
/// A ratchet in the style of the public surface baseline: <c>openapi-undescribed-baseline.txt</c>
/// lists every schema (<c>Schema</c>) and property (<c>Schema.property</c>) that has no
/// description today. A new undescribed entry fails, and so does a listed entry that
/// gained a description, so the file can only shrink. The scan counts a description that
/// sits in a <c>oneOf</c>, <c>anyOf</c> or <c>allOf</c> branch: a nullable reference
/// property carries its text there.
/// </para>
/// <para>
/// The baseline is a separate file. Refreshing the OpenAPI snapshot
/// (<c>TRACON_OPENAPI_REFRESH=1</c>) never rewrites it.
/// </para>
/// </remarks>
public sealed class OpenApiDescriptionCoverageTests
{
    private static readonly string[] BranchKeywords = ["oneOf", "anyOf", "allOf"];

    [Fact]
    public async Task Undescribed_schemas_and_properties_match_the_checked_in_baseline()
    {
        var document = JsonNode.Parse(await OpenApiSnapshotTests.GenerateAsync())
            ?? throw new InvalidOperationException("The OpenAPI document is a JSON null.");

        var actual = FindUndescribed(document);
        var baseline = ReadBaseline();

        var lost = actual.Except(baseline, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var stale = baseline.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        lost.ShouldBeEmpty(
            "These schemas or properties have no description in the OpenAPI document. An internal DTO " +
            "takes its text from [Description]; a public type takes it from its XML summary: " +
            string.Join(", ", lost));

        stale.ShouldBeEmpty(
            "These baseline lines now have a description. Delete them from " +
            "'openapi-undescribed-baseline.txt' so the baseline only shrinks: " +
            string.Join(", ", stale));
    }

    /// <summary>Proves the scan is not vacuous: it finds each kind of gap and accepts each kind of text.</summary>
    [Fact]
    public void The_scan_finds_an_undescribed_schema_and_an_undescribed_property()
    {
        var document = JsonNode.Parse(
            """
            {
              "components": {
                "schemas": {
                  "Described": {
                    "description": "A schema.",
                    "properties": {
                      "direct": { "type": "string", "description": "Direct text." },
                      "branch": { "oneOf": [ { "type": "null" }, { "description": "Text in a branch.", "$ref": "#/components/schemas/Other" } ] },
                      "bare": { "type": "string" },
                      "blank": { "type": "string", "description": "  " },
                      "nullBranch": { "oneOf": [ { "type": "null" }, { "$ref": "#/components/schemas/Other" } ] }
                    }
                  },
                  "Undescribed": { "type": "object" }
                }
              }
            }
            """)!;

        FindUndescribed(document).Order(StringComparer.Ordinal).ShouldBe(
            ["Described.bare", "Described.blank", "Described.nullBranch", "Undescribed"]);
    }

    private static HashSet<string> FindUndescribed(JsonNode document)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        if (document["components"]?["schemas"] is not JsonObject schemas)
        {
            return found;
        }

        foreach (var (schemaName, schema) in schemas)
        {
            if (schema is not JsonObject schemaObject)
            {
                continue;
            }

            if (!IsDescribed(schemaObject))
            {
                found.Add(schemaName);
            }

            if (schemaObject["properties"] is not JsonObject properties)
            {
                continue;
            }

            foreach (var (propertyName, property) in properties)
            {
                if (property is not JsonObject propertyObject || !IsDescribed(propertyObject))
                {
                    found.Add(schemaName + "." + propertyName);
                }
            }
        }

        return found;
    }

    private static bool IsDescribed(JsonObject node)
    {
        if (HasText(node))
        {
            return true;
        }

        foreach (var keyword in BranchKeywords)
        {
            if (node[keyword] is JsonArray branches &&
                branches.OfType<JsonObject>().Any(HasText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasText(JsonObject node)
        => node["description"] is JsonValue value &&
           value.TryGetValue<string>(out var text) &&
           !string.IsNullOrWhiteSpace(text);

    private static HashSet<string> ReadBaseline()
    {
        var path = Path.Combine(FindRepositoryRoot(), "tests", "Tracon.AspNetCore.FunctionalTests", "openapi-undescribed-baseline.txt");

        File.Exists(path).ShouldBeTrue($"'{path}' is missing.");

        return File.ReadAllLines(path)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tracon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Tracon.slnx not found.");
    }
}
