using System.Globalization;

namespace Tracon.Generators.UnitTests;

/// <summary>
/// Verifies 135.1/135.4: an object parameter's graph is limited to 3 nested levels, a cycle
/// is caught instead of recursing forever (TRC0012), and every object type reached must be
/// declared on the tool's <c>JsonSerializerContext</c> (TRC0011) - the same rule TRC0008
/// already enforces for a complex RESULT type, extended to cover a nested PARAMETER type too.
/// </summary>
public sealed class ToolObjectGraphTests
{
    /// <summary>
    /// A graph 4 nested levels deep (root's member, its member, its member, its member -
    /// manual case 5's "four-level graph"): the root and its first 3 nested levels are
    /// within the limit, and only the 4th nested level is rejected.
    /// </summary>
    [Fact]
    public void A_graph_four_levels_deep_produces_TRC0012_and_blocks_generation()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            public sealed record Level0(Level1 Next);
            public sealed record Level1(Level2 Next);
            public sealed record Level2(Level3 Next);
            public sealed record Level3(Level4 Next);
            public sealed record Level4(int Value);

            internal static class Tools
            {
                [TraconTool("walk", "Walks a deep graph.")]
                public static void Walk([Description("The root.")] Level0 root) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("TRC0012");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("Level4");

        // No AddGeneratedTools() call site and no emittable tool - nothing is generated at all.
        result.GeneratedFiles().ShouldBeEmpty();
    }

    /// <summary>A graph exactly 3 nested levels deep (root's member, its member, its member) is within the limit.</summary>
    [Fact]
    public void A_graph_three_levels_deep_is_within_the_limit()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace MyApp;

            public sealed record Level0(Level1 Next);
            public sealed record Level1(Level2 Next);
            public sealed record Level2(Level3 Next);
            public sealed record Level3(int Value);

            [JsonSerializable(typeof(Level0))]
            [JsonSerializable(typeof(Level1))]
            [JsonSerializable(typeof(Level2))]
            [JsonSerializable(typeof(Level3))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [TraconTool("walk", "Walks a graph.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Walk([Description("The root.")] Level0 root) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();
        result.SingleWrapperFile().ShouldNotBeEmpty();
    }

    /// <summary>135.1: a type that reaches itself again through its own members is rejected, never walked forever.</summary>
    [Fact]
    public async Task A_cycle_produces_TRC0012_naming_the_cyclic_path_and_the_generator_returns_promptly()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            public sealed record NodeA(NodeB Next);
            public sealed record NodeB(NodeA Next);

            internal static class Tools
            {
                [TraconTool("walk", "Walks a cyclic graph.")]
                public static void Walk([Description("The root.")] NodeA root) { }
            }
            """;

        var result = await Task.Run(() => GeneratorTestHelper.Run(Source)).WaitAsync(TimeSpan.FromSeconds(10));

        var diagnostics = result.DiagnosticsWithId("TRC0012");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("NodeA → NodeB → NodeA");
    }

    /// <summary>
    /// TRC0011 (135.4): every distinct object type in the graph is reported once, in the
    /// same compilation, when the tool declares no context at all (Open Question 3 - report
    /// every missing type, not just the first).
    /// </summary>
    [Fact]
    public void An_object_parameter_with_no_JsonSerializerContext_reports_TRC0011_for_every_type_in_its_graph()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            public sealed record Inner(string X);
            public sealed record Outer(Inner Nested);

            internal static class Tools
            {
                [TraconTool("process", "Processes an object.")]
                public static void Process([Description("The value.")] Outer value) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("TRC0011");
        diagnostics.Count.ShouldBe(2);

        var messages = diagnostics.Select(d => d.GetMessage(CultureInfo.InvariantCulture)).ToList();
        messages.ShouldContain(m => m.Contains("MyApp.Outer", StringComparison.Ordinal));
        messages.ShouldContain(m => m.Contains("MyApp.Inner", StringComparison.Ordinal));

        result.GeneratedFiles().ShouldBeEmpty();
    }

    /// <summary>When the context declares the outer type but forgets a nested one, only the nested type is reported.</summary>
    [Fact]
    public void A_JsonSerializerContext_missing_only_a_nested_type_reports_TRC0011_for_that_type_alone()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace MyApp;

            public sealed record Inner(string X);
            public sealed record Outer(Inner Nested);

            [JsonSerializable(typeof(Outer))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [TraconTool("process", "Processes an object.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Process([Description("The value.")] Outer value) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("TRC0011");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("MyApp.Inner");
    }

    /// <summary>When every type in the graph is declared, TRC0011 never fires.</summary>
    [Fact]
    public void A_JsonSerializerContext_declaring_every_type_in_the_graph_produces_no_TRC0011()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace MyApp;

            public sealed record Inner(string X);
            public sealed record Outer(Inner Nested);

            [JsonSerializable(typeof(Outer))]
            [JsonSerializable(typeof(Inner))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [TraconTool("process", "Processes an object.", JsonSerializerContext = typeof(ToolJsonContext))]
                public static void Process([Description("The value.")] Outer value) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("TRC0011").ShouldBeEmpty();
        result.SingleWrapperFile().ShouldNotBeEmpty();
    }

    /// <summary>135.1: a type with more than one public constructor has no single, unambiguous member list - unsupported (TRC0003), not silently picked.</summary>
    [Fact]
    public void A_type_with_more_than_one_public_constructor_is_reported_as_an_unsupported_parameter_type()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            public sealed class Ambiguous
            {
                public Ambiguous(string name) => Name = name;
                public Ambiguous(string name, int weight) { Name = name; Weight = weight; }

                public string Name { get; }
                public int Weight { get; }
            }

            internal static class Tools
            {
                [TraconTool("process", "Processes a value.")]
                public static void Process([Description("The value.")] Ambiguous value) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("TRC0003").Count.ShouldBe(1);
        result.DiagnosticsWithId("TRC0011").ShouldBeEmpty();
        result.DiagnosticsWithId("TRC0012").ShouldBeEmpty();
    }

    /// <summary>135.1: a generic type is never a supported object shape - unsupported (TRC0003).</summary>
    [Fact]
    public void A_generic_record_parameter_is_reported_as_an_unsupported_parameter_type()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            public sealed record Wrapper<T>(T Value);

            internal static class Tools
            {
                [TraconTool("process", "Processes a value.")]
                public static void Process([Description("The value.")] Wrapper<string> value) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("TRC0003").Count.ShouldBe(1);
    }

    /// <summary>135.4: TRC0003's message no longer claims a nested object can never be expressed.</summary>
    [Fact]
    public void TRC0003_no_longer_claims_a_nested_object_is_never_expressed()
    {
        const string Source = """
            using System.ComponentModel;
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("process", "Processes a value.")]
                public static void Process([Description("The value.")] System.Collections.Generic.Dictionary<string, string> value) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("TRC0003");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldNotContain("never expresses a nested object");
    }
}
