namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Verifies 130's DoD: the same source produces a BIT-IDENTICAL generated schema across
/// two independent generator runs (two separate drivers, not the same driver's cache -
/// <see cref="IncrementalGeneratorCacheTests"/> covers the cache instead).
/// </summary>
public sealed class ToolSchemaDeterminismTests
{
    [Fact]
    public void The_same_source_produces_byte_identical_wrapper_text_across_two_independent_runs()
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
                    [Description("The name.")] [MinLength(1)] [MaxLength(20)] [RegularExpression("^[a-z]+$")] string name,
                    [Description("The count.")] [Range(1, 100)] int count,
                    [Description("The tags.")] [MinLength(2)] [MaxLength(5)] string[] tags) { }
            }
            """;

        var first = GeneratorTestHelper.Run(Source);
        var second = GeneratorTestHelper.Run(Source);

        first.SingleWrapperFile().ShouldBe(second.SingleWrapperFile());
    }

    /// <summary>135's DoD: an object parameter's schema is just as bit-identical across two independent runs.</summary>
    [Fact]
    public void An_object_parameters_schema_is_byte_identical_across_two_independent_runs()
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

        var first = GeneratorTestHelper.Run(Source);
        var second = GeneratorTestHelper.Run(Source);

        first.SingleWrapperFile().ShouldBe(second.SingleWrapperFile());
    }
}
