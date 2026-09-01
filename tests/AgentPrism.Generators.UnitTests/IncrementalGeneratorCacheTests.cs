using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Verifies 130.4: <c>ParameterConstraints</c> only carries primitive fields, so it
/// participates in <c>ParameterModel</c>'s incremental-generator value equality (52.5)
/// automatically. A constraint-only change between two runs of the SAME driver must
/// invalidate the cached step and reach the second run's schema - the same pattern
/// <c>ToolSchemaDescriptionTests</c> uses for <c>Description</c> (125).
/// </summary>
public sealed class IncrementalGeneratorCacheTests
{
    [Fact]
    public void Changing_only_a_constraint_value_produces_a_fresh_schema_not_a_stale_cached_one()
    {
        const string First = """
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

        const string Second = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_count", "Sets a count.")]
                public static void SetCount([Description("The count.")] [Range(1, 200)] int count) { }
            }
            """;

        var (first, second) = GeneratorTestHelper.RunIncremental(First, Second);

        first.SingleWrapperFile().ShouldContain("\\\"maximum\\\":100");
        second.SingleWrapperFile().ShouldContain("\\\"maximum\\\":200");
    }

    /// <summary>
    /// The reverse direction: when NOTHING about the marked method changes (a
    /// constraint included), the tool-candidate step must stay cached - proving the new
    /// <c>Constraints</c> field does not defeat incrementality by, for example, capturing
    /// something order- or reference-dependent.
    /// </summary>
    [Fact]
    public void The_tool_candidate_step_stays_cached_when_a_constrained_parameter_does_not_change()
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

            internal static class Unrelated
            {
                public const int Value = 1;
            }
            """;

        const string SourceWithUnrelatedChange = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_count", "Sets a count.")]
                public static void SetCount([Description("The count.")] [Range(1, 100)] int count) { }
            }

            internal static class Unrelated
            {
                public const int Value = 2;
            }
            """;

        var (_, second) = GeneratorTestHelper.RunIncremental(Source, SourceWithUnrelatedChange);

        var reasons = second.StepReasons(ToolRegistrationGenerator.TrackingNames.ToolCandidates);

        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }
}
