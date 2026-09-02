using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Verifies 135.1/135.2: a supported object type (a public record or class with a single
/// public constructor) and an array of that type produce a nested JSON Schema node, and
/// the existing constraint/description handling carries over to an object member exactly
/// as it does at the top level.
/// </summary>
public sealed class ToolSchemaObjectTests
{
    [Fact]
    public void An_object_parameter_produces_a_nested_object_schema_node()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using AgentPrism;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("rubric");
        node.GetProperty("type").GetString().ShouldBe("object");

        var properties = node.GetProperty("properties");
        properties.GetProperty("Name").GetProperty("type").GetString().ShouldBe("string");
        properties.GetProperty("Weight").GetProperty("type").GetString().ShouldBe("integer");

        var required = node.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.ShouldContain(name => string.Equals(name, "Name", StringComparison.Ordinal));
        required.ShouldContain(name => string.Equals(name, "Weight", StringComparison.Ordinal));

        node.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void An_object_array_parameter_produces_an_array_of_object_schema_nodes()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using AgentPrism;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool("score_all", "Scores a list of rubrics.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void ScoreAll([Description("The rubrics.")] IReadOnlyList<Rubric> rubrics) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var node = SchemaJson(result.SingleWrapperFile()).GetProperty("properties").GetProperty("rubrics");
        node.GetProperty("type").GetString().ShouldBe("array");

        var items = node.GetProperty("items");
        items.GetProperty("type").GetString().ShouldBe("object");
        items.GetProperty("properties").GetProperty("Name").GetProperty("type").GetString().ShouldBe("string");
    }

    /// <summary>135.2: the existing constraint handling applies to an object member exactly as it does at the top level.</summary>
    [Fact]
    public void A_Range_attribute_on_an_object_member_produces_minimum_and_maximum_on_the_member_node()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;
            using AgentPrism;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] [Range(1, 5)] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var member = SchemaJson(result.SingleWrapperFile())
            .GetProperty("properties").GetProperty("rubric")
            .GetProperty("properties").GetProperty("Weight");

        member.GetProperty("minimum").GetInt32().ShouldBe(1);
        member.GetProperty("maximum").GetInt32().ShouldBe(5);
    }

    /// <summary>An object member's <c>[Description]</c> reaches the member's own schema node, not the parent's.</summary>
    [Fact]
    public void A_Description_attribute_on_an_object_member_produces_a_description_on_the_member_node()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using AgentPrism;

            namespace MyApp;

            public sealed record Rubric([Description("The rubric's name.")] string Name, int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var member = SchemaJson(result.SingleWrapperFile())
            .GetProperty("properties").GetProperty("rubric")
            .GetProperty("properties").GetProperty("Name");

        member.GetProperty("description").GetString().ShouldBe("The rubric's name.");
    }

    /// <summary>A member with a default value is optional - absent from the object node's "required" array.</summary>
    [Fact]
    public void An_object_member_with_a_default_value_is_not_required()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using AgentPrism;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] int Weight = 1);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var required = SchemaJson(result.SingleWrapperFile())
            .GetProperty("properties").GetProperty("rubric")
            .GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();

        required.ShouldContain(name => string.Equals(name, "Name", StringComparison.Ordinal));
        required.ShouldNotContain(name => string.Equals(name, "Weight", StringComparison.Ordinal));
    }

    /// <summary>135.5: binding deserializes the whole object through the tool's own <c>JsonSerializerContext</c>, never through reflection.</summary>
    [Fact]
    public void An_object_parameter_binds_through_the_tools_JsonSerializerContext()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using AgentPrism;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("global::System.Text.Json.JsonSerializer.Deserialize(e, global::MyApp.ToolJsonContext.Default.GetTypeInfo(typeof(global::MyApp.Rubric))!)");
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
