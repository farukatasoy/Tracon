using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class AgentParameterValidatorTests
{
    [Fact]
    public void Definition_with_empty_schema_is_never_checked_against_its_text()
    {
        // An agent that never opts into this feature keeps free-form text -
        // including a coincidental "{{...}}" substring.
        var definition = TestData.Definition() with { Instructions = "Say {{literally}} out loud." };

        AgentParameterValidator.ValidateSchema(definition).ShouldBeEmpty();
    }

    [Fact]
    public void Undeclared_placeholder_in_instructions_is_rejected()
    {
        var definition = TestData.Definition() with
        {
            Instructions = "Hello {{customer}}.",
            Parameters = [Param("other")],
        };

        var errors = AgentParameterValidator.ValidateSchema(definition);

        errors.ShouldContain(error => error.Contains("customer", StringComparison.Ordinal));
    }

    [Fact]
    public void Declared_placeholder_used_in_instructions_passes()
    {
        var definition = TestData.Definition() with
        {
            Instructions = "Hello {{customer}}.",
            Parameters = [Param("customer")],
        };

        AgentParameterValidator.ValidateSchema(definition).ShouldBeEmpty();
    }

    [Fact]
    public void Schema_entry_unused_in_the_text_is_not_an_error()
    {
        var definition = TestData.Definition() with
        {
            Instructions = "No placeholders here.",
            Parameters = [Param("unused")],
        };

        AgentParameterValidator.ValidateSchema(definition).ShouldBeEmpty();
    }

    [Fact]
    public void Duplicate_parameter_name_is_rejected()
    {
        var definition = TestData.Definition() with
        {
            Parameters = [Param("customer"), Param("customer")],
        };

        var errors = AgentParameterValidator.ValidateSchema(definition);

        errors.ShouldContain(error => error.Contains("declared more than once", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("a-b")]
    [InlineData("a b")]
    [InlineData("")]
    public void Invalid_identifier_is_rejected(string name)
    {
        var definition = TestData.Definition() with { Parameters = [Param(name)] };

        var errors = AgentParameterValidator.ValidateSchema(definition);

        errors.ShouldContain(error => error.Contains("valid identifier", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_culture_variant_is_checked_against_the_same_schema()
    {
        // 🚨 A variant that carries an extra placeholder must fail even
        // though ANOTHER variant does not use it - otherwise one language
        // works and another falls over only at run time.
        var definition = TestData.Definition() with
        {
            Instructions = "Hello {{customer}}.",
            Parameters = [Param("customer")],
            InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["en"] = "Hello {{customer}}.",
                ["tr"] = "Merhaba {{customer}}, {{extra}}.",
            },
        };

        var errors = AgentParameterValidator.ValidateSchema(definition);

        errors.ShouldContain(error => error.Contains("extra", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_required_parameter_with_no_default_is_reported()
    {
        var schema = new[] { Param("customer", required: true) };

        var result = AgentParameterValidator.ValidateValues(schema, values: null);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Code == AgentParameterValidator.MissingParameterCode && error.ParameterName == "customer");
    }

    [Fact]
    public void Missing_required_parameter_with_a_default_is_not_reported()
    {
        var schema = new[] { Param("tone", required: true, defaultValue: "formal") };

        var result = AgentParameterValidator.ValidateValues(schema, values: null);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Missing_optional_parameter_is_not_reported()
    {
        var schema = new[] { Param("nickname", required: false) };

        var result = AgentParameterValidator.ValidateValues(schema, values: null);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Unknown_value_is_never_silently_dropped()
    {
        var schema = new[] { Param("customer") };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme", ["typo"] = "oops" };

        var result = AgentParameterValidator.ValidateValues(schema, values);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Code == AgentParameterValidator.UnknownParameterCode && error.ParameterName == "typo");
    }

    [Fact]
    public void Matching_values_pass()
    {
        var schema = new[] { Param("customer", required: true) };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" };

        AgentParameterValidator.ValidateValues(schema, values).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Value_over_the_configured_limit_is_rejected()
    {
        var schema = new[] { Param("customer") };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = new string('a', 10) };

        var result = AgentParameterValidator.ValidateValues(schema, values, maxValueLength: 9);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error =>
            error.Code == AgentParameterValidator.ValueTooLongCode && error.ParameterName == "customer");
    }

    [Fact]
    public void Value_at_exactly_the_limit_passes()
    {
        var schema = new[] { Param("customer") };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = new string('a', 9) };

        AgentParameterValidator.ValidateValues(schema, values, maxValueLength: 9).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void No_limit_supplied_never_rejects_a_long_value()
    {
        // The default (null) preserves the behavior of every caller written
        // before this check existed - no length is enforced unless a caller
        // opts in.
        var schema = new[] { Param("customer") };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = new string('a', 1_000_000) };

        AgentParameterValidator.ValidateValues(schema, values).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Limit_is_measured_in_UTF8_bytes_not_characters()
    {
        // A multi-byte character must count its actual encoded size, not 1
        // per char - otherwise the limit under-counts non-ASCII input.
        var schema = new[] { Param("customer") };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "日本語" };

        // 3 characters, but 9 UTF-8 bytes (3 bytes each).
        AgentParameterValidator.ValidateValues(schema, values, maxValueLength: 8).IsValid.ShouldBeFalse();
        AgentParameterValidator.ValidateValues(schema, values, maxValueLength: 9).IsValid.ShouldBeTrue();
    }

    private static AgentParameter Param(string name, bool required = false, string? defaultValue = null)
        => new() { Name = name, Kind = AgentParameterKind.Text, Required = required, DefaultValue = defaultValue };
}
