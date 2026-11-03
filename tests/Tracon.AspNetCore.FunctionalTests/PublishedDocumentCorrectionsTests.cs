using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Tracon.Api;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The document corrections the reference host registers, on documents small enough to read.
/// </summary>
/// <remarks>
/// <c>OpenApiDocumentShapeTests</c> proves the live document came out right; these prove what each rule touches and,
/// as important, what it leaves alone: a nullable enum keeps its wrapper, a nullable object that is not free-form
/// keeps its shape, and a second run changes nothing.
/// </remarks>
public sealed class PublishedDocumentCorrectionsTests
{
    [Fact]
    public void An_enum_component_loses_its_null_and_keeps_the_other_values_in_order()
    {
        var kind = new OpenApiSchema { Enum = [JsonValue.Create("A"), JsonValue.Create("B"), null!] };
        var document = Document(("Kind", kind));

        var counts = PublishedDocumentCorrections.Apply(document);

        counts.NullableEnumComponents.ShouldBe(1);
        kind.Enum!.Select(static value => value!.GetValue<string>()).ShouldBe(["A", "B"]);
    }

    [Fact]
    public void A_nullable_member_whose_other_branch_accepts_null_becomes_any_of()
    {
        var holder = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["payload"] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, new OpenApiSchemaReference("JsonElement")] },
            },
        };
        var document = Document(("JsonElement", new OpenApiSchema()), ("Holder", holder));

        var counts = PublishedDocumentCorrections.Apply(document);

        counts.AnyOfWrappers.ShouldBe(1);

        var payload = (OpenApiSchema)holder.Properties["payload"];

        payload.OneOf.ShouldBeNull();
        payload.AnyOf.ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Fact]
    public void A_nullable_enum_member_keeps_its_one_of_once_the_component_has_lost_its_null()
    {
        var holder = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["kind"] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, new OpenApiSchemaReference("Kind")] },
            },
        };
        var document = Document(("Kind", new OpenApiSchema { Enum = [JsonValue.Create("A"), null!] }), ("Holder", holder));

        var counts = PublishedDocumentCorrections.Apply(document);

        counts.NullableEnumComponents.ShouldBe(1);
        counts.AnyOfWrappers.ShouldBe(0);
        ((OpenApiSchema)holder.Properties["kind"]).OneOf.ShouldNotBeNull().Count.ShouldBe(2);
    }

    [Fact]
    public void A_nullable_object_member_that_is_not_a_json_element_dictionary_is_left_alone()
    {
        var holder = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["nested"] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, new OpenApiSchemaReference("Nested")] },
            },
        };
        var nested = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal) { ["name"] = new OpenApiSchema { Type = JsonSchemaType.String } },
        };

        var counts = PublishedDocumentCorrections.Apply(Document(("Nested", nested), ("Holder", holder)));

        counts.AnyOfWrappers.ShouldBe(0);
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        var holder = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["payload"] = new OpenApiSchema { OneOf = [new OpenApiSchema { Type = JsonSchemaType.Null }, new OpenApiSchemaReference("JsonElement")] },
            },
        };
        var document = Document(("JsonElement", new OpenApiSchema()), ("Kind", new OpenApiSchema { Enum = [JsonValue.Create("A"), null!] }), ("Holder", holder));

        PublishedDocumentCorrections.Apply(document);
        var second = PublishedDocumentCorrections.Apply(document);

        second.NullableEnumComponents.ShouldBe(0);
        second.AnyOfWrappers.ShouldBe(0);
    }

    [Theory]
    [InlineData(typeof(IReadOnlyDictionary<string, JsonElement>), true)]
    [InlineData(typeof(IDictionary<string, JsonElement>), true)]
    [InlineData(typeof(Dictionary<string, JsonElement>), true)]
    [InlineData(typeof(IReadOnlyDictionary<string, string>), false)]
    [InlineData(typeof(IReadOnlyDictionary<string, object>), false)]
    [InlineData(typeof(IReadOnlyDictionary<int, JsonElement>), false)]
    [InlineData(typeof(List<JsonElement>), false)]
    [InlineData(typeof(string), false)]
    public void Only_a_string_keyed_dictionary_of_json_elements_is_a_free_form_dictionary(Type type, bool expected)
        => PublishedDocumentCorrections.IsJsonElementDictionary(type).ShouldBe(expected);

    private static OpenApiDocument Document(params (string Name, OpenApiSchema Schema)[] schemas)
    {
        var document = new OpenApiDocument { Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal) } };

        foreach (var (name, schema) in schemas)
        {
            document.Components.Schemas[name] = schema;
        }

        return document;
    }
}
