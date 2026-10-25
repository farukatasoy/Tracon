using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.OpenAIShared.Tests;

/// <summary>
/// The schema the OpenAI clients send for an author's schema, and the two lists that tell the author what
/// differs (Phase 245).
/// </summary>
/// <remarks>
/// Linked into <c>Tracon.OpenAI.UnitTests</c> and <c>Tracon.Azure.UnitTests</c>: the source under test is
/// compiled into both packages. Whether the copy matches the real client is
/// <see cref="StrictResponseSchemaWireTests"/>' job; this class pins what the copy does.
/// </remarks>
public sealed class OpenAIResponseSchemaTests
{
    private const string Author = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "maxLength": 20, "description": "Full name" },
            "nickname": { "type": "string" },
            "age": { "type": ["integer", "null"], "minimum": 0 },
            "role": { "type": "string", "default": "user" },
            "address": {
              "type": "object",
              "properties": {
                "city": { "type": "string" },
                "zip": { "type": "string", "pattern": "^[0-9]{5}$" }
              },
              "required": ["city"]
            },
            "tags": {
              "type": "array",
              "minItems": 1,
              "items": { "type": "object", "properties": { "label": { "type": "string" } } }
            }
          },
          "required": ["name"]
        }
        """;

    [Fact]
    public void The_effective_schema_closes_objects_and_requires_every_property()
    {
        var shape = OpenAIResponseSchema.Describe(Parse(Author), strict: false);
        var schema = JsonNode.Parse(shape.EffectiveSchema.GetRawText())!.AsObject();

        schema["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
        Names(schema["required"]).ShouldBe(["name", "nickname", "age", "role", "address", "tags"]);

        var address = schema["properties"]!["address"]!.AsObject();
        address["additionalProperties"]!.GetValue<bool>().ShouldBeFalse();
        Names(address["required"]).ShouldBe(["city", "zip"]);
        Names(schema["properties"]!["tags"]!["items"]!["required"]).ShouldBe(["label"]);
    }

    [Fact]
    public void A_bound_moves_into_the_description_and_a_default_follows_it()
    {
        var shape = OpenAIResponseSchema.Describe(Parse(Author), strict: false);
        var properties = JsonNode.Parse(shape.EffectiveSchema.GetRawText())!["properties"]!.AsObject();

        var name = properties["name"]!.AsObject();
        name.ContainsKey("maxLength").ShouldBeFalse();
        name["description"]!.GetValue<string>().ShouldBe($"Full name{Environment.NewLine}maxLength: 20");

        var role = properties["role"]!.AsObject();
        role.ContainsKey("default").ShouldBeFalse();
        role["description"]!.GetValue<string>().ShouldBe("Default value: \"user\"");

        properties["age"]!.AsObject().ContainsKey("minimum").ShouldBeFalse();
    }

    [Fact]
    public void The_properties_the_author_left_optional_are_listed_with_their_pointer_but_not_the_nullable_ones()
    {
        var shape = OpenAIResponseSchema.Describe(Parse(Author), strict: false);

        shape.OptionalMadeRequired.ShouldBe(
        [
            "#/properties/address",
            "#/properties/address/properties/zip",
            "#/properties/nickname",
            "#/properties/role",
            "#/properties/tags",
            "#/properties/tags/items/properties/label",
        ]);
    }

    [Fact]
    public void The_bounds_that_are_not_sent_are_listed_with_the_pointer_of_their_schema()
    {
        var shape = OpenAIResponseSchema.Describe(Parse(Author), strict: false);

        shape.KeywordsNotEnforced.ShouldBe(
        [
            "#/properties/address/properties/zip: pattern",
            "#/properties/age: minimum",
            "#/properties/name: maxLength",
            "#/properties/tags: minItems",
        ]);
    }

    [Fact]
    public void A_property_named_like_a_keyword_is_not_a_bound()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"format":{"type":["string","null"]},"pattern":{"type":["string","null"]}}}"""),
            strict: false);

        shape.KeywordsNotEnforced.ShouldBeEmpty();
        shape.OptionalMadeRequired.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"enum":["a",null]}""")]
    [InlineData("""{"const":null}""")]
    [InlineData("""{"anyOf":[{"type":"string"},{"type":"null"}]}""")]
    [InlineData("""{"oneOf":[{"type":"integer"},{"type":"null"}]}""")]
    [InlineData("""{"type":"null"}""")]
    [InlineData("{}")]
    [InlineData("true")]
    public void A_property_that_lets_null_through_is_not_reported(string property)
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse($$$"""{"type":"object","properties":{"note":{{{property}}}}}"""), strict: false);

        shape.OptionalMadeRequired.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"enum":["a","b"]}""")]
    [InlineData("""{"const":"a"}""")]
    [InlineData("""{"anyOf":[{"type":"string"},{"type":"integer"}]}""")]
    [InlineData("""{"type":"string"}""")]
    [InlineData("""{"$ref":"#/$defs/Note"}""")]
    public void A_property_that_does_not_let_null_through_is_reported(string property)
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse($$$"""{"type":"object","properties":{"note":{{{property}}}}}"""), strict: false);

        shape.OptionalMadeRequired.ShouldBe(["#/properties/note"]);
    }

    [Fact]
    public void A_oneOf_alternative_stays_in_the_effective_schema_and_is_walked()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"answer":{"oneOf":[{"type":"object","properties":{"text":{"type":"string"}}},{"type":"string","maxLength":4}]}},"required":["answer"]}"""),
            strict: false);

        var answer = JsonNode.Parse(shape.EffectiveSchema.GetRawText())!["properties"]!["answer"]!.AsObject();

        answer["oneOf"]!.AsArray().Count.ShouldBe(2);
        shape.OptionalMadeRequired.ShouldBe(["#/properties/answer/oneOf/0/properties/text"]);
        shape.KeywordsNotEnforced.ShouldBe(["#/properties/answer/oneOf/1: maxLength"]);
    }

    [Fact]
    public void A_pointer_escapes_the_slash_and_the_tilde_of_a_property_name()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"a/b":{"type":"string"},"c~d":{"type":"string"}}}"""), strict: false);

        shape.OptionalMadeRequired.ShouldBe(["#/properties/a~1b", "#/properties/c~0d"]);
    }

    [Fact]
    public void A_property_named_like_an_array_position_keeps_its_name()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"[0]":{"type":"string"}}}"""), strict: false);

        shape.OptionalMadeRequired.ShouldBe(["#/properties/[0]"]);
    }

    [Fact]
    public void A_schema_without_properties_has_nothing_to_report_and_the_root_pointer_is_a_hash()
    {
        var shape = OpenAIResponseSchema.Describe(Parse("""{"type":"object","maxProperties":3}"""), strict: false);

        shape.OptionalMadeRequired.ShouldBeEmpty();
        shape.KeywordsNotEnforced.ShouldBe(["#: maxProperties"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_strict_flag_is_carried_and_changes_nothing_else(bool strict)
    {
        var shape = OpenAIResponseSchema.Describe(Parse(Author), strict);
        var other = OpenAIResponseSchema.Describe(Parse(Author), !strict);

        shape.Strict.ShouldBe(strict);
        shape.EffectiveSchema.GetRawText().ShouldBe(other.EffectiveSchema.GetRawText());
        shape.OptionalMadeRequired.ShouldBe(other.OptionalMadeRequired);
        shape.KeywordsNotEnforced.ShouldBe(other.KeywordsNotEnforced);
    }

    [Fact]
    public void The_author_schema_is_not_changed()
    {
        var schema = Parse(Author);
        var before = schema.GetRawText();

        _ = OpenAIResponseSchema.Describe(schema, strict: true);

        schema.GetRawText().ShouldBe(before);
    }

    [Fact]
    public void A_schema_the_library_cannot_transform_is_an_argument_exception()
    {
        Should.Throw<ArgumentException>(
            () => OpenAIResponseSchema.Describe(Parse("""{"type":"object","properties":{"x":"string"}}"""), strict: false));
    }

    [Fact]
    public void The_refusals_of_strict_mode_are_listed_only_for_the_service_they_were_measured_against()
    {
        var schema = Parse("""{"type":"object","properties":{"answer":{"oneOf":[{"type":"string"},{"type":"integer"}]}},"required":["answer"]}""");

        OpenAIResponseSchema.Describe(schema, strict: true, measuredService: true).StrictUnsupported
            .ShouldBe(["#/properties/answer: oneOf"]);
        OpenAIResponseSchema.Describe(schema, strict: true, measuredService: false).StrictUnsupported.ShouldBeEmpty();
        OpenAIResponseSchema.Describe(schema, strict: true).StrictUnsupported.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("allOf", """{"allOf":[{"type":"string"}]}""")]
    [InlineData("if", """{"type":"object","properties":{"x":{"type":"string"}},"if":{"properties":{"x":{"const":"a"}}}}""")]
    [InlineData("then", """{"type":"object","properties":{"x":{"type":"string"}},"then":{}}""")]
    [InlineData("dependentRequired", """{"type":"object","properties":{"x":{"type":"string"}},"dependentRequired":{"x":["x"]}}""")]
    [InlineData("dependentSchemas", """{"type":"object","properties":{"x":{"type":"string"}},"dependentSchemas":{"x":{}}}""")]
    public void Each_keyword_the_service_answered_with_not_permitted_is_listed(string keyword, string property)
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse($$$"""{"type":"object","properties":{"note":{{{property}}}},"required":["note"]}"""),
            strict: true,
            measuredService: true);

        shape.StrictUnsupported.ShouldBe([$"#/properties/note: {keyword}"]);
    }

    [Fact]
    public void A_free_form_object_is_listed_because_strict_mode_needs_additionalProperties_false()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"meta":{"type":"object"},"tags":{"type":"object","additionalProperties":{"type":"string"}}},"required":["meta","tags"]}"""),
            strict: true,
            measuredService: true);

        shape.StrictUnsupported.ShouldBe(["#/properties/meta: additionalProperties", "#/properties/tags: additionalProperties"]);
    }

    [Fact]
    public void An_object_with_its_properties_spelled_out_and_anyOf_are_not_listed()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"a":{"anyOf":[{"type":"string"},{"type":"null"}]},"b":{"type":"object","properties":{}},"c":{"type":["string","null"]}},"required":["a","b","c"]}"""),
            strict: true,
            measuredService: true);

        shape.StrictUnsupported.ShouldBeEmpty();
    }

    [Fact]
    public void The_walk_reaches_definitions_the_transform_leaves_alone_and_ignores_property_names()
    {
        var shape = OpenAIResponseSchema.Describe(
            Parse("""{"type":"object","properties":{"oneOf":{"type":"string"},"item":{"$ref":"#/$defs/Item"}},"required":["oneOf","item"],"$defs":{"Item":{"type":"object","properties":{"x":{"allOf":[{"type":"string"}]}},"required":["x"]}}}"""),
            strict: true,
            measuredService: true);

        // The transform does not reach $defs, so the service gets that object open as well.
        shape.StrictUnsupported.ShouldBe(["#/$defs/Item/properties/x: allOf", "#/$defs/Item: additionalProperties"]);
    }

    [Fact]
    public void A_default_beside_a_description_that_is_not_text_is_an_invalid_operation_the_library_throws_too()
    {
        // The library reads the description as text when it moves the default into it, and throws otherwise. The
        // copy fails the same way, so the validator has to survive it (ResponseSchemaReportTests).
        Should.Throw<InvalidOperationException>(
            () => OpenAIResponseSchema.Describe(
                Parse("""{"type":"object","properties":{"x":{"type":"string","default":"a","description":5}}}"""),
                strict: false));
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string[] Names(JsonNode? array)
        => array!.AsArray().Select(static item => item!.GetValue<string>()).ToArray();
}
