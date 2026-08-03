using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>Maliyet cozumlemesinin fiyat sirasini ve yuvarlama davranisini dogrular.</summary>
public sealed class RunPricingResolverTests
{
    [Fact]
    public void Model_veya_kullanim_yoksa_null_doner()
    {
        var resolver = CreateResolver();

        resolver.Resolve("openai", null, Usage(1, 1)).ShouldBeNull();
        resolver.Resolve("openai", "gpt-x", null).ShouldBeNull();
    }

    [Fact]
    public void Katalog_fiyati_yapilandirma_fiyatindan_once_gelir()
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
    public void Katalogda_fiyat_yoksa_yapilandirma_kullanilir()
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
    public void Fiyat_hicbir_yerde_yoksa_maliyet_null_sifir_degil()
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
    public void Bilinmeyen_model_de_unknown_doner()
    {
        var resolver = CreateResolver();

        var cost = resolver.Resolve("openai", "hic-boyle-model-yok", Usage(1, 1));

        cost.ShouldNotBeNull();
        cost.Source.ShouldBe(PricingSource.Unknown);
    }

    [Fact]
    public void Saglayici_verilmezse_katalogda_alfabetik_ilk_eslesen_kullanilir()
    {
        var first = new FakeModelProvider(name: "alfa-provider", models:
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
    public void Saglayici_verilmezse_yapilandirmada_alfabetik_ilk_eslesen_kullanilir()
    {
        var resolver = CreateResolver(pricing: new AgentPrismPricingOptions
        {
            Providers =
            {
                ["zeta-provider"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["shared-model"] = new() { InputCostPerMillionTokens = 999m },
                },
                ["alfa-provider"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
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
    public void Girdi_ve_cikti_fiyati_birbirinden_bagimsiz_hesaplanir()
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
