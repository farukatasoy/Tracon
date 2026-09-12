using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Prices a voice session's duration at the moment the record is written.</summary>
/// <remarks>
/// <para>
/// The price is resolved when the record is <strong>written</strong> and stored
/// with it; the HTTP layer only reads what was stored. The reasoning is the same one
/// the run path already settled: a cost is a <em>price snapshot</em>. If a closed
/// session were re-priced on read, editing the price table would silently change
/// what <c>GET /api/voice/sessions</c> says yesterday's conversations cost.
/// </para>
/// <para>
/// This is also why <c>IVoicePricingReader</c> gains no <c>ForDuration</c> member:
/// nothing outside this class needs to price a duration, and adding the member would
/// invite exactly the read-time re-pricing above.
/// </para>
/// </remarks>
internal sealed class VoiceDurationPricing
{
    private readonly IOptionsMonitor<TraconOptions> _options;

    /// <summary>Creates a pricer.</summary>
    /// <param name="options">The Tracon options, which carry the price table.</param>
    public VoiceDurationPricing(IOptionsMonitor<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <summary>Prices a live session's duration.</summary>
    /// <param name="provider">The provider name, e.g. <c>openai</c>.</param>
    /// <param name="model">The model identifier.</param>
    /// <param name="seconds">The billable duration the provider reported.</param>
    /// <returns>
    /// The cost, or <see langword="null"/> when the duration is unknown or the model
    /// is not priced — never a zero, which would claim the session was free.
    /// </returns>
    public VoiceSessionCost? Price(string? provider, string? model, decimal? seconds)
    {
        if (seconds is not { } billable || provider is not { Length: > 0 } || model is not { Length: > 0 })
        {
            return null;
        }

        var pricing = _options.CurrentValue.Pricing;

        if (!pricing.Voice.TryGetValue(provider, out var models)
            || !models.TryGetValue(model, out var price)
            || price.PerMinute is not { } perMinute)
        {
            return null;
        }

        return new VoiceSessionCost
        {
            DurationCost = ComputeDurationCost(billable, perMinute),
            CharacterCost = null,
            Currency = pricing.Currency,
        };
    }

    /// <summary>Converts a billable duration and a per-minute price into a cost.</summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <param name="perMinute">The price of one minute.</param>
    /// <returns>The cost.</returns>
    /// <remarks>
    /// The formula lives here and <strong>only</strong> here. No provider package
    /// carries a second copy, and none should: a sum spelled out in two places drifts
    /// the day one of them gains a term.
    /// </remarks>
    public static decimal ComputeDurationCost(decimal seconds, decimal perMinute)
        => seconds / 60m * perMinute;
}
