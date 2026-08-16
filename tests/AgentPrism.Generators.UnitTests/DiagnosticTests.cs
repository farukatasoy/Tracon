using System.Globalization;

namespace AgentPrism.Generators.UnitTests;

/// <summary>Verifies each of the APG0001-APG0007 diagnostics individually.</summary>
public sealed class DiagnosticTests
{
    [Fact]
    public void APG0001_is_reported_when_the_same_tool_name_is_used_on_two_methods()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class ToolsA
            {
                [AgentPrismTool("conflict", "First.")]
                public static string First() => "a";
            }

            internal static class ToolsB
            {
                [AgentPrismTool("conflict", "Second.")]
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
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("invalid name!", "Description.")]
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
            using AgentPrism;

            namespace MyApp;

            internal sealed class ComplexType { public string? Name { get; set; } }

            internal static class Tools
            {
                [AgentPrismTool("create", "Creates.")]
                public static void Create(ComplexType data) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0003");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("ComplexType");
    }

    [Fact]
    public void APG0004_a_generic_method_is_rejected()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("convert", "Converts.")]
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
            using AgentPrism;
            using Microsoft.Extensions.DependencyInjection;

            namespace MyApp;

            internal static class Program
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddAgentPrism().AddGeneratedTools();
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
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("nameless_description")]
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
    public void APG0007_an_instance_method_is_rejected_and_the_message_points_to_K218()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal sealed class Tools
            {
                [AgentPrismTool("get", "Gets.")]
                public string Get() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0007");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("K-218");
    }

    [Fact]
    public void An_unprocessable_class_is_never_skipped_silently_it_always_produces_a_diagnostic()
    {
        // 52.5: silent skipping is forbidden - the tests verifying APG0003/4/7 already
        // prove this; here we confirm in one place that at least one blocking diagnostic
        // is produced for EVERY category.
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal sealed class BrokenTool
            {
                [AgentPrismTool("bad")]
                public string Get(string id) => id;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldNotBeEmpty();
        result.GeneratedFiles().ShouldBeEmpty();
    }
}
