using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a user may enter for the fields an MCP server asked for: each wrong value is a message the
/// user can act on, never a tool call the server then fails.
/// </summary>
public sealed class ToolInputSchemaValidatorTests
{
    private static readonly JsonElement Schema = Parse("""
        {
          "type": "object",
          "properties": {
            "name":   { "type": "string", "minLength": 2, "maxLength": 5 },
            "age":    { "type": "integer", "minimum": 0, "maximum": 150 },
            "score":  { "type": "number", "minimum": 0.5 },
            "agree":  { "type": "boolean" },
            "color":  { "type": "string", "enum": ["red", "green"] },
            "size":   { "type": "string", "oneOf": [ { "const": "s", "title": "Small" }, { "const": "l", "title": "Large" } ] },
            "tags":   { "type": "array", "minItems": 1, "maxItems": 2, "items": { "type": "string", "enum": ["a", "b", "c"] } },
            "extras": { "type": "array", "items": { "anyOf": [ { "const": "x" }, { "const": "y" } ] } }
          },
          "required": ["name"]
        }
        """);

    [Fact]
    public void Values_that_fit_the_schema_pass()
    {
        Validate(("name", "Ada"), ("age", 36), ("score", 1.5), ("agree", true), ("color", "red"), ("size", "l"), ("tags", new[] { "a", "b" }), ("extras", new[] { "x" }))
            .ShouldBeNull();
    }

    [Fact]
    public void Only_the_required_fields_are_needed()
    {
        Validate(("name", "Ada")).ShouldBeNull();
    }

    [Fact]
    public void A_required_field_left_out_is_named()
    {
        Validate(("age", 3)).ShouldNotBeNull().ShouldContain("'name' is required");
        ToolInputSchemaValidator.Validate(Schema, content: null).ShouldNotBeNull().ShouldContain("'name' is required");
    }

    [Fact]
    public void A_field_the_server_did_not_ask_for_is_refused_not_dropped()
    {
        Validate(("name", "Ada"), ("nickname", "A")).ShouldNotBeNull().ShouldContain("'nickname' is not a field");
    }

    [Theory]
    [InlineData("name", 7, "must be text")]
    [InlineData("name", "A", "at least 2")]
    [InlineData("name", "Adalovelace", "at most 5")]
    [InlineData("age", "36", "whole number")]
    [InlineData("age", 3.5, "whole number")]
    [InlineData("age", -1, "at least 0")]
    [InlineData("age", 151, "at most 150")]
    [InlineData("score", 0.1, "at least 0.5")]
    [InlineData("score", "high", "must be a number")]
    [InlineData("agree", "yes", "true or false")]
    [InlineData("color", "blue", "one of: red, green")]
    [InlineData("size", "m", "one of: s, l")]
    public void A_value_that_does_not_fit_its_field_is_refused_with_the_reason(string field, object value, string reason)
    {
        var fields = string.Equals(field, "name", StringComparison.Ordinal)
            ? new[] { (field, value) }
            : new[] { ("name", (object)"Ada"), (field, value) };

        Validate(fields).ShouldNotBeNull().ShouldContain(reason);
    }

    [Theory]
    [InlineData(new string[0], "at least 1")]
    [InlineData(new[] { "a", "b", "c" }, "at most 2")]
    [InlineData(new[] { "z" }, "only hold: a, b, c")]
    public void A_list_is_checked_for_its_size_and_its_options(string[] value, string reason)
    {
        Validate(("name", "Ada"), ("tags", value)).ShouldNotBeNull().ShouldContain(reason);
    }

    [Fact]
    public void A_list_of_the_wrong_kind_is_refused()
    {
        Validate(("name", "Ada"), ("tags", "a")).ShouldNotBeNull().ShouldContain("must be a list");
        Validate(("name", "Ada"), ("tags", new[] { 1 })).ShouldNotBeNull().ShouldContain("list of text");
    }

    [Fact]
    public void A_value_over_the_field_limit_is_refused_before_anything_else_is_read()
    {
        Validate(("name", new string('x', ToolInputSchemaValidator.MaxFieldLength + 1)))
            .ShouldNotBeNull().ShouldContain("larger than");
    }

    [Fact]
    public void Values_over_the_total_limit_together_are_refused()
    {
        var wide = new string('x', ToolInputSchemaValidator.MaxFieldLength - 100);
        var schema = Parse("""{"type":"object","properties":{"a":{"type":"string"},"b":{"type":"string"},"c":{"type":"string"},"d":{"type":"string"},"e":{"type":"string"},"f":{"type":"string"},"g":{"type":"string"},"h":{"type":"string"},"i":{"type":"string"}}}""");

        var content = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" }
            .ToDictionary(static name => name, _ => JsonSerializer.SerializeToElement(wide), StringComparer.Ordinal);

        ToolInputSchemaValidator.Validate(schema, content).ShouldNotBeNull().ShouldContain("together");
    }

    [Fact]
    public void A_schema_without_properties_takes_no_fields_and_an_empty_answer()
    {
        var empty = Parse("""{"type":"object"}""");

        ToolInputSchemaValidator.Validate(empty, content: null).ShouldBeNull();
        ToolInputSchemaValidator.Validate(empty, new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["x"] = JsonSerializer.SerializeToElement(1) })
            .ShouldNotBeNull().ShouldContain("'x' is not a field");
    }

    [Fact]
    public void A_schema_that_is_not_an_object_takes_no_fields()
    {
        ToolInputSchemaValidator.Validate(default, content: null).ShouldBeNull();
        ToolInputSchemaValidator.Validate(Parse("[]"), content: null).ShouldBeNull();
    }

    [Fact]
    public void A_property_the_check_does_not_understand_takes_any_value_of_a_known_type()
    {
        var odd = Parse("""{"type":"object","properties":{"x":{"type":"object"},"y":{}}}""");

        ToolInputSchemaValidator.Validate(odd, new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["x"] = JsonSerializer.SerializeToElement(new { any = 1 }),
            ["y"] = JsonSerializer.SerializeToElement("z"),
        }).ShouldBeNull();
    }

    private static string? Validate(params (string Name, object Value)[] fields)
        => ToolInputSchemaValidator.Validate(
            Schema,
            fields.ToDictionary(static field => field.Name, static field => JsonSerializer.SerializeToElement(field.Value), StringComparer.Ordinal));

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }
}
