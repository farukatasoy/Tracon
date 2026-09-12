using Microsoft.CodeAnalysis;

namespace Tracon.Generators.UnitTests;

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
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("set_count", "Sets a count.")]
                public static void SetCount([Description("The count.")] [Range(1, 100)] int count) { }
            }
            """;

        const string Second = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("set_count", "Sets a count.")]
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
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("set_count", "Sets a count.")]
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
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("set_count", "Sets a count.")]
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

    /// <summary>
    /// 135.3: an object member's list is held in <c>EquatableArray&lt;ObjectMember&gt;</c>,
    /// not a bare <c>ImmutableArray</c> - a value-equal object graph must still hit the
    /// pipeline's cache, exactly like <see cref="ParameterConstraints"/> above. Roslyn
    /// re-runs the analysis on every incremental run regardless (a syntax provider always
    /// re-executes its transform); what determines whether the DOWNSTREAM step is cached is
    /// whether the two resulting <c>ToolCandidate</c> values are EQUAL - the assertion this
    /// test makes.
    /// </summary>
    [Fact]
    public void An_unchanged_object_parameter_produces_an_equal_tool_candidate_across_two_runs()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [TraconTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var (first, second) = GeneratorTestHelper.RunIncremental(Source, Source);

        first.SingleWrapperFile().ShouldBe(second.SingleWrapperFile());

        var reasons = second.StepReasons(ToolRegistrationGenerator.TrackingNames.ToolCandidates);
        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }

    /// <summary>Changing a NESTED object member's constraint must still reach the second run's schema - the cache key covers the whole graph, not just the root parameter.</summary>
    [Fact]
    public void Changing_only_a_nested_object_members_constraint_produces_a_fresh_schema()
    {
        const string First = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] [Range(1, 5)] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [TraconTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        const string Second = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace MyApp;

            public sealed record Rubric([Description("The name.")] string Name, [Description("The weight.")] [Range(1, 10)] int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [TraconTool("score", "Scores a rubric.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Score([Description("The rubric.")] Rubric rubric) { }
            }
            """;

        var (first, second) = GeneratorTestHelper.RunIncremental(First, Second);

        first.SingleWrapperFile().ShouldContain("\\\"maximum\\\":5");
        second.SingleWrapperFile().ShouldContain("\\\"maximum\\\":10");
    }
}
