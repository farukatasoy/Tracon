using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Computes the cost of a voice call from the <c>Tracon:Pricing:Voice</c>
/// section.
/// </summary>
/// <remarks>
/// <para>
/// Tracon <strong>never invents</strong> a price. When the
/// configuration has no match, the cost stays <see langword="null"/> —
/// <strong>not</strong> zero. Writing zero would say "this call was free."
/// </para>
/// <para>
/// The price table is keyed by the <c>ProviderName</c> of the implementation that
/// did the work, not by <see cref="VoiceOptions.Provider"/>. That setting
/// only selects the built-in client; with a consumer's own provider it still says
/// <c>elevenlabs</c>, and the cost was looked up under the wrong provider.
/// </para>
/// <para>
/// When no model is configured, only the built-in client falls back to its own
/// default model. A consumer's provider has no model Tracon knows, so the cost
/// stays <see langword="null"/> instead of borrowing an ElevenLabs price.
/// </para>
/// </remarks>
internal sealed class VoicePricing : IVoicePricingReader
{
    private readonly IOptions<TraconOptions> _options;
    private readonly IOptions<VoiceOptions> _voiceOptions;
    private readonly ISpeechSynthesizer _synthesizer;

    public VoicePricing(
        IOptions<TraconOptions> options,
        IOptions<VoiceOptions> voiceOptions,
        ISpeechSynthesizer synthesizer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(voiceOptions);
        ArgumentNullException.ThrowIfNull(synthesizer);

        _options = options;
        _voiceOptions = voiceOptions;
        _synthesizer = synthesizer;
    }

    /// <inheritdoc />
    public string? Currency => _options.Value.Pricing.Currency;

    /// <inheritdoc />
    /// <remarks>
    /// Uses the bound synthesizer and the configured synthesis model. The HTTP
    /// layer does not know the model name and should not.
    /// </remarks>
    public decimal? ForCharacters(decimal characters)
        => ForSynthesis(_synthesizer, characters);

    /// <summary>Computes the cost of a synthesis call.</summary>
    /// <param name="synthesizer">The synthesizer that produced the audio.</param>
    /// <param name="characters">Number of billed characters.</param>
    /// <returns>The amount; <see langword="null"/> when the price is undefined.</returns>
    public decimal? ForSynthesis(ISpeechSynthesizer synthesizer, decimal characters)
    {
        ArgumentNullException.ThrowIfNull(synthesizer);

        var model = _voiceOptions.Value.SynthesisModelId
                    ?? (synthesizer is ElevenLabsSpeechClient ? ElevenLabsSpeechClient.DefaultSynthesisModel : null);

        return Find(synthesizer.ProviderName, model)?.PerMillionCharacters is { } rate
            ? rate * characters / 1_000_000m
            : null;
    }

    /// <summary>Computes the cost of a transcription call.</summary>
    /// <param name="transcriber">The transcriber that resolved the audio.</param>
    /// <param name="duration">Duration of the transcribed audio.</param>
    /// <returns>The amount; <see langword="null"/> when the price is undefined.</returns>
    public decimal? ForTranscription(ISpeechTranscriber transcriber, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(transcriber);

        var model = _voiceOptions.Value.TranscriptionModelId
                    ?? (transcriber is ElevenLabsSpeechClient ? ElevenLabsSpeechClient.DefaultTranscriptionModel : null);

        return Find(transcriber.ProviderName, model)?.PerMinute is { } rate
            ? rate * (decimal)duration.TotalMinutes
            : null;
    }

    private VoicePriceOverride? Find(string provider, string? model)
    {
        if (provider is not { Length: > 0 } || model is not { Length: > 0 })
        {
            return null;
        }

        return _options.Value.Pricing.Voice.TryGetValue(provider, out var models) &&
               models.TryGetValue(model, out var price)
            ? price
            : null;
    }
}
