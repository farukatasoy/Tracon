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
internal sealed class VoicePricing : IVoicePricingReader, IConfiguredMediaModelSource
{
    private readonly IOptions<TraconOptions> _options;
    private readonly IOptions<VoiceOptions> _voiceOptions;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly ISpeechTranscriber _transcriber;
    private readonly UnpricedMediaWarnings? _warnings;

    public VoicePricing(
        IOptions<TraconOptions> options,
        IOptions<VoiceOptions> voiceOptions,
        ISpeechSynthesizer synthesizer,
        ISpeechTranscriber transcriber,
        UnpricedMediaWarnings? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(voiceOptions);
        ArgumentNullException.ThrowIfNull(synthesizer);
        ArgumentNullException.ThrowIfNull(transcriber);

        _options = options;
        _voiceOptions = voiceOptions;
        _synthesizer = synthesizer;
        _transcriber = transcriber;
        _warnings = warnings;
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

        return VoiceCharacterPricing.Price(_options.Value.Pricing, providerName, model, characters, _warnings);
    }

    /// <inheritdoc />
    public decimal? ForTranscription(string providerName, string? modelId, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(providerName);

        var model = modelId ?? DefaultTranscriptionModel(providerName);

        if (MediaPriceLookup.Voice(_options.Value.Pricing, providerName, model)?.PerMinute is { } rate)
        {
            return rate * (decimal)duration.TotalMinutes;
        }

        _warnings?.WarnOnce(MediaPriceKind.Transcription, providerName, model);

        return null;
    }

    /// <summary>Names the speech models the host configured: what the default synthesizer and transcriber will run.</summary>
    /// <remarks>
    /// Reads the options and the two bound clients only, never a saved definition or a tenant's own provider: the models a
    /// tenant brought are named once in the log, the first time a call finds no price for them. A model is listed under the
    /// name the pricing lookup uses, the provider name of the client that does the work, not the <c>Provider</c> setting.
    /// </remarks>
    public IReadOnlyList<ConfiguredMediaModel> ConfiguredModels()
    {
        var models = new List<ConfiguredMediaModel>(2);

        if (DefaultSynthesisModel(_synthesizer.ProviderName) is { Length: > 0 } synthesis)
        {
            models.Add(new ConfiguredMediaModel(MediaPriceKind.SpeechSynthesis, _synthesizer.ProviderName, synthesis));
        }

        if (DefaultTranscriptionModel(_transcriber.ProviderName) is { Length: > 0 } transcription)
        {
            models.Add(new ConfiguredMediaModel(MediaPriceKind.Transcription, _transcriber.ProviderName, transcription));
        }

        return models;
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
}
