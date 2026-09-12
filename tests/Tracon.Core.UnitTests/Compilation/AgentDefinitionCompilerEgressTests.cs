using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Verifies that an agent definition naming a provider a tenant's egress
/// policy forbids is rejected AT COMPILE TIME (phase 65, F-119) — not only
/// when a real network call would go out.
/// </summary>
public sealed class AgentDefinitionCompilerEgressTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Compiling_an_agent_bound_to_a_forbidden_provider_throws_a_compilation_exception()
    {
        var tenantContext = new FixedTenantContext(Tenant);
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(Tenant, ["anthropic"]);

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai")],
            tenantContext: tenantContext,
            tenantEgressPolicies: egress);

        var compiler = new AgentDefinitionCompiler(
            registry,
            TestData.Registry(),
            tenantContext: tenantContext);

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "openai") };

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await compiler.CompileAsync(definition, CancellationToken.None));

        exception.Message.ShouldContain("openai");
        exception.AgentName.ShouldBe(definition.Name);
    }

    [Fact]
    public async Task An_allowed_provider_compiles_normally()
    {
        var tenantContext = new FixedTenantContext(Tenant);
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(Tenant, ["openai"]);

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai")],
            tenantContext: tenantContext,
            tenantEgressPolicies: egress);

        var compiler = new AgentDefinitionCompiler(
            registry,
            TestData.Registry(),
            tenantContext: tenantContext);

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "openai") };

        var agent = await compiler.CompileAsync(definition, CancellationToken.None);

        agent.ShouldNotBeNull();
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
