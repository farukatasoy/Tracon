namespace AgentPrism.Generators.UnitTests;

/// <summary>Verifies the source generated for successfully classified tools.</summary>
public sealed class GeneratedOutputTests
{
    [Fact]
    public void A_registration_is_generated_for_a_marked_static_method()
    {
        const string Source = """
            using System.ComponentModel;
            using AgentPrism;

            namespace MyApp;

            internal static class OrderTools
            {
                [AgentPrismTool("get_order_status", "Returns the status of an order.")]
                public static string GetOrderStatus([Description("The order number.")] string orderId) => orderId;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var aggregate = result.GeneratedFiles()["AgentPrismGeneratedTools.g.cs"];
        aggregate.ShouldContain("AddGeneratedTools");
        aggregate.ShouldNotContain("source:");

        var wrapperFile = result.SingleWrapperFile();
        wrapperFile.ShouldContain("public override string Name => \"get_order_status\";");
        wrapperFile.ShouldContain("Returns the status of an order.");
        wrapperFile.ShouldContain("MyApp.OrderTools.GetOrderStatus(");
    }

    [Fact]
    public void The_method_name_is_used_when_the_attribute_is_unnamed()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool]
                public static string Ping() => "pong";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("public override string Name => \"Ping\";");
    }

    [Fact]
    public void The_approval_requirement_carries_into_the_generated_registration()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("delete", "Deletes permanently.", RequiresApproval = true)]
                public static void Delete(string id) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var aggregate = result.GeneratedFiles()["AgentPrismGeneratedTools.g.cs"];
        aggregate.ShouldContain("requiresApproval: true");
    }

    [Fact]
    public void Effect_permission_and_timeout_carry_into_the_generated_registration()
    {
        const string Source = """
            using System.ComponentModel;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool(
                    "cancel_order",
                    "Cancels an order.",
                    RequiresApproval = true,
                    Effect = ToolEffect.Destructive,
                    RequiredPermission = "orders.cancel",
                    TimeoutSeconds = 5,
                    SafeToRepeat = true,
                    MaxOutputBytes = 768)]
                public static void CancelOrder([Description("The order number.")] string orderId) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var aggregate = result.GeneratedFiles()["AgentPrismGeneratedTools.g.cs"];

        aggregate.ShouldContain("effect: (global::AgentPrism.ToolEffect)2");
        aggregate.ShouldContain("requiredPermission: \"orders.cancel\"");
        aggregate.ShouldContain("timeout: global::System.TimeSpan.FromSeconds(5)");
        aggregate.ShouldContain("safeToRepeat: true");
        aggregate.ShouldContain("maxOutputBytes: 768");
    }

    [Fact]
    public void A_complex_result_uses_the_tool_owned_source_generated_context()
    {
        const string Source = """
            using AgentPrism;
            using System.ComponentModel;
            using System.Text.Json.Serialization;

            namespace MyApp;

            internal sealed record OrderResult(string Id);

            [JsonSerializable(typeof(OrderResult))]
            internal partial class ToolJsonContext : JsonSerializerContext;

            internal static class Tools
            {
                [AgentPrismTool(
                    "get_order",
                    "Gets an order.",
                    JsonSerializerContext = typeof(ToolJsonContext))]
                public static OrderResult GetOrder([Description("The order number.")] string id) => new(id);
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("ToolJsonContext.Default.GetTypeInfo(typeof(global::MyApp.OrderResult))");
        wrapper.ShouldNotContain("JsonSerializable(typeof(");
    }

    [Fact]
    public void Undeclared_effect_permission_and_timeout_generate_defaults()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("get_order_status", "Returns the order status.")]
                public static string GetOrderStatus(string orderId) => orderId;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var aggregate = result.GeneratedFiles()["AgentPrismGeneratedTools.g.cs"];

        aggregate.ShouldContain("effect: (global::AgentPrism.ToolEffect)0");
        aggregate.ShouldContain("requiredPermission: null");
        aggregate.ShouldContain("timeout: null");
    }

    [Fact]
    public void The_JSON_schema_contains_parameters_and_required()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("search", "Searches.")]
                public static string Search(string query, int count = 10) => query;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("\\\"query\\\":{\\\"type\\\":\\\"string\\\"}");
        wrapper.ShouldContain("\\\"count\\\":{\\\"type\\\":\\\"integer\\\"}");
        wrapper.ShouldContain("\\\"required\\\":[\\\"query\\\"]");
        wrapper.ShouldContain("GetOptional(arguments, \"count\", static e => e.GetInt32(), 10)");
    }

    [Fact]
    public void An_enum_parameter_generates_a_string_schema_and_Enum_Parse()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal enum Status { Open, Closed }

            internal static class Tools
            {
                [AgentPrismTool("set_status", "Sets the status.")]
                public static void SetStatus(Status status) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("\\\"enum\\\":[\\\"Open\\\",\\\"Closed\\\"]");
        wrapper.ShouldContain("global::System.Enum.Parse<global::MyApp.Status>(e.GetString()!, ignoreCase: true)");
    }

    [Fact]
    public void An_array_parameter_generates_an_array_schema_and_GetArray()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("tag", "Tags.")]
                public static void Tag(string[] tags) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("\\\"type\\\":\\\"array\\\"");
        wrapper.ShouldContain("GetArray(arguments, \"tags\", static e => e.GetString()!, required: true, defaultValue: null)");
    }

    [Fact]
    public void A_bare_array_parameter_is_converted_with_ToArray_and_the_generated_code_compiles()
    {
        // GetArray returns IReadOnlyList<T>; there is no implicit conversion to a T[] parameter.
        // This test verifies not just the text but also that the OUTPUT COMPILATION IS ERROR-FREE -
        // it catches the CS1503 regression found in MT-PKG-044. Other tests only check the
        // generated text and never run the compilation at all.
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("tag", "Tags.")]
                public static void Tag(string[] tags) { }

                [AgentPrismTool("sum_numbers", "Sums numbers.")]
                public static int SumNumbers(int[] numbers) { var sum = 0; foreach (var n in numbers) { sum += n; } return sum; }

                [AgentPrismTool("list_also_works", "IReadOnlyList<T> should still use GetArray directly.")]
                public static int ListAlsoWorks(System.Collections.Generic.IReadOnlyList<int> numbers) => numbers.Count;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile(hintNamePrefix: "Tag_");
        wrapper.ShouldContain("global::System.Linq.Enumerable.ToArray(global::AgentPrism.AgentPrismGeneratedToolArguments.GetArray(");

        var listWrapper = result.SingleWrapperFile(hintNamePrefix: "ListAlsoWorks_");
        listWrapper.ShouldNotContain("ToArray");

        var errors = result.OutputCompilation.GetDiagnostics()
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToList();
        errors.ShouldBeEmpty(customMessage: string.Join('\n', errors.Select(e => e.ToString())));
    }

    [Fact]
    public void A_CancellationToken_parameter_is_excluded_from_the_schema_and_bound_directly()
    {
        const string Source = """
            using System.Threading;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("wait", "Waits.")]
                public static void Wait(string id, CancellationToken cancellationToken) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("cancellationToken\\\"");
        wrapper.ShouldContain("MyApp.Tools.Wait(@id, cancellationToken)");
    }

    [Fact]
    public void An_async_Task_returning_method_is_generated_with_await()
    {
        const string Source = """
            using System.ComponentModel;
            using System.Threading.Tasks;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("get", "Gets.")]
                public static async Task<string> GetAsync([Description("The order number.")] string id)
                {
                    await Task.Yield();
                    return id;
                }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("async global::System.Threading.Tasks.ValueTask<object?> InvokeCoreAsync");
        wrapper.ShouldContain("await global::MyApp.Tools.GetAsync(@id).ConfigureAwait(false);");
    }

    [Fact]
    public void Both_tools_are_generated_when_they_live_in_two_different_classes()
    {
        const string Source = """
            using System.ComponentModel;
            using AgentPrism;

            namespace MyApp;

            internal static class OrderTools
            {
                [AgentPrismTool("get_order", "Returns an order.")]
                public static string GetOrder([Description("The order number.")] string id) => id;
            }

            internal static class UserTools
            {
                [AgentPrismTool("get_user", "Returns a user.")]
                public static string GetUser([Description("The user number.")] string id) => id;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var files = result.GeneratedFiles();
        files.Count.ShouldBe(3); // 2 wrapper + 1 aggregator
        files.Values.Count(text => text.Contains("\"get_order\"", StringComparison.Ordinal)).ShouldBe(1);
        files.Values.Count(text => text.Contains("\"get_user\"", StringComparison.Ordinal)).ShouldBe(1);
    }
}
