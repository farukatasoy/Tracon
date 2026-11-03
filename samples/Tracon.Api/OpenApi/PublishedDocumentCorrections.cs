using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Tracon.Api;

/// <summary>
/// Rewrites three shapes the .NET OpenAPI generator writes so the published document says what the
/// server really accepts and returns.
/// </summary>
/// <remarks>
/// <para>
/// Tracon.AspNetCore does not depend on <c>Microsoft.AspNetCore.OpenApi</c>, so a document transformer
/// cannot ship with it: a consumer's own <c>AddOpenApi()</c> call produces that consumer's document, as
/// the generator wrote it. This class is the one place the repository's own document is corrected. The
/// reference host registers it for its live document, and the test host that writes the committed
/// snapshot links this same file, so both documents come out of the same code.
/// </para>
/// <para>
/// 🚨 It is registered with <c>services.Configure&lt;OpenApiOptions&gt;("v1", Configure)</c> next to a
/// bare <c>AddOpenApi()</c>. Passing the configuration delegate to <c>AddOpenApi</c> makes the XML
/// documentation interceptor skip the call and every schema property loses its description.
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// A nullable enum is written twice as nullable: the shared component lists <c>null</c> among its
/// values, and every nullable property wraps the reference in <c>oneOf</c> with a null branch. A
/// value of <c>null</c> then matches two branches and fails <c>oneOf</c>. The component loses its
/// <c>null</c>; the wrapper already carries the nullability, and a bare reference is correctly
/// non-nullable.
/// </description>
/// </item>
/// <item>
/// <description>
/// A nullable <c>JsonElement</c> is written as <c>oneOf</c> of null and the <c>JsonElement</c>
/// component, which is the empty schema and accepts null by itself. The same value matches both
/// branches. <c>anyOf</c> says "one or more", which is what is meant.
/// </description>
/// </item>
/// <item>
/// <description>
/// A dictionary whose values are <c>JsonElement</c> is written as a bare <c>type: object</c>, which
/// the TypeScript generator turns into <c>Record&lt;string, never&gt;</c> (no key may carry a value).
/// An <c>additionalProperties</c> entry that accepts any value says what the dictionary holds. The
/// property is found by its CLR type: the document no longer shows what the values were.
/// </description>
/// </item>
/// </list>
/// </remarks>
internal static class PublishedDocumentCorrections
{
    /// <summary>Registers the corrections on the document named <c>v1</c>.</summary>
    /// <param name="options">The OpenAPI options of the host.</param>
    public static void Configure(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddSchemaTransformer(static (schema, context, _) =>
        {
            if (context.JsonPropertyInfo is { } property && IsJsonElementDictionary(property.PropertyType))
            {
                schema.AdditionalProperties = new OpenApiSchema();
            }

            return Task.CompletedTask;
        });

        options.AddDocumentTransformer(static (document, _, _) =>
        {
            Apply(document);

            return Task.CompletedTask;
        });
    }

    /// <summary>Applies the document-level corrections (the first and the second) to a document in place.</summary>
    /// <param name="document">The generated document.</param>
    /// <returns>How many entries each correction changed.</returns>
    public static CorrectionCounts Apply(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var schemas = document.Components?.Schemas;

        if (schemas is null)
        {
            return new CorrectionCounts(0, 0);
        }

        // The enum goes first: once a component has lost its null, a property that wraps it is not a case for the
        // second correction.
        var enumsClosed = CloseNullableEnums(schemas);
        var anyOfRewritten = RewriteNullableAnyValue(schemas);

        return new CorrectionCounts(enumsClosed, anyOfRewritten);
    }

    /// <summary>Gets whether a CLR type is a dictionary keyed by string whose values are <see cref="JsonElement"/>.</summary>
    /// <param name="type">The property type.</param>
    /// <returns><see langword="true"/> for <c>IDictionary</c>, <c>IReadOnlyDictionary</c> and <c>Dictionary</c> of string to <c>JsonElement</c>.</returns>
    internal static bool IsJsonElementDictionary(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!type.IsGenericType
            || type.GetGenericArguments() is not [var key, var value]
            || key != typeof(string)
            || value != typeof(JsonElement))
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();

        return definition == typeof(IReadOnlyDictionary<,>)
            || definition == typeof(IDictionary<,>)
            || definition == typeof(Dictionary<,>);
    }

    // Removes the null entry from the value list of an enum component.
    private static int CloseNullableEnums(IDictionary<string, IOpenApiSchema> schemas)
    {
        var changed = 0;

        foreach (var schema in schemas.Values.OfType<OpenApiSchema>())
        {
            if (schema.Enum is not { Count: > 0 } values)
            {
                continue;
            }

            var before = values.Count;

            for (var index = values.Count - 1; index >= 0; index--)
            {
                if (values[index] is not { } value || value.GetValueKind() == JsonValueKind.Null)
                {
                    values.RemoveAt(index);
                }
            }

            if (values.Count != before)
            {
                changed++;
            }
        }

        return changed;
    }

    // oneOf [null, X] becomes anyOf [null, X] when X accepts null by itself.
    private static int RewriteNullableAnyValue(IDictionary<string, IOpenApiSchema> schemas)
    {
        var changed = 0;

        foreach (var schema in schemas.Values.OfType<OpenApiSchema>())
        {
            if (schema.Properties is null)
            {
                continue;
            }

            foreach (var property in schema.Properties.Values.OfType<OpenApiSchema>())
            {
                if (property.OneOf is not { Count: 2 } branches || property.AnyOf is { Count: > 0 })
                {
                    continue;
                }

                var other = branches.FirstOrDefault(static branch => !IsNullBranch(branch));

                if (other is null || branches.Count(IsNullBranch) != 1 || !AcceptsAnything(Resolve(other, schemas)))
                {
                    continue;
                }

                property.AnyOf = [.. branches];
                property.OneOf = null;
                changed++;
            }
        }

        return changed;
    }

    private static bool IsNullBranch(IOpenApiSchema branch)
        => branch is not OpenApiSchemaReference && branch.Type == JsonSchemaType.Null;

    private static IOpenApiSchema Resolve(IOpenApiSchema schema, IDictionary<string, IOpenApiSchema> schemas)
        => schema is OpenApiSchemaReference { Reference.Id: { Length: > 0 } id } && schemas.TryGetValue(id, out var target)
            ? target
            : schema;

    // The empty schema `{}` (JsonElement): no type, no value list and no keyword that narrows it.
    private static bool AcceptsAnything(IOpenApiSchema schema)
        => schema.Type is null
            && schema.Enum is not { Count: > 0 }
            && schema.OneOf is not { Count: > 0 }
            && schema.AnyOf is not { Count: > 0 }
            && schema.AllOf is not { Count: > 0 }
            && schema.Properties is not { Count: > 0 }
            && schema.Items is null
            && schema.Not is null
            && schema.Format is null
            && schema.Pattern is null
            && schema.Const is null;
}

/// <summary>How many entries each document-level correction of <see cref="PublishedDocumentCorrections"/> changed.</summary>
/// <param name="NullableEnumComponents">Enum components that lost their <c>null</c> value.</param>
/// <param name="AnyOfWrappers">Wrappers rewritten from <c>oneOf</c> to <c>anyOf</c>.</param>
internal sealed record CorrectionCounts(int NullableEnumComponents, int AnyOfWrappers);
