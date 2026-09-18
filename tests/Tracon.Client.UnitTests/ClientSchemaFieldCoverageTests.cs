using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracon.Client.Generated;

namespace Tracon.Client.UnitTests;

/// <summary>
/// Proves that every property a schema declares in
/// <c>docs/openapi/tracon.json</c> exists on the generated type.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The gap this closes was measured, not imagined. Phase 176 added
/// <c>EvaluatorVersion</c> to a response record, the OpenAPI snapshot picked it
/// up, and the generated client was never re-run: the field was absent from
/// <c>TraconApiClient.g.cs</c> for a whole phase. Neither existing gate could
/// see it - <see cref="ClientCoverageTests"/> proves a METHOD exists per
/// operationId, and <see cref="ClientDescriptionBaselineTests"/> pins a count.
/// A consumer reading that field through the typed client got nothing, with
/// nothing red anywhere.
/// </para>
/// <para>
/// Reflection over the compiled assembly rather than a text scan of the
/// generated file: NSwag renames a property (<c>id</c> becomes <c>Id</c>) and
/// carries the wire name in <see cref="JsonPropertyNameAttribute"/>, which is
/// the name this compares, so the gate cannot drift with a naming convention.
/// </para>
/// </remarks>
public sealed class ClientSchemaFieldCoverageTests
{
    /// <summary>
    /// Schemas the generated client deliberately does not materialize as a
    /// type of its own.
    /// </summary>
    /// <remarks>
    /// Empty today, and kept so a future exemption has to be written down with
    /// a reason rather than silently skipped.
    /// </remarks>
    private static readonly HashSet<string> NotMaterialized = new(StringComparer.Ordinal);

    [Fact]
    public void Every_schema_property_exists_on_the_generated_type()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DocumentPath));

        var generated = typeof(TraconApiClient).Assembly.GetTypes()
            .Where(static type => type.IsClass && type.IsPublic)
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        var missing = new List<string>();
        var checkedSchemas = 0;

        foreach (var schema in document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject())
        {
            if (NotMaterialized.Contains(schema.Name)
                || !schema.Value.TryGetProperty("properties", out var properties)
                || !generated.TryGetValue(schema.Name, out var type))
            {
                continue;
            }

            checkedSchemas++;

            var carried = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property =>
                    property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var property in properties.EnumerateObject())
            {
                if (!carried.Contains(property.Name))
                {
                    missing.Add($"{schema.Name}.{property.Name}");
                }
            }
        }

        // A scan that matched no schema would pass for the wrong reason.
        checkedSchemas.ShouldBeGreaterThan(100);

        missing.Sort(StringComparer.Ordinal);

        missing.ShouldBeEmpty(
            customMessage: $"{missing.Count} schema propert(ies) are missing from the generated client, so a " +
                           "consumer reading them through the typed client gets nothing. Regenerate it: " +
                           "dotnet tool restore && python3 scripts/nswag-prepare-document.py " +
                           "docs/openapi/tracon.json artifacts/openapi/tracon.client-input.json && " +
                           "dotnet nswag run nswag.json && python3 scripts/nswag-postprocess-client.py " +
                           "src/Tracon.Client/Generated/TraconApiClient.g.cs docs/openapi/tracon.json && " +
                           "python3 scripts/generate-client-json-context.py " +
                           "src/Tracon.Client/Generated/TraconApiClient.g.cs " +
                           "src/Tracon.Client/Generated/TraconClientJsonContext.g.cs");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string DocumentPath { get; } =
        Path.Combine(RepositoryRoot, "docs", "openapi", "tracon.json");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Tracon.slnx not found.");
    }
}
