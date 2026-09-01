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
}
