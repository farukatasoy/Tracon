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
/// The model is the one the provider reported (<see cref="SpeechAudio.ModelId"/>,
/// <see cref="SpeechTranscript.ModelId"/>), then the one the request named. Only
/// when neither is known does the default provider's name fall back to the
/// configured model, and only the built-in client to its own default model. Any
/// other provider with no known model has no price: it never borrows the default's.
/// </para>
/// </remarks>
internal sealed class VoicePricing : IVoicePricingReader
{
    private readonly IOptions<TraconOptions> _options;
    private readonly IOptions<VoiceOptions> _voiceOptions;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly ISpeechTranscriber _transcriber;

    public VoicePricing(
        IOptions<TraconOptions> options,
        IOptions<VoiceOptions> voiceOptions,
        ISpeechSynthesizer synthesizer,
        ISpeechTranscriber transcriber)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(voiceOptions);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(transcriber);

        _options = options;
        _voiceOptions = voiceOptions;
        _synthesizer = synthesizer;
        _transcriber = transcriber;
    }

    /// <inheritdoc />
    public string? Currency => _options.Value.Pricing.Currency;

    /// <inheritdoc />
    /// <remarks>
    /// Uses the bound synthesizer and the configured synthesis model. The HTTP
    /// layer does not know the model name and should not.
    /// </remarks>
    public decimal? ForCharacters(decimal characters)
        => ForSynthesis(_synthesizer.ProviderName, modelId: null, characters);

    /// <inheritdoc />
    public decimal? ForSynthesis(string providerName, string? modelId, decimal characters)
    {
        ArgumentNullException.ThrowIfNull(providerName);

        var model = modelId ?? DefaultSynthesisModel(providerName);

        return Find(providerName, model)?.PerMillionCharacters is { } rate
            ? rate * characters / 1_000_000m
            : null;
    }

    /// <inheritdoc />
    public decimal? ForTranscription(string providerName, string? modelId, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(providerName);

        var model = modelId ?? DefaultTranscriptionModel(providerName);

        return Find(providerName, model)?.PerMinute is { } rate
            ? rate * (decimal)duration.TotalMinutes
            : null;
    }

    /// <summary>The configured model of the default synthesizer, for its own provider only.</summary>
    private string? DefaultSynthesisModel(string providerName)
        => IsSameProvider(providerName, _synthesizer.ProviderName)
            ? _voiceOptions.Value.SynthesisModelId
              ?? (_synthesizer is ElevenLabsSpeechClient ? ElevenLabsSpeechClient.DefaultSynthesisModel : null)
            : null;

    /// <summary>The configured model of the default transcriber, for its own provider only.</summary>
    private string? DefaultTranscriptionModel(string providerName)
        => IsSameProvider(providerName, _transcriber.ProviderName)
            ? _voiceOptions.Value.TranscriptionModelId
              ?? (_transcriber is ElevenLabsSpeechClient ? ElevenLabsSpeechClient.DefaultTranscriptionModel : null)
            : null;

    private static bool IsSameProvider(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

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
