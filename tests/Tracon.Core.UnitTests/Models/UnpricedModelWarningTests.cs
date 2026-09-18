using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// A catalog model with no price is recorded honestly and silently; the silence
/// is what this warning removes.
/// </summary>
/// <remarks>
/// 🚨 What this closes: a sample installation defined thirteen models and not
/// one of them carried a price, so every single run was written with empty cost
/// columns and <see cref="PricingSource.Unknown"/>. The mechanism was right —
/// zero would have claimed the models were free — but the only way to find out
/// why the cost report was empty was to read the <c>pricing_source</c> value on
/// a row.
/// </remarks>
public sealed class UnpricedModelWarningTests
{
    private static ModelDescriptor Model(string name, decimal? input = null, decimal? output = null)
        => new()
        {
            Name = name,
            InputCostPerMillionTokens = input,
            OutputCostPerMillionTokens = output,
        };

    private static async Task<IReadOnlyList<string>> WarningsAsync(
        IEnumerable<IModelProvider> providers,
        TraconPricingOptions? pricing = null)
    {
        var logger = new CollectingLogger();

        var service = new UnpricedModelWarningService(
            providers,
            Options.Create(new TraconOptions { Pricing = pricing ?? new TraconPricingOptions() }),
            logger);

        await service.StartAsync(CancellationToken.None);

        return logger.Warnings;
    }

    [Fact]
    public async Task An_unpriced_catalog_names_every_model_it_cannot_price()
    {
        var provider = new CatalogProvider("openai", Model("gpt-5.4-mini"), Model("gpt-5.6-luna"));

        var warnings = await WarningsAsync([provider]);

        var warning = warnings.ShouldHaveSingleItem();

        warning.ShouldContain("openai/gpt-5.4-mini");
        warning.ShouldContain("openai/gpt-5.6-luna");

        // The operator needs the FIX in the message, not only the symptom.
        warning.ShouldContain("Tracon:Pricing:Providers");
    }

    [Fact]
    public async Task A_fully_priced_catalog_says_nothing()
    {
        var provider = new CatalogProvider("openai", Model("gpt-5.4-mini", input: 0.25m, output: 2m));

        (await WarningsAsync([provider])).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_price_from_configuration_counts_as_priced()
    {
        // The resolver falls through to Tracon:Pricing when the catalog entry
        // carries no rate; the warning must fall through the same way or it
        // would cry wolf at a correctly configured installation.
        var provider = new CatalogProvider("openai", Model("gpt-5.4-mini"));

        var pricing = new TraconPricingOptions();
        pricing.Providers["openai"] = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5.4-mini"] = new() { InputCostPerMillionTokens = 0.25m },
        };

        (await WarningsAsync([provider], pricing)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_input_only_price_counts_as_priced()
    {
        // Same "either rate is enough" rule RunPricingResolver applies. A model
        // priced on input alone resolves to PricingSource.Catalog, not Unknown.
        var provider = new CatalogProvider("openai", Model("gpt-5.4-mini", input: 0.25m));

        (await WarningsAsync([provider])).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_catalog_says_nothing()
    {
        // There is nothing to be wrong about, and an installation that defines
        // no catalog at all is a supported configuration.
        var provider = new CatalogProvider("openai");

        (await WarningsAsync([provider])).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_provider_registered_twice_is_named_once()
    {
        // UseOpenAI() registers two instances, one for ChatCompletions and one
        // for Responses, and they share a catalog.
        var first = new CatalogProvider("openai", Model("gpt-5.4-mini"));
        var second = new CatalogProvider("openai", Model("gpt-5.4-mini"));

        var warning = (await WarningsAsync([first, second])).ShouldHaveSingleItem();

        warning.ShouldContain("1 catalog model(s)");
    }

    /// <summary>A provider that exists only to carry a catalog.</summary>
    /// <remarks>
    /// The warning reads <see cref="IModelProvider.Models"/> and nothing else,
    /// so a client-producing fake would add a dependency this project does not
    /// otherwise take.
    /// </remarks>
    private sealed class CatalogProvider(string name, params ModelDescriptor[] models) : IModelProvider
    {
        public string Name => name;

        public IReadOnlyList<ModelDescriptor> Models => models;

        public IChatClient CreateChatClient(ModelBinding binding) => throw new NotSupportedException();
    }

    private sealed class CollectingLogger : ILogger<UnpricedModelWarningService>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings => _warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (logLevel == LogLevel.Warning)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
