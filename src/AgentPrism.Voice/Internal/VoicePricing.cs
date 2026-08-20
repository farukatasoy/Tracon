using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Computes the cost of a voice call from the <c>AgentPrism:Pricing:Voice</c>
/// section.
/// </summary>
/// <remarks>
/// AgentPrism <strong>never invents</strong> a price. When the
/// configuration has no match, the cost stays <see langword="null"/> —
/// <strong>not</strong> zero. Writing zero would say "this call was free."
/// </remarks>
internal sealed class VoicePricing : IVoicePricingReader
{
    private readonly IOptions<AgentPrismOptions> _options;
    private readonly IOptions<VoiceOptions> _voiceOptions;

    public VoicePricing(IOptions<AgentPrismOptions> options, IOptions<VoiceOptions> voiceOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(voiceOptions);

        _options = options;
        _voiceOptions = voiceOptions;
    }

    /// <inheritdoc />
    public string? Currency => _options.Value.Pricing.Currency;

    /// <inheritdoc />
    /// <remarks>
    /// Uses the configured provider and synthesis model. The HTTP layer does
    /// not know the model name and should not.
    /// </remarks>
    public decimal? ForCharacters(decimal characters)
        => ForCharacters(
            _voiceOptions.Value.Provider,
            _voiceOptions.Value.SynthesisModelId ?? ElevenLabsSpeechClient.DefaultSynthesisModel,
            characters);

    /// <summary>Computes the cost of a character-based call.</summary>
    /// <param name="provider">Name of the voice provider.</param>
    /// <param name="model">Model used.</param>
    /// <param name="characters">Number of billed characters.</param>
    /// <returns>The amount; <see langword="null"/> when the price is undefined.</returns>
    public decimal? ForCharacters(string provider, string? model, decimal characters)
        => Find(provider, model)?.PerMillionCharacters is { } rate
            ? rate * characters / 1_000_000m
            : null;

    /// <summary>Computes the cost of a duration-based call.</summary>
    /// <param name="provider">Name of the voice provider.</param>
    /// <param name="model">Model used.</param>
    /// <param name="duration">Duration of the transcribed audio.</param>
    /// <returns>The amount; <see langword="null"/> when the price is undefined.</returns>
    public decimal? ForDuration(string provider, string? model, TimeSpan duration)
        => Find(provider, model)?.PerMinute is { } rate
            ? rate * (decimal)duration.TotalMinutes
            : null;

    private VoicePriceOverride? Find(string provider, string? model)
    {
        if (model is not { Length: > 0 })
        {
            return null;
        }

        return _options.Value.Pricing.Voice.TryGetValue(provider, out var models) &&
               models.TryGetValue(model, out var price)
            ? price
            : null;
    }
}
