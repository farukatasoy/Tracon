using System.Globalization;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Verifies APG0010 (130.2): a constraint attribute that does not apply to its
/// parameter's type or shape is reported as a warning, never blocks generation, and
/// never reaches the schema.
/// </summary>
public sealed class ToolDiagnosticTests
{
    [Fact]
    public void A_Range_attribute_on_a_string_parameter_is_reported_and_omitted_from_the_schema()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_code", "Sets a code.")]
                public static void SetCode([Description("The code.")] [Range(1, 10)] string s) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0010");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("Range");

        // A warning does not block generation.
        result.GeneratedFiles().ShouldNotBeEmpty();

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("minimum");
        wrapper.ShouldNotContain("maximum");
    }

    [Fact]
    public void A_RegularExpression_attribute_on_an_integer_parameter_is_reported()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_count", "Sets a count.")]
                public static void SetCount([Description("The count.")] [RegularExpression("^[0-9]+$")] int count) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0010");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("RegularExpression");
    }

    [Fact]
    public void A_length_constraint_on_a_boolean_parameter_is_reported()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_flag", "Sets a flag.")]
                public static void SetFlag([Description("The flag.")] [MinLength(1)] bool flag) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").Count.ShouldBe(1);
    }

    /// <summary>
    /// <c>RangeAttribute</c>'s <c>Range(Type, string, string)</c> overload does not give
    /// the generator a compile-time numeric constant (130.2) - it is silently unsupported,
    /// reported instead of producing a wrong or crashing schema.
    /// </summary>
    [Fact]
    public void A_Type_based_Range_attribute_is_reported_and_not_rendered()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_ratio", "Sets a ratio.")]
                public static void SetRatio(
                    [Description("The ratio.")] [Range(typeof(decimal), "0", "1")] decimal ratio) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0010");
        diagnostics.Count.ShouldBe(1);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("minimum");
        wrapper.ShouldNotContain("maximum");
    }

    [Fact]
    public void A_StringLength_attribute_on_an_array_parameter_is_reported()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("tag", "Tags.")]
                public static void Tag([Description("The tags.")] [StringLength(10)] string[] tags) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").Count.ShouldBe(1);
    }

    /// <summary>
    /// JSON Schema requires <c>minLength</c>/<c>maxLength</c>/<c>minItems</c>/<c>maxItems</c>
    /// to be a non-negative integer (draft 2020-12, "nonNegativeInteger"). A negative
    /// constant is renderable C# but not a renderable schema value, so it is reported
    /// (never written as an invalid <c>-1</c>) - measured by an independent audit.
    /// </summary>
    [Fact]
    public void A_negative_MinLength_is_reported_and_omitted_from_the_schema()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [MinLength(-1)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").Count.ShouldBe(1);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("minLength");
    }

    [Fact]
    public void A_negative_MaxLength_on_an_array_parameter_is_reported_and_omitted_from_the_schema()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("tag", "Tags.")]
                public static void Tag([Description("The tags.")] [MaxLength(-1)] string[] tags) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").Count.ShouldBe(1);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("maxItems");
    }

    [Fact]
    public void A_negative_StringLength_MinimumLength_is_reported_and_neither_bound_reaches_the_schema()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [StringLength(10, MinimumLength = -1)] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").Count.ShouldBe(1);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("minLength");
        wrapper.ShouldNotContain("maxLength");
    }

    /// <summary>
    /// <c>MaxLengthAttribute()</c>'s parameterless overload (valid C#, meaning "use the
    /// store's own maximum") carries no length value the generator can render - it is
    /// reported the same as an incompatible type/shape rather than silently producing no
    /// schema effect, for consistency with every other unrenderable case.
    /// </summary>
    [Fact]
    public void A_parameterless_MaxLength_attribute_is_reported()
    {
        const string Source = """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("set_name", "Sets a name.")]
                public static void SetName([Description("The name.")] [MaxLength] string name) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").Count.ShouldBe(1);
    }

    [Fact]
    public void A_supported_constraint_produces_no_APG0010()
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
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0010").ShouldBeEmpty();
    }
}
