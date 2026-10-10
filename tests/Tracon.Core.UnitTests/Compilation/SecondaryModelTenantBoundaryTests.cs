using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Verifies that the model clients an agent calls BESIDES its own model — the
/// compaction summarizer and the judge of an <c>aiJudge</c> loop criterion —
/// pass the tenant's egress policy and credential resolution exactly like the
/// agent's own model does.
/// </summary>
/// <remarks>
/// 🚨 Regression (2026-10-11): both clients were built with the synchronous
/// <see cref="IModelProviderRegistry.CreateChatClient"/>, which resolves
/// neither. A definition whose summarization model named a provider the
/// tenant's egress policy forbids compiled, and every compaction sent the
/// tenant's conversation to that provider. A tenant that brought its own key
/// for the summarization provider was billed on the shared key instead. Only
/// the agent's own model went through
/// <see cref="IModelProviderRegistry.CreateChatClientAsync"/>.
/// </remarks>
public sealed class SecondaryModelTenantBoundaryTests
{
    private const string Tenant = "acme";
    private const string TenantKeyName = "Tracon:ProviderKeys:Acme:Anthropic";

    [Theory]
    [InlineData(CompactionStrategyKind.Summarization)]
    [InlineData(CompactionStrategyKind.Pipeline)]
    public async Task A_summarization_model_the_egress_policy_forbids_is_rejected_at_compile_time(CompactionStrategyKind strategy)
    {
        var host = await Host.CreateAsync(allowedProviders: ["openai"]);
        var definition = Agent() with { Compaction = Summarizing(strategy, summarizationModel: Binding("anthropic")) };

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await host.Compiler.CompileAsync(definition, CancellationToken.None));

        exception.Message.ShouldContain("anthropic");
        exception.AgentName.ShouldBe(definition.Name);
    }

    [Fact]
    public async Task The_utility_model_is_checked_when_it_is_the_one_that_summarizes()
    {
        // The definition names no summarization model, so the application-wide
        // utility model summarizes. The tenant's conversation still leaves for
        // that provider; the policy applies to it all the same.
        var host = await Host.CreateAsync(allowedProviders: ["openai"], utilityModel: Binding("anthropic"));
        var definition = Agent() with { Compaction = Summarizing(CompactionStrategyKind.Summarization, summarizationModel: null) };

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await host.Compiler.CompileAsync(definition, CancellationToken.None));

        exception.Message.ShouldContain("anthropic");
    }

    [Fact]
    public async Task An_aiJudge_whose_judge_model_the_egress_policy_forbids_is_rejected_at_compile_time()
    {
        // The judge reads every iteration's answer: the tenant's data leaves
        // for the judge's provider exactly as it does for the agent's own.
        var host = await Host.CreateAsync(allowedProviders: ["openai"], judgeModel: Binding("anthropic"));
        var definition = JudgedAgent();

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await host.Compiler.CompileAsync(definition, CancellationToken.None));

        exception.Message.ShouldContain("anthropic");
        exception.AgentName.ShouldBe(definition.Name);
    }

    [Fact]
    public async Task Allowed_summarization_and_judge_models_compile_normally()
    {
        var host = await Host.CreateAsync(allowedProviders: ["openai", "anthropic"], judgeModel: Binding("anthropic"));
        var definition = JudgedAgent() with
        {
            Compaction = Summarizing(CompactionStrategyKind.Summarization, summarizationModel: Binding("anthropic")),
        };

        var agent = await host.Compiler.CompileAsync(definition, CancellationToken.None);

        agent.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_summarization_model_is_built_with_the_tenants_own_credential()
    {
        var host = await Host.CreateAsync(bindAnthropic: true);
        var definition = Agent() with
        {
            Compaction = Summarizing(CompactionStrategyKind.Summarization, summarizationModel: Binding("anthropic")),
        };

        await host.Compiler.CompileAsync(definition, CancellationToken.None);

        host.Anthropic.LastCredential.ShouldNotBeNull();
        host.Anthropic.LastCredential.ApiKey.ShouldBe("sk-tenant-key");
    }

    [Fact]
    public async Task The_aiJudge_model_keeps_the_setup_credential_even_when_the_tenant_binds_its_provider()
    {
        // K-613/K-706: the host configures and pays for ONE judge model, the
        // same one the run judge uses. The tenant's egress policy applies to
        // it; the tenant's own key does not.
        var host = await Host.CreateAsync(bindAnthropic: true, judgeModel: Binding("anthropic"));

        await host.Compiler.CompileAsync(JudgedAgent(), CancellationToken.None);

        host.Anthropic.LastBinding.ShouldNotBeNull();
        host.Anthropic.LastCredential.ShouldBeNull();
    }

    [Fact]
    public async Task A_tenant_binding_for_the_summarization_provider_keeps_the_agent_out_of_the_compiled_agent_cache()
    {
        // The agent's own provider has no tenant binding, so before this fix
        // the agent was cached — with the summarizer's credential baked in.
        // Deleting or rotating the tenant's key would then not reach it.
        var host = await Host.CreateAsync(bindAnthropic: true);
        var store = new InMemoryAgentDefinitionStore(host.TenantContext);
        await store.SaveAsync(Agent("support") with
        {
            Compaction = Summarizing(CompactionStrategyKind.Summarization, summarizationModel: Binding("anthropic")),
        });

        var cache = new CompiledAgentCache();
        var source = new DefinitionStoreAgentSource(store, host.Compiler, cache, host.TenantContext);

        await source.ResolveAsync("support");
        await source.ResolveAsync("support");

        host.Anthropic.LastCredential.ShouldNotBeNull();
        host.Anthropic.LastCredential.ApiKey.ShouldBe("sk-tenant-key");
        cache.Count.ShouldBe(0);
    }

    private static ModelBinding Binding(string provider) => TestData.Binding(provider: provider);

    private static AgentDefinition Agent(string name = "test-agent")
        => TestData.Definition(name) with { Model = Binding("openai") };

    private static AgentDefinition JudgedAgent()
    {
        var harness = new HarnessSettings
        {
            Loop = new LoopSettings
            {
                Criteria = [new LoopCriterion { Kind = "aiJudge", JudgeCriteria = ["Is the answer complete?"] }],
            },
        };

        return TestData.Definition(harness: harness) with { Model = Binding("openai") };
    }

    private static CompactionSettings Summarizing(CompactionStrategyKind strategy, ModelBinding? summarizationModel)
        => new() { Strategy = strategy, TriggerMessages = 20, SummarizationModel = summarizationModel };

    private sealed record Host(
        AgentDefinitionCompiler Compiler,
        FakeModelProvider Anthropic,
        ITenantContext TenantContext)
    {
        public static async Task<Host> CreateAsync(
            IReadOnlyList<string>? allowedProviders = null,
            bool bindAnthropic = false,
            ModelBinding? utilityModel = null,
            ModelBinding? judgeModel = null)
        {
            var tenantContext = new FixedTenantContext(Tenant);
            var egress = new InMemoryTenantEgressPolicyStore();
            var bindings = new InMemoryTenantProviderBindingStore();

            if (allowedProviders is not null)
            {
                await egress.UpsertAsync(Tenant, allowedProviders);
            }

            if (bindAnthropic)
            {
                await bindings.UpsertAsync(new TenantProviderBinding
                {
                    TenantId = Tenant,
                    ProviderName = "anthropic",
                    ApiKeyConfigurationName = TenantKeyName,
                    UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection([new(TenantKeyName, "sk-tenant-key")])
                .Build();

            var anthropic = new FakeModelProvider(name: "anthropic");
            var registry = new ModelProviderRegistry(
                [new FakeModelProvider(name: "openai"), anthropic],
                tenantContext: tenantContext,
                tenantProviderBindings: bindings,
                tenantEgressPolicies: egress,
                credentialResolver: new TenantProviderCredentialResolver(
                    configuration,
                    new StaticOptionsMonitor<TraconTenantProviderOptions>(new TraconTenantProviderOptions()),
                    Options.Create(new TraconOptions())));

            var compiler = new AgentDefinitionCompiler(
                registry,
                TestData.Registry(),
                tenantContext: tenantContext,
                utilityModel: utilityModel,
                judgeOptions: judgeModel is null ? null : new ModelRunJudgeOptions { Model = judgeModel });

            return new Host(compiler, anthropic, tenantContext);
        }
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
