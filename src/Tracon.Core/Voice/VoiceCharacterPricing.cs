namespace Tracon;

/// <summary>Prices a synthesis by the quantity of its request, from <c>Tracon:Pricing:Voice</c>.</summary>
/// <remarks>
/// The one formula for a character price: the speech tools' reader and the speech synthesis service both read it, so the two cannot
/// price the same audio differently. The quantity is <see cref="SpeechAudio.CharactersBilled"/>: what the provider reported for the
/// request, in its own unit, or the length of the text sent when it reported none. The price table is in that unit; nothing here
/// converts between it and characters.
/// </remarks>
internal static class VoiceCharacterPricing
{
    /// <summary>Prices the quantity of one request.</summary>
    /// <param name="pricing">The price table.</param>
    /// <param name="provider">The provider name of the synthesizer that did the work.</param>
    /// <param name="model">The model that produced the audio, or <see langword="null"/> when unknown.</param>
    /// <param name="characters">The quantity of the request, in the provider's unit (<see cref="SpeechAudio.CharactersBilled"/>).</param>
    /// <returns>The amount, or <see langword="null"/> when the provider and model have no price; never zero for an unknown one.</returns>
    public static decimal? Price(TraconPricingOptions pricing, string? provider, string? model, decimal characters)
    {
        ArgumentNullException.ThrowIfNull(pricing);

        if (provider is not { Length: > 0 } || model is not { Length: > 0 })
        {
            return null;
        }

        return pricing.Voice.TryGetValue(provider, out var models)
               && models.TryGetValue(model, out var price)
               && price.PerMillionCharacters is { } rate
            ? rate * characters / 1_000_000m
            : null;
    }
}
