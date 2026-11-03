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
/// 🚨 The gap this closes was measured, not imagined. An earlier change added
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

    /// <summary>
    /// Proves that every member the document calls nullable is nullable on the generated type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The field test above proves a member EXISTS. A request member the document says accepts <c>null</c> (an optional
    /// free-form object, a nullable <c>JsonElement</c>) has to be nullable in the generated type too: a non-nullable
    /// type is a promise the generated code does not keep, so the member starts at <see langword="null"/> or
    /// <c>default</c> and the first request of a caller that sets only what it needs fails on the server (a
    /// <c>400</c>) or in the serializer.
    /// </para>
    /// <para>
    /// Reflection with <see cref="NullabilityInfoContext"/>, not a text scan, for the same reason as the test above:
    /// the generator renames members and the annotation is what the compiler reads.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_member_the_document_calls_nullable_is_nullable_on_the_generated_type()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(DocumentPath));

        var generated = typeof(TraconApiClient).Assembly.GetTypes()
            .Where(static type => type.IsClass && type.IsPublic)
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        var context = new NullabilityInfoContext();
        var disagreeing = new List<string>();
        var checkedMembers = 0;

        foreach (var schema in document.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject())
        {
            if (!schema.Value.TryGetProperty("properties", out var properties) || !generated.TryGetValue(schema.Name, out var type))
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject().Where(static property => IsNullableInDocument(property.Value)))
            {
                var member = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? candidate.Name,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase));

                if (member is null)
                {
                    continue;
                }

                checkedMembers++;

                var nullable = member.PropertyType.IsValueType
                    ? Nullable.GetUnderlyingType(member.PropertyType) is not null
                    : context.Create(member).ReadState == NullabilityState.Nullable;

                if (!nullable)
                {
                    disagreeing.Add($"{schema.Name}.{property.Name} ({member.PropertyType.Name})");
                }
            }
        }

        // A scan that matched no member would pass for the wrong reason.
        checkedMembers.ShouldBeGreaterThan(100);

        disagreeing.Sort(StringComparer.Ordinal);

        disagreeing.ShouldBeEmpty(
            customMessage: $"{disagreeing.Count} member(s) the document calls nullable are not nullable on the generated type, " +
                           "so a caller that leaves them unset sends a null or a default the type said it would not. " +
                           "The generated client is older than the document, or the document shape is one the generator reads " +
                           "as non-nullable.");
    }

    // A member accepts null when its type list names null or one of its alternatives is the null type.
    private static bool IsNullableInDocument(JsonElement property)
    {
        if (property.TryGetProperty("type", out var type))
        {
            return type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(static item => string.Equals(item.GetString(), "null", StringComparison.Ordinal))
                : string.Equals(type.GetString(), "null", StringComparison.Ordinal);
        }

        foreach (var key in new[] { "oneOf", "anyOf" })
        {
            if (property.TryGetProperty(key, out var branches)
                && branches.EnumerateArray().Any(static branch =>
                    branch.TryGetProperty("type", out var branchType)
                    && string.Equals(branchType.GetString(), "null", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
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
