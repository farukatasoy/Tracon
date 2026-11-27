using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>The host's own speech calls: resolved under the tenant's rules, measured and recorded on the current run.</summary>
internal sealed class SpeechSynthesisService : ISpeechSynthesisService
{
    private readonly ISpeechProviderResolver _providers;
    private readonly IOptionsMonitor<TraconOptions> _options;
    private readonly IVoicePricingReader? _pricing;
    private readonly UnpricedMediaWarnings? _warnings;

    public SpeechSynthesisService(
        ISpeechProviderResolver providers,
        IOptionsMonitor<TraconOptions> options,
        IVoicePricingReader? pricing = null,
        UnpricedMediaWarnings? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        _providers = providers;
        _options = options;
        _pricing = pricing;
        _warnings = warnings;
    }

    /// <inheritdoc />
    public async ValueTask<SpeechSynthesisResult> SynthesizeAsync(
        SpeechRequest request,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("The speech request has no text.", nameof(request));
        }

        var synthesizer = await _providers.GetSynthesizerAsync(provider, cancellationToken).ConfigureAwait(false);

        // The limit is the synthesizer's own, the one the speak tool and the operator endpoint read. The text is not cut: a
        // sentence nobody hears is worse than a refusal that says why.
        if (request.Text.Length > synthesizer.MaxCharactersPerRequest)
        {
            throw new TraconException(
                $"The text is {request.Text.Length} characters; the limit of the speech synthesizer '{synthesizer.ProviderName}' is " +
                $"{synthesizer.MaxCharactersPerRequest}. Split the text and voice the parts.");
        }

        var audio = await synthesizer.SynthesizeAsync(request, cancellationToken).ConfigureAwait(false);
        var usage = Measure(
            synthesizer.ProviderName,
            audio.ModelId ?? request.ModelId,
            audio.CharactersBilled ?? request.Text.Length,
            audio.UsageSource);

        return new SpeechSynthesisResult
        {
            Audio = audio,
            Usage = usage,
            UsageRecorded = await NonTokenUsageReporter.ReportAsync(nodeName: null, usage).ConfigureAwait(false),
        };
    }

    /// <inheritdoc />
    public async ValueTask<SpeechDialogueSynthesisResult> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        string? provider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Turns is not { Count: > 0 })
        {
            throw new ArgumentException("The dialogue request has no turns.", nameof(request));
        }

        var synthesizer = await _providers.GetDialogueSynthesizerAsync(provider, cancellationToken).ConfigureAwait(false);
        var dialogue = await synthesizer.SynthesizeDialogueAsync(request, cancellationToken).ConfigureAwait(false);
        var usage = Measure(
            synthesizer.ProviderName,
            dialogue.Audio.ModelId ?? request.ModelId,
            dialogue.Audio.CharactersBilled ?? request.Turns.Sum(static turn => turn.Text.Length),
            dialogue.Audio.UsageSource);

        return new SpeechDialogueSynthesisResult
        {
            Dialogue = dialogue,
            Usage = usage,
            UsageRecorded = await NonTokenUsageReporter.ReportAsync(nodeName: null, usage).ConfigureAwait(false),
        };
    }

    private ToolCallUsage Measure(string providerName, string? model, long characters, SpeechUsageSource source)
    {
        // A registered reader (the speech package's, or the host's own) knows the default synthesizer's configured model; without
        // one, the price table is read with the model the call reported.
        var cost = _pricing is null
            ? VoiceCharacterPricing.Price(_options.CurrentValue.Pricing, providerName, model, characters, _warnings)
            : _pricing.ForSynthesis(providerName, model, characters);

        return new ToolCallUsage
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = characters,
            Cost = cost,
            Currency = _pricing?.Currency ?? _options.CurrentValue.Pricing.Currency,
            IsEstimated = source != SpeechUsageSource.Provider,
            Provider = providerName,
            Model = model,
        };
    }
}
