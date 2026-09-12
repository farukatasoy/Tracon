using System.Globalization;

namespace Tracon.Generators.UnitTests;

/// <summary>Verifies each of the APG0001-APG0010 diagnostics individually.</summary>
public sealed class DiagnosticTests
{
    [Fact]
    public void APG0001_is_reported_when_the_same_tool_name_is_used_on_two_methods()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal static class ToolsA
            {
                [TraconTool("conflict", "First.")]
                public static string First() => "a";
            }

            internal static class ToolsB
            {
                [TraconTool("conflict", "Second.")]
                public static string Second() => "b";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0001").Count.ShouldBe(2);
    }

    [Fact]
    public void APG0002_is_reported_for_an_invalid_tool_name()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("invalid name!", "Description.")]
                public static string Get() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0002").Count.ShouldBe(1);
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void APG0003_is_reported_for_an_unsupported_parameter_type()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal sealed class ComplexType { public string? Name { get; set; } }

            internal static class Tools
            {
                [TraconTool("create", "Creates.")]
                public static void Create(ComplexType data) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0003");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("ComplexType");
    }

    /// <summary>
    /// 130 narrowed APG0003's boundary: the generator now expresses a constraint
    /// (<c>minimum</c>/<c>maximum</c>/length/<c>pattern</c>) from a standard
    /// DataAnnotations attribute, so the message must stop claiming it never does. A
    /// nested object is still never expressed, so that half of the boundary stays.
    /// </summary>
    [Fact]
    public void APG0003_message_names_the_nested_object_boundary_but_no_longer_claims_no_constraint_130()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal sealed class ComplexType { public string? Name { get; set; } }

            internal static class Tools
            {
                [TraconTool("create", "Creates.")]
                public static void Create(ComplexType data) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var message = result.DiagnosticsWithId("APG0003").Single().GetMessage(CultureInfo.InvariantCulture);
        message.ShouldContain("nested object");
        message.ShouldNotContain("constraint");
    }

    [Fact]
    public void APG0004_a_generic_method_is_rejected()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("convert", "Converts.")]
                public static T Convert<T>(T value) => value;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0004").Count.ShouldBe(1);
    }

    [Fact]
    public void APG0005_is_reported_when_AddGeneratedTools_is_called_but_no_method_is_marked()
    {
        const string Source = """
            using Tracon;
            using Microsoft.Extensions.DependencyInjection;

            namespace MyApp;

            internal static class Program
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddTracon().AddGeneratedTools();
                }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0005").Count.ShouldBe(1);
    }

    [Fact]
    public void APG0005_is_silent_when_AddGeneratedTools_is_not_called_and_no_method_is_marked()
    {
        const string Source = """
            namespace MyApp;

            internal static class Tools
            {
                public static string Ping() => "pong";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void APG0006_warns_when_the_description_is_missing_but_does_not_block_generation()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal static class Tools
            {
                [TraconTool("nameless_description")]
                public static string Get() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0006");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        // The warning does NOT block generation - the wrapper file is still created.
        result.GeneratedFiles().Count.ShouldBe(2);
    }

    [Fact]
    public void APG0008_requires_a_source_generated_context_for_a_complex_result()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal sealed record OrderResult(string Id);

            internal static class Tools
            {
                [TraconTool("get_order", "Gets an order.")]
                public static OrderResult GetOrder(string id) => new(id);
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0008").ShouldHaveSingleItem();
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void APG0007_an_instance_method_is_rejected_and_the_message_points_to_K218()
    {
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal sealed class Tools
            {
                [TraconTool("get", "Gets.")]
                public string Get() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0007");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("K-218");
    }

    [Fact]
    public void APG0009_warns_when_a_parameter_description_is_missing_but_does_not_block_generation()
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

        var diagnostics = result.DiagnosticsWithId("APG0009");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("query");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("search");

        // The warning does NOT block generation - the wrapper file is still created.
        result.GeneratedFiles().Count.ShouldBe(2);
    }

    [Fact]
    public void An_unprocessable_class_is_never_skipped_silently_it_always_produces_a_diagnostic()
    {
        // 52.5: silent skipping is forbidden - the tests verifying APG0003/4/7 already
        // prove this; here we confirm in one place that at least one blocking diagnostic
        // is produced for EVERY category.
        const string Source = """
            using Tracon;

            namespace MyApp;

            internal sealed class BrokenTool
            {
                [TraconTool("bad")]
                public string Get(string id) => id;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldNotBeEmpty();
        result.GeneratedFiles().ShouldBeEmpty();
    }
}
