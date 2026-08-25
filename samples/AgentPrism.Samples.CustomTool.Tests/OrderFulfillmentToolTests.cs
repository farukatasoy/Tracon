using System.Reflection;
using AgentPrism.Samples.CustomTool;
using AgentPrism.Testing;
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Samples.CustomTool.Tests;

/// <summary>Runs the published custom-tool contracts against the sample.</summary>
public sealed class OrderFulfillmentToolContractTests : RepeatableToolContract
{
    protected override string ExpectedResultText => "ready";

    protected override ValueTask<AgentPrismToolRegistration> CreateRegistrationAsync()
        => new(new AgentPrismToolRegistration(
            AIFunctionFactory.Create((Func<string>)(() => "ready"), "submit_order", "Submits an order to fulfillment."),
            requiresApproval: true,
            effect: ToolEffect.External,
            requiredPermission: "orders.submit",
            timeout: TimeSpan.FromSeconds(30),
            safeToRepeat: true,
            maxOutputBytes: 768));
}

/// <summary>Exercises both custom-tool paths through a real AgentPrism run.</summary>
public sealed class OrderFulfillmentToolRunTests
{
    [Fact]
    public async Task A_scoped_custom_tool_completes_a_real_agent_run()
    {
        var services = new ServiceCollection();
        var builder = services
            .AddAgentPrism()
            .AddModelProvider(new FakeModelProvider("custom-tool")
                .CallsTool("submit_order", new { orderId = "ORD-42" })
                .EchoesLastToolResult())
            // The registration still requires approval in production. This
            // sample test supplies an explicit code policy so it can prove the
            // invocation path without an HTTP approval round trip.
            .AddToolApprovalPolicy("submit_order", static _ => ToolApprovalPolicyDecision.NotRequired)
            .AddAgent(new AgentDefinition
            {
                Name = "fulfillment",
                Instructions = "Submit the requested order.",
                Model = new ModelBinding { Provider = "custom-tool", Model = "fake-model" },
                ToolNames = ["submit_order"],
            });

        builder.Services.AddScoped<IOrderRepository, FakeOrderRepository>();
        builder.Services.AddOrderFulfillmentTool();

        await using var provider = services.BuildServiceProvider();
        var agent = await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(
            "fulfillment", culture: null, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The fulfillment agent was not registered.");

        var response = await agent.RunAsync("Submit order ORD-42.");

        response.Text.ShouldContain("ORD-42");
        response.Text.ShouldContain("submitted");
    }

    [Fact]
    public async Task A_generated_complex_result_completes_a_real_agent_run()
    {
        var services = new ServiceCollection();
        services
            .AddAgentPrism()
            .AddModelProvider(new FakeModelProvider("generated-tool")
                .CallsTool("preview_order", new { orderId = "ORD-43" })
                .EchoesLastToolResult())
            .AddOrderPreviewTools()
            .AddAgent(new AgentDefinition
            {
                Name = "preview",
                Instructions = "Preview the requested order.",
                Model = new ModelBinding { Provider = "generated-tool", Model = "fake-model" },
                ToolNames = ["preview_order"],
            });

        await using var provider = services.BuildServiceProvider();
        var agent = await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(
            "preview", culture: null, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The preview agent was not registered.");

        var response = await agent.RunAsync("Preview order ORD-43.");

        response.Text.ShouldContain("ORD-43");
        response.Text.ShouldContain("ready");
    }

    [Fact]
    public async Task A_generated_complex_results_field_value_is_seen_by_the_content_guard()
    {
        // Proves the DoD claim behind Phase 102.2 end to end, through the REAL
        // pipeline (registration -> invocation -> masking), not a manually
        // built FunctionResultContent: a source-generator tool's complex
        // result reaches the guard as its actual serialized JSON field
        // values. Before 102.2 the guard saw only a bare CLR type name
        // ("AgentPrism.Samples.CustomTool.OrderPreview") and this card number
        // would leak straight through to the model's own final answer.
        const string CardNumber = "4539578763621486";

        var services = new ServiceCollection();
        services
            .AddAgentPrism()
            .AddModelProvider(new FakeModelProvider("guard-probe")
                .CallsTool("preview_order", new { orderId = CardNumber })
                .EchoesLastToolResult())
            .AddOrderPreviewTools()
            .AddPatternContentGuard(static options => options.MaskedPii = PiiPatterns.CreditCard)
            .AddAgent(new AgentDefinition
            {
                Name = "preview-guarded",
                Instructions = "Preview the requested order.",
                Model = new ModelBinding { Provider = "guard-probe", Model = "fake-model" },
                ToolNames = ["preview_order"],
            });

        await using var provider = services.BuildServiceProvider();
        var agent = await provider.GetRequiredService<IAgentCatalog>().ResolveAsync(
            "preview-guarded", culture: null, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The preview-guarded agent was not registered.");

        var response = await agent.RunAsync($"Preview order {CardNumber}.");

        response.Text.ShouldNotContain(CardNumber, Case.Sensitive);
    }
}

/// <summary>Ensures this sample follows every custom-tool contract it opts into.</summary>
public sealed class ToolContractCoverageTests
{
    [Fact]
    public void Every_tool_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.ToolContracts).ShouldBeEmpty();
}

internal sealed class FakeOrderRepository : IOrderRepository
{
    public Task<OrderReceipt> SubmitAsync(string orderId, CancellationToken cancellationToken)
        => Task.FromResult(new OrderReceipt(orderId, "submitted"));
}
