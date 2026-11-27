namespace Tracon;

/// <summary>The kinds of media call whose price comes from <c>Tracon:Pricing:Voice</c> or <c>Tracon:Pricing:Images</c>.</summary>
internal enum MediaPriceKind
{
    /// <summary>Speech synthesis, priced by billed character (<c>Tracon:Pricing:Voice</c>, <c>PerMillionCharacters</c>).</summary>
    SpeechSynthesis,

    /// <summary>Transcription, priced by audio minute (<c>Tracon:Pricing:Voice</c>, <c>PerMinute</c>).</summary>
    Transcription,

    /// <summary>A live voice session, priced by session minute (<c>Tracon:Pricing:Voice</c>, <c>PerMinute</c>).</summary>
    LiveVoice,

    /// <summary>Image generation (<c>Tracon:Pricing:Images</c>, <c>PerImage</c> or <c>OutputCostPerMillionTokens</c>).</summary>
    Image,
}

/// <summary>
/// The one place that finds the configured price of a media model and decides whether a model counts as priced.
/// </summary>
/// <remarks>
/// <para>
/// Four lookups price a media call (speech synthesis, transcription, a live voice session, an image), and two readers need to
/// agree with them about what "no price" means: the warning that says so once while a call is priced, and the diagnostics
/// report with its startup warning. All six read through this class, so a rule that changes here changes everywhere and the
/// report cannot call a model priced that the call prices at nothing.
/// </para>
/// <para>
/// A model counts as priced for a kind when the rate that kind needs is set. A voice model that carries only a per-minute
/// rate is priced for transcription and not for synthesis: the synthesis would be recorded without a cost.
/// </para>
/// </remarks>
internal static class MediaPriceLookup
{
    /// <summary>Finds the voice price entry of a provider and model.</summary>
    /// <param name="pricing">The price table.</param>
    /// <param name="provider">The provider name, or <see langword="null"/> when unknown.</param>
    /// <param name="model">The model, or <see langword="null"/> when unknown.</param>
    /// <returns>The entry, or <see langword="null"/> when there is none or either name is missing.</returns>
    public static VoicePriceOverride? Voice(TraconPricingOptions pricing, string? provider, string? model)
    {
        ArgumentNullException.ThrowIfNull(pricing);

        return provider is { Length: > 0 }
               && model is { Length: > 0 }
               && pricing.Voice.TryGetValue(provider, out var models)
               && models.TryGetValue(model, out var price)
            ? price
            : null;
    }

    /// <summary>Finds the image price entry of a provider and model.</summary>
    /// <param name="pricing">The price table.</param>
    /// <param name="provider">The provider name, or <see langword="null"/> when unknown.</param>
    /// <param name="model">The model, or <see langword="null"/> when unknown.</param>
    /// <returns>The entry, or <see langword="null"/> when there is none or either name is missing.</returns>
    public static ImagePriceOverride? Image(TraconPricingOptions pricing, string? provider, string? model)
    {
        ArgumentNullException.ThrowIfNull(pricing);

        return provider is { Length: > 0 }
               && model is { Length: > 0 }
               && pricing.Images.TryGetValue(provider, out var models)
               && models.TryGetValue(model, out var price)
            ? price
            : null;
    }

    /// <summary>Tells whether the price table prices a model for a kind of call.</summary>
    /// <param name="kind">The kind of call.</param>
    /// <param name="pricing">The price table.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model.</param>
    /// <returns><see langword="true"/> when the rate the kind needs is set.</returns>
    public static bool IsPriced(MediaPriceKind kind, TraconPricingOptions pricing, string? provider, string? model)
        => kind switch
        {
            MediaPriceKind.SpeechSynthesis => Voice(pricing, provider, model)?.PerMillionCharacters is not null,
            MediaPriceKind.Transcription or MediaPriceKind.LiveVoice => Voice(pricing, provider, model)?.PerMinute is not null,
            MediaPriceKind.Image => Image(pricing, provider, model) is { } image
                                              && (image.PerImage is not null || image.OutputCostPerMillionTokens is not null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown media price kind."),
        };

    /// <summary>Gets the short name of a kind, as the diagnostics report and the warnings print it.</summary>
    /// <param name="kind">The kind of call.</param>
    /// <returns>The name.</returns>
    public static string Label(MediaPriceKind kind)
        => kind switch
        {
            MediaPriceKind.SpeechSynthesis => "speech synthesis",
            MediaPriceKind.Transcription => "transcription",
            MediaPriceKind.LiveVoice => "live voice",
            MediaPriceKind.Image => "image generation",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown media price kind."),
        };

    /// <summary>Gets the configuration key that prices a model for a kind of call.</summary>
    /// <param name="kind">The kind of call.</param>
    /// <param name="provider">The provider name.</param>
    /// <param name="model">The model.</param>
    /// <returns>The full key, ready to be set.</returns>
    public static string Setting(MediaPriceKind kind, string provider, string model)
        => kind switch
        {
            MediaPriceKind.SpeechSynthesis => $"Tracon:Pricing:Voice:{provider}:{model}:PerMillionCharacters",
            MediaPriceKind.Transcription or MediaPriceKind.LiveVoice => $"Tracon:Pricing:Voice:{provider}:{model}:PerMinute",
            MediaPriceKind.Image => $"Tracon:Pricing:Images:{provider}:{model}:PerImage (or OutputCostPerMillionTokens)",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown media price kind."),
        };
}
