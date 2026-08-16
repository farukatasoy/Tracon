using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>Verifies the price precedence order and rounding behavior of cost resolution.</summary>
public sealed class RunPricingResolverTests
{
    [Fact]
    public void Returns_null_when_there_is_no_model_or_usage()
    {
        var resolver = CreateResolver();

        resolver.Resolve("openai", null, Usage(1, 1)).ShouldBeNull();
        resolver.Resolve("openai", "gpt-x", null).ShouldBeNull();
    }

    [Fact]
    public void Catalog_price_takes_precedence_over_configuration_price()
    {
        var provider = new FakeModelProvider(name: "openai", models:
        [
            new ModelDescriptor { Name = "gpt-x", InputCostPerMillionTokens = 1m, OutputCostPerMillionTokens = 2m },
        ]);
        var resolver = CreateResolver(
            providers: [provider],
            pricing: new AgentPrismPricingOptions
            {
                Currency = "USD",
                Providers =
                {
                    ["openai"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpt-x"] = new() { InputCostPerMillionTokens = 999m, OutputCostPerMillionTokens = 999m },
                    },
                },
            });

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 1_000_000));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Catalog);
        cost.InputCost.ShouldBe(1m);
        cost.OutputCost.ShouldBe(2m);
        cost.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Configuration_is_used_when_the_catalog_has_no_price()
    {
        var provider = new FakeModelProvider(name: "openai", models:
        [
            new ModelDescriptor { Name = "gpt-x" },
        ]);
        var resolver = CreateResolver(
            providers: [provider],
            pricing: new AgentPrismPricingOptions
            {
                Currency = "USD",
                Providers =
                {
                    ["openai"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["gpt-x"] = new() { InputCostPerMillionTokens = 0.25m, OutputCostPerMillionTokens = 2m },
                    },
                },
            });

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 500_000));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Configuration);
        cost.InputCost.ShouldBe(0.25m);
        cost.OutputCost.ShouldBe(1.0m);
    }

    [Fact]
    public void Cost_is_null_not_zero_when_no_price_exists_anywhere()
    {
        var provider = new FakeModelProvider(name: "openai", models: [new ModelDescriptor { Name = "gpt-x" }]);
        var resolver = CreateResolver(providers: [provider]);

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000, 1_000));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Unknown);
        cost.InputCost.ShouldBeNull();
        cost.OutputCost.ShouldBeNull();
    }

    [Fact]
    public void Unknown_model_also_returns_Unknown()
    {
        var resolver = CreateResolver();

        var cost = resolver.Resolve("openai", "no-such-model", Usage(1, 1));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Unknown);
    }

    [Fact]
    public void First_alphabetical_catalog_match_is_used_when_no_provider_is_given()
    {
        var first = new FakeModelProvider(name: "alpha-provider", models:
        [
            new ModelDescriptor { Name = "shared-model", InputCostPerMillionTokens = 1m },
        ]);
        var second = new FakeModelProvider(name: "zeta-provider", models:
        [
            new ModelDescriptor { Name = "shared-model", InputCostPerMillionTokens = 999m },
        ]);
        var resolver = CreateResolver(providers: [second, first]);

        var cost = resolver.Resolve(provider: null, "shared-model", Usage(1_000_000, 0));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(1m);
    }

    [Fact]
    public void First_alphabetical_configuration_match_is_used_when_no_provider_is_given()
    {
        var resolver = CreateResolver(pricing: new AgentPrismPricingOptions
        {
            Providers =
            {
                ["zeta-provider"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["shared-model"] = new() { InputCostPerMillionTokens = 999m },
                },
                ["alpha-provider"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["shared-model"] = new() { InputCostPerMillionTokens = 3m },
                },
            },
        });

        var cost = resolver.Resolve(provider: null, "shared-model", Usage(1_000_000, 0));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(3m);
    }

    [Fact]
    public void Input_and_output_price_are_computed_independently()
    {
        var provider = new FakeModelProvider(name: "openai", models:
        [
            new ModelDescriptor { Name = "gpt-x", InputCostPerMillionTokens = 2m },
        ]);
        var resolver = CreateResolver(providers: [provider]);

        var cost = resolver.Resolve("openai", "gpt-x", Usage(1_000_000, 1_000_000));

        cost.ShouldNotBeNull();
        cost.InputCost.ShouldBe(2m);
        cost.OutputCost.ShouldBeNull();
    }

    private static RunUsage Usage(long input, long output)
        => new() { InputTokens = input, OutputTokens = output, TotalTokens = input + output };

    private static RunPricingResolver CreateResolver(
        IEnumerable<FakeModelProvider>? providers = null,
        AgentPrismPricingOptions? pricing = null)
    {
        var registry = new ModelProviderRegistry(providers ?? []);
        var options = Options.Create(new AgentPrismOptions { Pricing = pricing ?? new AgentPrismPricingOptions() });

        return new RunPricingResolver(registry, options);
    }
}
