using Microsoft.CodeAnalysis;

namespace Tracon.Generators.UnitTests;

/// <summary>
/// Verifies the <c>IIncrementalGenerator</c> contract: an unrelated change
/// must NOT invalidate a step's cache (52.5, DoD "IncrementalityTests").
/// </summary>
public sealed class IncrementalityTests
{
    [Fact]
    public void Tool_candidate_step_does_not_rerun_on_an_unrelated_change()
    {
        const string First = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("get_order_status", "Returns the status of an order.")]
                public static string GetOrderStatus([Description("The order number.")] string orderId) => orderId;
            }

            internal static class Unrelated
            {
                public const int Value = 1;
            }
            """;

        // Only the value of the UNRELATED constant changed; the method marked
        // with [TraconTool] must produce an equivalent generator input (not
        // the same SyntaxNode location/content, but equivalent for the
        // generator's purposes, since ForAttributeWithMetadataName only tracks
        // the marked node).
        const string Second = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("get_order_status", "Returns the status of an order.")]
                public static string GetOrderStatus([Description("The order number.")] string orderId) => orderId;
            }

            internal static class Unrelated
            {
                public const int Value = 2;
            }
            """;

        var (first, second) = GeneratorTestHelper.RunIncremental(First, Second);

        first.Diagnostics.ShouldBeEmpty();
        second.Diagnostics.ShouldBeEmpty();

        var reasons = second.StepReasons(ToolRegistrationGenerator.TrackingNames.ToolCandidates);

        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void Tool_candidate_step_stays_cached_when_only_the_marked_methods_body_changes()
    {
        const string First = """
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("get_order_status", "Returns the status of an order.")]
                public static string GetOrderStatus(string orderId) => "v1";
            }
            """;

        const string Second = """
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("get_order_status", "Returns the status of an order.")]
                public static string GetOrderStatus(string orderId) => "v2";
            }
            """;

        var (_, second) = GeneratorTestHelper.RunIncremental(First, Second);

        // Since the model (signature, name, description) stays the SAME, the
        // ToolCandidate is equivalent - a body change does NOT affect
        // schema/registration generation, only the call-site text. The cache
        // therefore must stay Cached; this verifies the model carries only the
        // fields it NEEDS (not the SyntaxNode/body).
        var reasons = second.StepReasons(ToolRegistrationGenerator.TrackingNames.ToolCandidates);

        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }
}
