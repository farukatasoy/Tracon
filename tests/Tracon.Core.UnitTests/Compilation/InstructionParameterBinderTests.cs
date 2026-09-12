namespace Tracon.Core.UnitTests.Compilation;

public sealed class InstructionParameterBinderTests
{
    [Fact]
    public void Value_is_substituted_for_a_declared_placeholder()
    {
        var schema = new[] { Text("customer") };

        var result = InstructionParameterBinder.Bind(
            "Hello {{customer}}, welcome.",
            schema,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" });

        result.ShouldBe("Hello Acme, welcome.");
    }

    [Fact]
    public void Missing_value_falls_back_to_default()
    {
        var schema = new[] { Text("tone", defaultValue: "formal") };

        var result = InstructionParameterBinder.Bind("Use a {{tone}} tone.", schema, values: null);

        result.ShouldBe("Use a formal tone.");
    }

    [Fact]
    public void Missing_value_with_no_default_becomes_empty_string()
    {
        var schema = new[] { Text("nickname") };

        var result = InstructionParameterBinder.Bind("Hi {{nickname}}!", schema, values: null);

        result.ShouldBe("Hi !");
    }

    [Fact]
    public void Undeclared_placeholder_is_left_untouched()
    {
        var schema = new[] { Text("customer") };

        var result = InstructionParameterBinder.Bind(
            "{{customer}} said {{unrelated}}.",
            schema,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" });

        result.ShouldBe("Acme said {{unrelated}}.");
    }

    [Fact]
    public void Empty_schema_leaves_text_unchanged()
    {
        var result = InstructionParameterBinder.Bind("{{anything}}", [], values: null);

        result.ShouldBe("{{anything}}");
    }

    [Fact]
    public void Null_instructions_stay_null()
        => InstructionParameterBinder.Bind(null, [Text("x")], null).ShouldBeNull();

    [Fact]
    public void Substitution_is_single_pass()
    {
        // 🚨 A value that itself contains a placeholder must never be
        // substituted a second time - otherwise a user-supplied value could
        // smuggle in another parameter's substitution, or recurse forever on
        // a self-referential value.
        var schema = new[] { Text("a"), Text("b") };

        var result = InstructionParameterBinder.Bind(
            "{{a}}",
            schema,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "{{b}}", ["b"] = "should-not-appear" });

        result.ShouldBe("{{b}}");
    }

    [Fact]
    public void Value_quoted_in_the_template_is_JSON_escaped()
    {
        var schema = new[] { Text("customer") };

        var result = InstructionParameterBinder.Bind(
            """{"customer": "{{customer}}"}""",
            schema,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "a\"b\\c" });

        result.ShouldBe("""{"customer": "a\"b\\c"}""");

        // The produced text must itself be valid JSON.
        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(result!));
    }

    [Fact]
    public void Value_not_quoted_in_the_template_is_substituted_verbatim()
    {
        var schema = new[] { Text("customer") };

        var result = InstructionParameterBinder.Bind(
            "Customer: {{customer}}",
            schema,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "a\"b\\c" });

        result.ShouldBe("Customer: a\"b\\c");
    }

    [Fact]
    public void Control_characters_are_escaped_inside_a_quoted_placeholder()
    {
        var schema = new[] { Text("note") };

        var result = InstructionParameterBinder.Bind(
            """{"note": "{{note}}"}""",
            schema,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["note"] = "line1\nline2\ttabbed" });

        result.ShouldBe("""{"note": "line1\nline2\ttabbed"}""");
        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(result!));
    }

    private static AgentParameter Text(string name, string? defaultValue = null)
        => new() { Name = name, Kind = AgentParameterKind.Text, DefaultValue = defaultValue };
}
