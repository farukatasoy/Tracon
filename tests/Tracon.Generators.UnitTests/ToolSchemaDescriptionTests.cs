using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tracon.Generators.UnitTests;

/// <summary>
/// Verifies 125.1/125.2: <c>[Description]</c> on a tool parameter reaches the generated
/// JSON schema, is placed correctly for an array parameter, survives JSON-unsafe text,
/// and its absence is reported (TRC0009) without blocking generation.
/// </summary>
public sealed class ToolSchemaDescriptionTests
{
    [Fact]
    public void A_Description_attribute_on_a_scalar_parameter_reaches_the_schema()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class OrderTools
            {
                [TraconTool("submit_order", "Submits an order to the fulfillment system.")]
                public static string SubmitOrder(
                    [Description("The identifier of the order to submit.")] string orderId)
                    => orderId;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("TRC0009").ShouldBeEmpty();

        var schema = SchemaJson(result.SingleWrapperFile());
        schema.GetProperty("properties").GetProperty("orderId").GetProperty("description").GetString()
            .ShouldBe("The identifier of the order to submit.");
    }

    [Fact]
    public void An_array_parameters_description_is_written_on_the_array_node_not_inside_items()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("tag", "Tags.")]
                public static void Tag([Description("The tags to apply.")] string[] tags) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var tagsNode = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("tags");

        tagsNode.GetProperty("description").GetString().ShouldBe("The tags to apply.");
        tagsNode.GetProperty("items").TryGetProperty("description", out _).ShouldBeFalse(
            "the description belongs to the parameter, not the array element leaf - it must not be nested under 'items'");
    }

    [Fact]
    public void Quotes_and_backslashes_in_a_description_round_trip_through_the_generated_schema()
    {
        const string Description = """A "quoted" path like C:\temp.""";

        var source = $$"""
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search([Description({{ToVerbatimLiteral(Description)}})] string query) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        result.Diagnostics.ShouldNotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        var schema = SchemaJson(result.SingleWrapperFile());
        schema.GetProperty("properties").GetProperty("query").GetProperty("description").GetString()
            .ShouldBe(Description);
    }

    [Fact]
    public void A_very_long_description_round_trips_through_the_generated_schema()
    {
        var description = string.Concat(Enumerable.Repeat("A very long description sentence. ", 200));

        var source = $$"""
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search([Description({{ToVerbatimLiteral(description)}})] string query) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        result.Diagnostics.ShouldNotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        var schema = SchemaJson(result.SingleWrapperFile());
        schema.GetProperty("properties").GetProperty("query").GetProperty("description").GetString()
            .ShouldBe(description);
    }

    [Fact]
    public void Unicode_and_multi_line_text_in_a_description_round_trip_through_the_generated_schema()
    {
        const string Description = "An order id, e.g. café-42 or 東京.\nSecond line.\tTabbed.";

        var source = $$"""
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search([Description({{ToVerbatimLiteral(Description)}})] string query) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(source);

        result.Diagnostics.ShouldNotContain(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);

        var schema = SchemaJson(result.SingleWrapperFile());
        schema.GetProperty("properties").GetProperty("query").GetProperty("description").GetString()
            .ShouldBe(Description);
    }

    [Fact]
    public void A_parameter_without_a_Description_attribute_produces_no_description_and_a_warning()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search(string query) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("TRC0009");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        // A warning does not block generation.
        var schema = SchemaJson(result.SingleWrapperFile());
        schema.GetProperty("properties").GetProperty("query").TryGetProperty("description", out _).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_Description_is_treated_as_missing()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search([Description("")] string query) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("TRC0009").Count.ShouldBe(1);

        var schema = SchemaJson(result.SingleWrapperFile());
        schema.GetProperty("properties").GetProperty("query").TryGetProperty("description", out _).ShouldBeFalse();
    }

    [Fact]
    public void A_CancellationToken_parameter_never_needs_a_description()
    {
        const string Source = """
            using System.Threading;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("wait", "Waits.")]
                public static void Wait(CancellationToken cancellationToken) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("TRC0009").ShouldBeEmpty();
    }

    /// <summary>
    /// 🚨 <c>ParameterModel</c> is an incremental-generator model, cached by record
    /// equality (positional record - the new <c>Description</c> field enters equality
    /// automatically). This proves it in practice: a description-only change between two
    /// runs of the SAME driver must reach the second run's schema, not a stale cached one.
    /// </summary>
    [Fact]
    public void Changing_only_the_description_produces_a_fresh_schema_not_a_stale_cached_one()
    {
        const string First = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search([Description("v1")] string query) => query;
            }
            """;

        const string Second = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("search", "Searches.")]
                public static string Search([Description("v2")] string query) => query;
            }
            """;

        var (first, second) = GeneratorTestHelper.RunIncremental(First, Second);

        SchemaJson(first.SingleWrapperFile()).GetProperty("properties").GetProperty("query").GetProperty("description").GetString()
            .ShouldBe("v1");
        SchemaJson(second.SingleWrapperFile()).GetProperty("properties").GetProperty("query").GetProperty("description").GetString()
            .ShouldBe("v2");
    }

    /// <summary>
    /// Renders <paramref name="value"/> as a regular (non-raw) C# string literal, so it can
    /// be embedded as an attribute argument in generated "subject" source text regardless of
    /// embedded quotes, backslashes, or newlines. A raw string literal is not used here: its
    /// delimiters must sit on their own line once the content contains a newline, which this
    /// helper cannot guarantee for caller-supplied text.
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
    /// Extracts the tool's JSON schema from the generated wrapper source and parses it.
    /// Reads through Roslyn (<see cref="LiteralExpressionSyntax.Token"/>'s decoded
    /// <c>ValueText</c>) rather than hand-decoding the doubly-escaped (JSON, then C#
    /// string literal) text, and then through <see cref="JsonDocument"/> - together, the
    /// exact two decode steps the generated code itself performs at compile time and run
    /// time, so this proves the round trip rather than a hand-computed guess at it.
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
