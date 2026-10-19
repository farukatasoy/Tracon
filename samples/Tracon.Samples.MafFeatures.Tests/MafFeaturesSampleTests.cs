using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Testing;

namespace Tracon.Samples.MafFeatures.Tests;

/// <summary>
/// Runs the three doors of the sample through the full <c>AddTracon()</c> pipeline, using
/// only published Tracon packages.
/// </summary>
/// <remarks>
/// The guide "Use any Microsoft Agent Framework feature" quotes this sample's code and
/// makes claims about what each door keeps. These tests check four of them: the middleware
/// sees every tool call of every door, and the door that is asynchronous and names its
/// model gets the tenant's key, a cost, and the egress policy, while the synchronous
/// factory door gets none of the three.
/// </remarks>
public sealed class MafFeaturesSampleTests
{
    private const string TenantKeyName = "Tracon:ProviderKeys:Byok";
    private const string TenantKey = "tenant-key";

    [Theory]
    [InlineData(MafFeaturesRegistration.SupportAgentName)]
    [InlineData(OrderAssistantRegistration.AgentName)]
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
    public async Task A_factory_agent_run_uses_the_setup_key_and_records_no_cost()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(
            provider,
            new ModelBinding { Provider = "byok", Model = "fake-model" },
            withTenantKey: true);

        // The tenant has its own key for this provider. The factory agent cannot use it.
        await BindTenantKeyAsync(host, "byok");

        var agent = await host.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(OrderAssistantRegistration.AgentName, culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        await agent.RunAsync("Is order 1042 open?", cancellationToken: TestContext.Current.CancellationToken);

        provider.Keys.ShouldBe([CredentialRecordingProvider.SetupKey]);

        var run = await SingleRunAsync(host, OrderAssistantRegistration.AgentName);
        run.ModelId.ShouldBeNull();
        run.Cost.ShouldBeNull();
    }

    [Fact]
    public async Task The_egress_policy_refuses_a_custom_source_agent_but_does_not_reach_a_factory_agent()
    {
        var provider = new CredentialRecordingProvider("byok", PricedModel);
        await using var host = BuildHost(provider, new ModelBinding { Provider = "byok", Model = "fake-model" });

        // The tenant may call one provider only, and it is not the one these agents use.
        await host.GetRequiredService<ITenantEgressPolicyStore>().UpsertAsync(
            host.GetRequiredService<ITenantContext>().TenantId,
            ["allowed-provider"],
            TestContext.Current.CancellationToken);

        var catalog = host.GetRequiredService<IAgentCatalog>();

        // The custom source builds its client with the asynchronous call, which checks the policy.
        await Should.ThrowAsync<TraconTenantProviderRefusedException>(async () =>
            await catalog.ResolveAsync(InvoiceAssistantSource.AgentName, culture: null, TestContext.Current.CancellationToken));

        // The factory's synchronous call does not check it: the agent runs against a provider
        // the tenant may not use. This is the loss the guide names.
        var factoryAgent = await catalog.ResolveAsync(OrderAssistantRegistration.AgentName, culture: null, TestContext.Current.CancellationToken);
        factoryAgent.ShouldNotBeNull();

        await factoryAgent.RunAsync("Is order 1042 open?", cancellationToken: TestContext.Current.CancellationToken);

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

    private static ServiceProvider BuildHost(IModelProvider provider, ModelBinding model, bool withTenantKey = false)
    {
        var services = new ServiceCollection();

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
