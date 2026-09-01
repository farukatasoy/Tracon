using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Verifies 130.1/130.3: standard <c>System.ComponentModel.DataAnnotations</c> attributes
/// reach the generated JSON Schema as <c>minimum</c>/<c>maximum</c>/<c>minLength</c>/
/// <c>maxLength</c>/<c>minItems</c>/<c>maxItems</c>/<c>pattern</c>, in a fixed key order,
/// independent of the build's current culture.
/// </summary>
public sealed class ToolSchemaConstraintTests
{
    [Fact]
    public void A_Range_attribute_on_an_integer_parameter_produces_minimum_and_maximum()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_count", "Sets a count.")]
                public static void SetCount([Description("The count.")] [Range(1, 100)] int count) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("count");
        node.GetProperty("minimum").GetInt32().ShouldBe(1);
        node.GetProperty("maximum").GetInt32().ShouldBe(100);
    }

    [Fact]
    public void A_MinLength_attribute_on_a_string_parameter_produces_minLength()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [MinLength(3)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("name");
        node.GetProperty("minLength").GetInt32().ShouldBe(3);
        node.TryGetProperty("minItems", out _).ShouldBeFalse();
    }

    /// <summary>
    /// 130.3: a length constraint on an array targets the array itself, never its
    /// element - <c>[MinLength(2)] string[] tags</c> means "at least two tags", not
    /// "each tag at least two characters".
    /// </summary>
    [Fact]
    public void A_MinLength_attribute_on_an_array_parameter_produces_minItems_not_minLength()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("tag", "Tags.")]
                public static void Tag([Description("The tags.")] [MinLength(2)] string[] tags) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("tags");
        node.GetProperty("minItems").GetInt32().ShouldBe(2);
        node.TryGetProperty("minLength", out _).ShouldBeFalse();
        node.GetProperty("items").TryGetProperty("minLength", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_MaxLength_attribute_on_a_string_parameter_produces_maxLength()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [MaxLength(20)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("name");
        node.GetProperty("maxLength").GetInt32().ShouldBe(20);
    }

    [Fact]
    public void A_MaxLength_attribute_on_an_array_parameter_produces_maxItems()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("tag", "Tags.")]
                public static void Tag([Description("The tags.")] [MaxLength(5)] string[] tags) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("tags");
        node.GetProperty("maxItems").GetInt32().ShouldBe(5);
    }

    [Fact]
    public void A_StringLength_attribute_produces_minLength_and_maxLength()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [StringLength(10, MinimumLength = 3)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("name");
        node.GetProperty("minLength").GetInt32().ShouldBe(3);
        node.GetProperty("maxLength").GetInt32().ShouldBe(10);
    }

    [Fact]
    public void A_RegularExpression_attribute_produces_pattern()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_code", "Sets a code.")]
                public static void SetCode([Description("The code.")] [RegularExpression(@"^[A-Z]{3}-\d{4}$")] string code) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("code");
        node.GetProperty("pattern").GetString().ShouldBe(@"^[A-Z]{3}-\d{4}$");
    }

    [Fact]
    public void A_pattern_containing_backslashes_and_quotes_survives_the_JSON_round_trip()
    {
        const string Pattern = "^\\d{3}\"$";

        var source = $$"""
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_code", "Sets a code.")]
                public static void SetCode([Description("The code.")] [RegularExpression({{ToVerbatimLiteral(Pattern)}})] string code) { }
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        result.Diagnostics.ShouldNotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("code");
        node.GetProperty("pattern").GetString().ShouldBe(Pattern);
    }

    [Fact]
    public void Conflicting_MinLength_and_StringLength_the_narrower_minimum_wins()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName(
                    [Description("The name.")] [MinLength(2)] [StringLength(10, MinimumLength = 3)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("name");
        node.GetProperty("minLength").GetInt32().ShouldBe(3);
    }

    [Fact]
    public void Conflicting_MaxLength_and_StringLength_the_narrower_maximum_wins()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName(
                    [Description("The name.")] [MaxLength(20)] [StringLength(10)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("name");
        node.GetProperty("maxLength").GetInt32().ShouldBe(10);
    }

    [Fact]
    public void Leaf_constraint_keys_are_written_minLength_then_maxLength_then_pattern()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_code", "Sets a code.")]
                public static void SetCode(
                    [Description("The code.")] [MinLength(1)] [MaxLength(20)] [RegularExpression("^[A-Z]+$")] string code) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        var minLengthIndex = wrapper.IndexOf("minLength", StringComparison.Ordinal);
        var maxLengthIndex = wrapper.IndexOf("maxLength", StringComparison.Ordinal);
        var patternIndex = wrapper.IndexOf("pattern", StringComparison.Ordinal);

        minLengthIndex.ShouldBeGreaterThan(0);
        maxLengthIndex.ShouldBeGreaterThan(minLengthIndex);
        patternIndex.ShouldBeGreaterThan(maxLengthIndex);
    }

    [Fact]
    public void Array_constraints_are_written_minimum_maximum_on_items_then_minItems_maxItems_on_the_array()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_values", "Sets values.")]
                public static void SetValues(
                    [Description("The values.")] [Range(1, 100)] [MinLength(2)] [MaxLength(5)] int[] values) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        var minimumIndex = wrapper.IndexOf("minimum", StringComparison.Ordinal);
        var maximumIndex = wrapper.IndexOf("maximum", StringComparison.Ordinal);
        var minItemsIndex = wrapper.IndexOf("minItems", StringComparison.Ordinal);
        var maxItemsIndex = wrapper.IndexOf("maxItems", StringComparison.Ordinal);

        minimumIndex.ShouldBeGreaterThan(0);
        maximumIndex.ShouldBeGreaterThan(minimumIndex);
        minItemsIndex.ShouldBeGreaterThan(maximumIndex);
        maxItemsIndex.ShouldBeGreaterThan(minItemsIndex);
    }

    /// <summary>
    /// 130.3: <c>minimum</c>/<c>maximum</c> are rendered with
    /// <see cref="CultureInfo.InvariantCulture"/>, independent of the generator process's
    /// current culture - a comma-decimal culture must never leak a "1,5" into the JSON text.
    /// </summary>
    [Fact]
    public void A_decimal_Range_keeps_the_invariant_decimal_separator_under_a_comma_decimal_culture()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_ratio", "Sets a ratio.")]
                public static void SetRatio([Description("The ratio.")] [Range(1.5, 2.5)] double ratio) { }
            }
            """;

        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

        GeneratorRunResult result;

        try
        {
            result = GeneratorTestHelper.Run(Source);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("ratio");
        node.GetProperty("minimum").GetRawText().ShouldBe("1.5");
        node.GetProperty("maximum").GetRawText().ShouldBe("2.5");
    }

    /// <summary>Zero is the smallest valid JSON Schema <c>nonNegativeInteger</c> - unlike a negative bound (see <c>ToolDiagnosticTests</c>), it is a legitimate length.</summary>
    [Fact]
    public void A_MaxLength_of_zero_produces_maxLength_zero()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [MaxLength(0)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("name");
        node.GetProperty("maxLength").GetInt32().ShouldBe(0);
    }

    [Fact]
    public void A_Range_spanning_the_full_Int32_domain_produces_matching_minimum_and_maximum()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_count", "Sets a count.")]
                public static void SetCount([Description("The count.")] [Range(int.MinValue, int.MaxValue)] int count) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("count");
        node.GetProperty("minimum").GetInt64().ShouldBe((long)int.MinValue);
        node.GetProperty("maximum").GetInt64().ShouldBe((long)int.MaxValue);
    }

    [Fact]
    public void An_empty_RegularExpression_pattern_produces_an_empty_pattern_string()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_code", "Sets a code.")]
                public static void SetCode([Description("The code.")] [RegularExpression("")] string code) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("code");
        node.GetProperty("pattern").GetString().ShouldBe(string.Empty);
    }

    [Fact]
    public void A_parameter_with_no_constraint_attribute_carries_no_constraint_keys()
    {
        const string Source = """
            using System.ComponentModel;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("search", "Searches.")]
                public static string Search([Description("The query.")] string query) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("query");
        node.TryGetProperty("minLength", out _).ShouldBeFalse();
        node.TryGetProperty("maxLength", out _).ShouldBeFalse();
        node.TryGetProperty("pattern", out _).ShouldBeFalse();
        node.TryGetProperty("minimum", out _).ShouldBeFalse();
        node.TryGetProperty("maximum", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Renders <paramref name="value"/> as a regular (non-raw) C# string literal, so it can
    /// be embedded as an attribute argument in generated "subject" source text regardless of
    /// embedded quotes or backslashes.
    /// </summary>
    private static string ToVerbatimLiteral(string value)
    {
        var sb = new StringBuilder("\"");

        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }

        return sb.Append('"').ToString();
    }

    /// <summary>
    /// Extracts the tool's JSON schema from the generated wrapper source and parses it,
    /// through the same two decode steps (C# string literal, then JSON) the generated code
    /// itself performs.
    /// </summary>
    private static JsonElement SchemaJson(string wrapperSource)
    {
        var tree = CSharpSyntaxTree.ParseText(wrapperSource);

        var literal = tree.GetRoot()
            .DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Select(node => node.Token.ValueText)
            .First(text => text.StartsWith("{\"type\":\"object\"", StringComparison.Ordinal));

        return JsonDocument.Parse(literal).RootElement;
    }
}
