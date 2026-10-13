using Microsoft.Extensions.Options;

namespace Tracon.Api;

/// <summary>
/// What this sample's nodes charge for the characters they speak: the host's own price
/// table, read from <c>Tracon:Pricing:Voice</c>.
/// </summary>
/// <remarks>
/// Tracon never prices a workflow node's work: the node reports an AMOUNT, and the
/// code that did the work is the one that knows its rate. A host that registered the
/// voice tools (<c>UseVoice</c>) can ask <c>IVoicePricingReader</c> instead; this sample
/// runs without a voice key, so it reads the same configuration section directly.
/// An engine or a model with no entry there is reported with NO amount, which the console
/// shows as "unpriced" - never as zero.
/// </remarks>
internal sealed class TonePricing(TraconOptions options)
{
    /// <summary>Reads the host's price table from the service provider a node was built with.</summary>
    /// <param name="provider">The service provider handed to the function's factory.</param>
    /// <returns>The rates.</returns>
    public static TonePricing From(IServiceProvider provider)
        => new(provider.GetRequiredService<IOptions<TraconOptions>>().Value);

    /// <summary>Builds the item a node reports for the characters it spoke.</summary>
    /// <param name="characters">The characters billed.</param>
    /// <param name="isEstimated">Whether the count is an estimate rather than the provider's own.</param>
    /// <param name="provider">The engine that did the work.</param>
    /// <param name="model">The model that did the work, or <see langword="null"/>.</param>
    /// <returns>The item, with an amount only when the table has this engine and model.</returns>
    public ToolCallUsage Characters(
        int characters,
        bool isEstimated,
        string provider = ToneSpeechSynthesizer.Name,
        string? model = "tone-1")
    {
        decimal? amount = null;

        if (model is not null &&
            options.Pricing.Voice.TryGetValue(provider, out var models) &&
            models.TryGetValue(model, out var price) &&
            price.PerMillionCharacters is { } perMillion)
        {
            amount = perMillion * characters / 1_000_000m;
        }

        return new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = characters,
            Cost = amount,
            Currency = options.Pricing.Currency,
            IsEstimated = isEstimated,
            Provider = provider,
            Model = model,
        };
    }
}
