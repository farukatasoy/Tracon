using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.Testing;

namespace Tracon.Samples.MafFeatures.Tests;

/// <summary>
/// Runs the doors of the sample through the full <c>AddTracon()</c> pipeline, using
/// only published Tracon packages.
/// </summary>
/// <remarks>
/// The guide "Use any Microsoft Agent Framework feature" quotes this sample's code and
/// makes claims about what each door keeps. These tests check them: the middleware sees
/// every tool call of every door; an extension adds a context provider and a chat client
/// ring to a data agent, and the ring sees every model call; a factory agent that declares
/// its model and a custom source get the tenant's key, a cost, and the egress policy; and
/// a factory agent that declares no model gets none of the three, and Tracon says so once.
/// </remarks>
public sealed class MafFeaturesSampleTests
{
    private const string TenantKeyName = "Tracon:ProviderKeys:Byok";
    private const string TenantKey = "tenant-key";

    [Theory]
    [InlineData(MafFeaturesRegistration.SupportAgentName)]
    [InlineData(OrderAssistantRegistration.AgentName)]
    [InlineData(PlainOrderAssistantRegistration.AgentName)]
    [InlineData(InvoiceAssistantSource.AgentName)]
    public async Task Tool_middleware_in_a_decorator_sees_the_tool_call_of_every_door(string agentName)
    {
        var provider = new FakeModelProvider("fake")
            .CallsTool(SearchOrdersToolRegistration.ToolName, new { customer = "acme" })
            .EchoesLastToolResult();

        await using var host = BuildHost(provider, new ModelBinding { Provider = "fake", Model = "fake-model" });

        var agent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(agentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        var response = await agent.RunAsync("What does acme have open?", cancellationToken: TestContext.Current.CancellationToken);

        // The middleware saw the call...
        host.GetRequiredService<ToolCallLog>().Calls
            .ShouldBe([$"{agentName}:{SearchOrdersToolRegistration.ToolName}"]);

        // ...and the tool still ran: the middleware added to the chain, it did not replace it.
        response.Text.ShouldContain("1042");
    }

    [Fact]
    public async Task An_extension_adds_a_context_provider_to_a_data_agent_and_the_agent_keeps_the_tenants_key_and_cost()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(
            provider,
            new ModelBinding { Provider = "byok", Model = "fake-model" },
            withTenantKey: true);

        await BindTenantKeyAsync(host, "byok");

        var agent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(MafFeaturesRegistration.SupportAgentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        await agent.RunAsync("Can I refund 250?", cancellationToken: TestContext.Current.CancellationToken);

        // The provider the extension added reached the model call...
        provider.LastInstructions.ShouldNotBeNull().ShouldContain(PolicyNoticeExtensionRegistration.Notice);

        // ...and the agent still went through the compiler: the tenant's key and a cost.
        provider.Keys.ShouldBe([TenantKey]);

        var run = await SingleRunAsync(host, MafFeaturesRegistration.SupportAgentName);
        run.ModelId.ShouldBe("fake-model");
        run.Cost.ShouldNotBeNull().InputCost.ShouldNotBeNull().ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task An_extensions_ring_sees_every_model_call_of_a_data_agent_including_the_tool_result()
    {
        var provider = new FakeModelProvider("fake")
            .CallsTool(SearchOrdersToolRegistration.ToolName, new { customer = "acme" })
            .EchoesLastToolResult();

        await using var host = BuildHost(provider, new ModelBinding { Provider = "fake", Model = "fake-model" });

        var agent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(MafFeaturesRegistration.SupportAgentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        var response = await agent.RunAsync("What does acme have open?", cancellationToken: TestContext.Current.CancellationToken);

        // The ring is inside the tool-call loop: it saw the question and the call that took the
        // tool result to the model.
        host.GetRequiredService<ModelCallLog>().Calls.ShouldBe(
        [
            $"{MafFeaturesRegistration.SupportAgentName}:user",
            $"{MafFeaturesRegistration.SupportAgentName}:tool",
        ]);
        response.Text.ShouldContain("1042");
    }

    [Fact]
    public async Task A_custom_source_run_uses_the_tenants_own_key_and_records_cost()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(
            provider,
            new ModelBinding { Provider = "byok", Model = "fake-model" },
            withTenantKey: true);

        await BindTenantKeyAsync(host, "byok");

        var agent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(InvoiceAssistantSource.AgentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        await agent.RunAsync("Is invoice 7 paid?", cancellationToken: TestContext.Current.CancellationToken);

        provider.Keys.ShouldBe([TenantKey]);

        var run = await SingleRunAsync(host, InvoiceAssistantSource.AgentName);
        run.ModelId.ShouldBe("fake-model");
        run.Cost.ShouldNotBeNull();
        run.Cost.InputCost.ShouldNotBeNull().ShouldBeGreaterThan(0m);
        run.Cost.OutputCost.ShouldNotBeNull().ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task A_factory_agent_that_declares_its_model_uses_the_tenants_key_and_records_cost()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(
            provider,
            new ModelBinding { Provider = "byok", Model = "fake-model" },
            withTenantKey: true);

        await BindTenantKeyAsync(host, "byok");

        var agent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(OrderAssistantRegistration.AgentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        await agent.RunAsync("Is order 1042 open?", cancellationToken: TestContext.Current.CancellationToken);

        provider.Keys.ShouldBe([TenantKey]);

        var run = await SingleRunAsync(host, OrderAssistantRegistration.AgentName);
        run.ModelId.ShouldBe("fake-model");
        run.Cost.ShouldNotBeNull();
        run.Cost.InputCost.ShouldNotBeNull().ShouldBeGreaterThan(0m);
        run.Cost.OutputCost.ShouldNotBeNull().ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task A_factory_agent_that_declares_no_model_uses_the_setup_key_records_no_cost_and_is_named_once()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        var logs = new CapturingLoggerProvider();
        await using var host = BuildHost(
            provider,
            new ModelBinding { Provider = "byok", Model = "fake-model" },
            withTenantKey: true,
            logs);

        // The tenant has its own key for this provider. The agent cannot use it.
        await BindTenantKeyAsync(host, "byok");

        var catalog = host.GetRequiredService<IAgentCatalog>();

        foreach (var attempt in new[] { 1, 2 })
        {
            _ = attempt;

            var agent = await catalog.ResolveAsync(PlainOrderAssistantRegistration.AgentName, culture: null, TestContext.Current.CancellationToken);
            agent.ShouldNotBeNull();
            await agent.RunAsync("Is order 1042 open?", cancellationToken: TestContext.Current.CancellationToken);
        }

        provider.Keys.ShouldBe([CredentialRecordingProvider.SetupKey, CredentialRecordingProvider.SetupKey]);

        var run = (await host.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { AgentName = PlainOrderAssistantRegistration.AgentName }, TestContext.Current.CancellationToken))
            .ToList();
        run.Count.ShouldBe(2);
        run.ShouldAllBe(static record => record.ModelId == null && record.Cost == null);

        // Tracon names the loss, once for the agent, and names the overload that removes it.
        var warning = logs.Warnings.Where(static line => line.Contains(PlainOrderAssistantRegistration.AgentName, StringComparison.Ordinal)).ShouldHaveSingleItem();
        warning.ShouldContain("declares none");
        warning.ShouldContain("AddAgent(name, model, factory)");
    }

    [Fact]
    public async Task The_egress_policy_refuses_a_custom_source_agent_and_a_factory_agent_that_declares_its_model()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(provider, new ModelBinding { Provider = "byok", Model = "fake-model" });

        // The tenant may call one provider only, and it is not the one these agents use.
        await host.GetRequiredService<ITenantEgressPolicyStore>().UpsertAsync(
            host.GetRequiredService<ITenantContext>().TenantId,
            ["allowed-provider"],
            TestContext.Current.CancellationToken);

        var catalog = host.GetRequiredService<IAgentCatalog>();

        // Both build the client with the asynchronous call, which checks the policy.
        await Should.ThrowAsync<TraconTenantProviderRefusedException>(async () =>
            await catalog.ResolveAsync(InvoiceAssistantSource.AgentName, culture: null, TestContext.Current.CancellationToken));
        await Should.ThrowAsync<TraconTenantProviderRefusedException>(async () =>
            await catalog.ResolveAsync(OrderAssistantRegistration.AgentName, culture: null, TestContext.Current.CancellationToken));

        provider.Keys.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_egress_policy_does_not_reach_a_factory_agent_that_declares_no_model()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(provider, new ModelBinding { Provider = "byok", Model = "fake-model" });

        await host.GetRequiredService<ITenantEgressPolicyStore>().UpsertAsync(
            host.GetRequiredService<ITenantContext>().TenantId,
            ["allowed-provider"],
            TestContext.Current.CancellationToken);

        // The factory's synchronous call does not check it: the agent runs against a provider
        // the tenant may not use. This is the loss the guide names.
        var plainAgent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(PlainOrderAssistantRegistration.AgentName, culture: null, TestContext.Current.CancellationToken);
        plainAgent.ShouldNotBeNull();

        await plainAgent.RunAsync("Is order 1042 open?", cancellationToken: TestContext.Current.CancellationToken);

        provider.Keys.ShouldBe([CredentialRecordingProvider.SetupKey]);
    }

    private static ModelDescriptor PricedModel { get; } = new()
    {
        Name = "fake-model",
        ContextWindowTokens = 8_192,
        MaxOutputTokens = 1_024,
        InputCostPerMillionTokens = 3m,
        OutputCostPerMillionTokens = 15m,
    };

    private static ServiceProvider BuildHost(
        IModelProvider provider,
        ModelBinding model,
        bool withTenantKey = false,
        ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();

        if (logs is not null)
        {
            services.AddLogging(logging => logging.AddProvider(logs));
        }

        if (withTenantKey)
        {
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(TenantKeyName, TenantKey)]).Build());
        }

        services.AddTracon()
            .AddModelProvider(provider)
            .AddMafFeatures(model);

        return services.BuildServiceProvider();
    }

    private static async Task BindTenantKeyAsync(ServiceProvider host, string providerName)
        => await host.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = host.GetRequiredService<ITenantContext>().TenantId,
                ProviderName = providerName,
                ApiKeyConfigurationName = TenantKeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

    private static async Task<RunRecord> SingleRunAsync(ServiceProvider host, string agentName)
        => (await host.GetRequiredService<IRunStore>()
                .QueryRunsAsync(new RunQuery { AgentName = agentName }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
}
