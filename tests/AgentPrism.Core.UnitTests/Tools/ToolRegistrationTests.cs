using System.Text.Json;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Tools;

/// <summary>
/// Validates the descriptors the tool registry exposes to the interface, and
/// the <c>AddToolsFrom&lt;T&gt;()</c> scan.
/// </summary>
/// <remarks>
/// Tools are defined only in code; the interface only picks from this list.
/// This is a security boundary. Rationale: <c>docs/MIMARI.md</c>, rule K2.
/// </remarks>
public sealed class ToolRegistrationTests
{
    [Fact]
    public void Tool_descriptor_carries_name_description_and_json_schema()
    {
        var registry = TestData.Registry(TestData.Tool("get_order", "Returns the order status."));

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe("get_order");
        descriptor.Description.ShouldBe("Returns the order status.");
        descriptor.JsonSchema.ShouldNotBeNullOrWhiteSpace();
        JsonDocument.Parse(descriptor.JsonSchema!).RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Fact]
    public void Json_schema_contains_the_parameters()
    {
        var registry = TestData.Registry(
            Microsoft.Extensions.AI.AIFunctionFactory.Create(
                (string orderId) => $"status: {orderId}",
                "get_order_status",
                "Returns the shipment status."));

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.JsonSchema!.ShouldContain("orderId");
    }

    [Fact]
    public void Approval_requirement_is_carried_to_the_descriptor()
    {
        var registry = new ToolRegistry([new AgentPrismToolRegistration(TestData.Tool("delete"), requiresApproval: true)]);

        registry.List().ShouldHaveSingleItem().RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Registering_the_same_name_twice_throws()
    {
        var exception = Should.Throw<AgentPrismException>(
            () => TestData.Registry(TestData.Tool("duplicate"), TestData.Tool("duplicate")));

        exception.Message.ShouldContain("duplicate");
    }

    [Fact]
    public void Server_side_tool_descriptor_does_not_run_on_client()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"));

        registry.List().ShouldHaveSingleItem().RunsOnClient.ShouldBeFalse();
    }

    [Fact]
    public void AddClientTool_registers_a_declaration_that_runs_on_client()
    {
        var schema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type":"object","properties":{}}""");

        var registry = BuildRegistry(
            builder => builder.AddClientTool("read_page_title", "Reads the current page title.", schema));

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.Name.ShouldBe("read_page_title");
        descriptor.Description.ShouldBe("Reads the current page title.");
        descriptor.RunsOnClient.ShouldBeTrue();
        descriptor.JsonSchema.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AddClientTool_produces_a_tool_that_is_not_invocable()
    {
        var schema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type":"object","properties":{}}""");

        var registry = BuildRegistry(
            builder => builder.AddClientTool("read_page_title", "Reads the current page title.", schema));

        registry.TryGet("read_page_title", out var tool).ShouldBeTrue();

        // ShouldNotBeOfType checks EXACT type equality; AIFunction is abstract,
        // so no instance could ever fail that check regardless of correctness.
        // ShouldNotBeAssignableTo is the assertion that actually distinguishes
        // an invocable AIFunction from a declaration-only AIFunctionDeclaration.
        tool.ShouldNotBeAssignableTo<Microsoft.Extensions.AI.AIFunction>();
    }

    [Fact]
    public void Client_tool_cannot_require_approval()
    {
        var schema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type":"object","properties":{}}""");

        var declaration = Microsoft.Extensions.AI.AIFunctionFactory.CreateDeclaration(
            "read_page_title", "Reads the current page title.", schema, returnJsonSchema: null);

        var exception = Should.Throw<AgentPrismException>(
            () => new ToolRegistry([new AgentPrismToolRegistration(declaration, requiresApproval: true)]));

        exception.Message.ShouldContain("read_page_title");
        exception.Message.ShouldContain("client");
    }

    [Fact]
    public void AddToolsFrom_registers_only_marked_methods()
    {
        var registry = BuildRegistry(builder => builder.AddToolsFrom(typeof(SampleToolClass)));

        registry.List().Select(static descriptor => descriptor.Name)
            .ShouldBe(["DefaultNamedMethod", "custom_name"], ignoreOrder: true);
    }

    [Fact]
    public void AddToolsFrom_carries_the_description_and_approval_flag()
    {
        var registry = BuildRegistry(builder => builder.AddToolsFrom(typeof(SampleToolClass)));

        var descriptor = registry.List().Single(static d => string.Equals(d.Name, "custom_name", StringComparison.Ordinal));

        descriptor.Description.ShouldBe("Custom-named tool.");
        descriptor.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void AddToolsFrom_uses_the_method_name_when_no_name_is_given()
    {
        var registry = BuildRegistry(builder => builder.AddToolsFrom(typeof(SampleToolClass)));

        registry.TryGet("DefaultNamedMethod", out _).ShouldBeTrue();
    }

    [Fact]
    public void AddToolsFrom_throws_when_there_is_no_marked_method()
    {
        // A silent, empty registration would make the caller think tools were registered.
        var exception = Should.Throw<AgentPrismException>(
            () => BuildRegistry(builder => builder.AddToolsFrom(typeof(UnmarkedClass))));

        exception.Message.ShouldContain(nameof(UnmarkedClass));
        exception.Message.ShouldContain("AgentPrismTool");
    }

    [Fact]
    public void AddToolsFrom_rejects_the_sample_method_at_scan_time()
    {
        // K-218 fix: MAF passes an EMPTY provider as AIFunctionArguments.Services
        // into the tool body (EmptyServiceProvider, NOT null). This check used to
        // sit behind an `is { }` pattern at call time that never triggered.
        // Fix: move the rejection to SCAN time instead of waiting for the first
        // tool call.
        var services = new ServiceCollection();
        services.AddSingleton(new GreetingSettings("Hello"));

        var exception = Should.Throw<AgentPrismException>(
            () => services.AddAgentPrism().AddToolsFrom<SampleClassWithMethod>());

        exception.Message.ShouldContain(nameof(SampleClassWithMethod));
        exception.Message.ShouldContain("Greet");
        exception.Message.ShouldContain("K-218");
    }

    private static IToolRegistry BuildRegistry(Action<IAgentPrismBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddAgentPrism());

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IToolRegistry>();
    }

    private sealed record GreetingSettings(string Prefix);

    private static class SampleToolClass
    {
        [AgentPrismTool]
        public static string DefaultNamedMethod() => "result";

        [AgentPrismTool("custom_name", "Custom-named tool.", RequiresApproval = true)]
        public static string CustomNamedMethod() => "result";

        public static string UnmarkedMethod() => "not registered";
    }

    private static class UnmarkedClass
    {
        public static string Nothing() => "not registered";
    }

    private sealed class SampleClassWithMethod(GreetingSettings settings)
    {
        [AgentPrismTool("greet")]
        public string Greet(string name) => $"{settings.Prefix} {name}";
    }
}
