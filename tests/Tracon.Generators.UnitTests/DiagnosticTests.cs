using System.Globalization;

namespace Tracon.Generators.UnitTests;

/// <summary>Verifies each of the TRC0001-TRC0010 diagnostics individually.</summary>
public sealed class DiagnosticTests
{
    [Fact]
    public void TRC0001_is_reported_when_the_same_tool_name_is_used_on_two_methods()
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

        result.DiagnosticsWithId("TRC0001").Count.ShouldBe(2);
    }

    [Fact]
    public void TRC0002_is_reported_for_an_invalid_tool_name()
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

        result.DiagnosticsWithId("TRC0002").Count.ShouldBe(1);
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void TRC0003_is_reported_for_an_unsupported_parameter_type()
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

        var diagnostics = result.DiagnosticsWithId("TRC0003");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("ComplexType");
    }

    /// <summary>
    /// 130 narrowed TRC0003's boundary: the generator now expresses a constraint
    /// (<c>minimum</c>/<c>maximum</c>/length/<c>pattern</c>) from a standard
    /// DataAnnotations attribute, so the message must stop claiming it never does. A
    /// nested object is still never expressed, so that half of the boundary stays.
    /// </summary>
    [Fact]
    public void TRC0003_message_names_the_nested_object_boundary_but_no_longer_claims_no_constraint_130()
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

        var message = result.DiagnosticsWithId("TRC0003").Single().GetMessage(CultureInfo.InvariantCulture);
        message.ShouldContain("nested object");
        message.ShouldNotContain("constraint");
    }

    [Fact]
    public void TRC0004_a_generic_method_is_rejected()
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

        result.DiagnosticsWithId("TRC0004").Count.ShouldBe(1);
    }

    [Fact]
    public void TRC0005_is_reported_when_AddGeneratedTools_is_called_but_no_method_is_marked()
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

        result.DiagnosticsWithId("TRC0005").Count.ShouldBe(1);
    }

    [Fact]
    public void TRC0005_is_silent_when_AddGeneratedTools_is_not_called_and_no_method_is_marked()
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
    public void TRC0006_warns_when_the_description_is_missing_but_does_not_block_generation()
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

        var diagnostics = result.DiagnosticsWithId("TRC0006");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        // The warning does NOT block generation - the wrapper file is still created.
        result.GeneratedFiles().Count.ShouldBe(2);
    }

    [Fact]
    public void TRC0008_requires_a_source_generated_context_for_a_complex_result()
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

        result.DiagnosticsWithId("TRC0008").ShouldHaveSingleItem();
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void TRC0007_an_instance_method_is_rejected_and_the_message_points_to_K218()
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

        var diagnostics = result.DiagnosticsWithId("TRC0007");
        diagnostics.Count.ShouldBe(1);
        var message = diagnostics[0].GetMessage(CultureInfo.InvariantCulture);
        message.ShouldContain("K-218");

        // The runtime scanner rejects the same mistake with the same three
        // escapes (ToolRegistrationTests.AddToolsFrom_rejects_the_sample_method_at_scan_time).
        // This message is the one a developer actually reads: the analyzer fails
        // the BUILD, so the runtime path never runs. It offered only two of the
        // three, and the missing one is the answer for the case that brings a
        // developer here — a tool with a dependency that must be resolved per call.
        message.ShouldContain("static");
        message.ShouldContain("AddTool(");
        message.ShouldContain("AddScopedTool(");
    }

    [Fact]
    public void TRC0009_warns_when_a_parameter_description_is_missing_but_does_not_block_generation()
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
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("query");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("search");

        // The warning does NOT block generation - the wrapper file is still created.
        result.GeneratedFiles().Count.ShouldBe(2);
    }

    [Fact]
    public void An_unprocessable_class_is_never_skipped_silently_it_always_produces_a_diagnostic()
    {
        // 52.5: silent skipping is forbidden - the tests verifying TRC0003/4/7 already
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
