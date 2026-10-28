using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tenancy;

/// <summary>
/// Phase 259 (F-254): the one rule every path to a provider shares, with the fail-closed switch.
/// </summary>
/// <remarks>
/// The rule is a pure function, so its truth table is a unit test. That every caller READS the switch
/// is not: those cases cross the host and live in the functional tests of each path. The model
/// registry is exercised here because a fallback chain is the one place a single call asks the rule
/// more than once.
/// </remarks>
public sealed class TenantProviderAccessTests
{
    private const string Tenant = "acme";

    [Theory]
    [InlineData("openai", false, true)]
    [InlineData("OPENAI", true, true)]
    [InlineData("anthropic", false, true)]
    [InlineData("anthropic", true, true)]
    [InlineData("mistral", false, false)]
    [InlineData("mistral", true, false)]
    public void A_policy_judges_by_its_list_whatever_the_switch(string provider, bool requirePolicy, bool expected)
    {
        // The switch is about a tenant WITHOUT a policy. A tenant that has one is judged by its list alone,
        // so turning the switch on never refuses a provider the policy names.
        TenantProviderAccess.IsAllowed(Policy("openai", "anthropic"), provider, requirePolicy).ShouldBe(expected);
    }

    [Fact]
    public void No_policy_is_open_when_the_switch_is_off_and_closed_when_it_is_on()
    {
        TenantProviderAccess.IsAllowed(null, "openai", requirePolicy: false).ShouldBeTrue();
        TenantProviderAccess.IsAllowed(null, "openai", requirePolicy: true).ShouldBeFalse();
    }

    [Fact]
    public void An_empty_policy_refuses_everything_with_the_switch_on_or_off()
    {
        var empty = Policy();

        TenantProviderAccess.IsAllowed(empty, "openai", requirePolicy: false).ShouldBeFalse();
        TenantProviderAccess.IsAllowed(empty, "openai", requirePolicy: true).ShouldBeFalse();
    }

    [Fact]
    public void The_refusal_without_a_policy_names_the_tenant_the_provider_and_the_switch()
    {
        var exception = Should.Throw<TraconTenantProviderRefusedException>(
            () => TenantProviderAccess.EnsureAllowed(null, Tenant, "openai", requirePolicy: true, TenantProviderCallKind.Speech));

        exception.Message.ShouldContain($"Tenant '{Tenant}' has no egress policy");
        exception.Message.ShouldContain("Tracon:TenantProviders:RequireEgressPolicy");
        exception.Message.ShouldContain("speech provider 'openai'");
    }

    [Fact]
    public void A_fallback_refusal_without_a_policy_says_which_provider_it_backs_up()
    {
        var message = TenantProviderAccess.DescribeDenial(null, Tenant, "anthropic", fallbackFor: "openai");

        message.ShouldContain("(used as a fallback for 'openai')");
        message.ShouldContain("has no egress policy");
    }

    [Fact]
    public void The_refusal_with_a_policy_keeps_the_wording_clients_match_on()
    {
        // Pinned by the model-path tests and by clients that match it; the policy-present text must not
        // move because the missing-policy text was added next to it.
        TenantProviderAccess.DescribeDenial(Policy("openai"), Tenant, "anthropic")
            .ShouldBe("Tenant 'acme' is not allowed to call model provider 'anthropic'. Allowed providers: openai.");

        TenantProviderAccess.DescribeDenial(Policy("openai"), Tenant, "anthropic", fallbackFor: "openai")
            .ShouldBe(
                "Tenant 'acme' is not allowed to call model provider 'anthropic' (used as a fallback for 'openai'). " +
                "Allowed providers: openai.");
    }

    [Fact]
    public void The_switch_is_off_when_no_options_are_given()
    {
        TenantProviderAccess.PolicyRequired(null).ShouldBeFalse();
        TenantProviderAccess.PolicyRequired(Options(requirePolicy: false)).ShouldBeFalse();
        TenantProviderAccess.PolicyRequired(Options(requirePolicy: true)).ShouldBeTrue();
    }

    // ---- the model registry: one call, the primary and every fallback ----------------

    [Fact]
    public async Task The_registry_refuses_a_tenant_without_a_policy_when_one_is_required()
    {
        var registry = Registry(requirePolicy: true, policy: null);

        var exception = await Should.ThrowAsync<TraconTenantProviderRefusedException>(
            async () => await registry.CreateChatClientAsync(Binding()));

        exception.Message.ShouldContain("has no egress policy");
    }

    [Fact]
    public async Task The_registry_refuses_a_tenant_without_a_store_when_a_policy_is_required()
    {
        // A registry with no policy store has no policy for any tenant. Reading that as "nothing to
        // apply" would fail open on exactly the switch that exists to fail closed.
        var provider = new FakeModelProvider(name: "openai");
        var registry = new ModelProviderRegistry(
            [provider],
            tenantContext: new FixedTenantContext(Tenant),
            tenantProviderOptions: Options(requirePolicy: true));

        await Should.ThrowAsync<TraconTenantProviderRefusedException>(
            async () => await registry.CreateChatClientAsync(Binding()));
    }

    [Fact]
    public async Task The_registry_lets_a_tenant_without_a_policy_through_when_the_switch_is_off()
    {
        var registry = Registry(requirePolicy: false, policy: null);

        (await registry.CreateChatClientAsync(Binding())).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_registry_refuses_a_fallback_the_policy_omits_with_the_switch_on()
    {
        var registry = Registry(requirePolicy: true, policy: ["openai"]);

        var exception = await Should.ThrowAsync<TraconTenantProviderRefusedException>(
            async () => await registry.CreateChatClientAsync(Binding(fallback: "anthropic")));

        exception.Message.ShouldContain("'anthropic'");
        exception.Message.ShouldContain("(used as a fallback for 'openai')");
    }

    [Fact]
    public async Task The_registry_accepts_a_primary_and_a_fallback_the_policy_names()
    {
        var registry = Registry(requirePolicy: true, policy: ["openai", "anthropic"]);

        (await registry.CreateChatClientAsync(Binding(fallback: "anthropic"))).ShouldNotBeNull();
    }

    private static ModelProviderRegistry Registry(bool requirePolicy, string[]? policy)
    {
        var egress = new InMemoryTenantEgressPolicyStore();

        if (policy is not null)
        {
            egress.UpsertAsync(Tenant, policy).AsTask().GetAwaiter().GetResult();
        }

        return new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai"), new FakeModelProvider(name: "anthropic")],
            tenantContext: new FixedTenantContext(Tenant),
            tenantProviderBindings: new InMemoryTenantProviderBindingStore(),
            tenantEgressPolicies: egress,
            tenantProviderOptions: Options(requirePolicy));
    }

    private static ModelBinding Binding(string? fallback = null)
    {
        var binding = TestData.Binding(provider: "openai");

        return fallback is null
            ? binding
            : binding with { Fallbacks = [new ModelFallback { Provider = fallback, Model = "fallback-1" }] };
    }

    private static TenantEgressPolicy Policy(params string[] allowed)
        => new() { TenantId = Tenant, AllowedProviders = allowed, UpdatedAt = DateTimeOffset.UtcNow };

    private static IOptions<TraconTenantProviderOptions> Options(bool requirePolicy)
        => Microsoft.Extensions.Options.Options.Create(new TraconTenantProviderOptions { RequireEgressPolicy = requirePolicy });

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
