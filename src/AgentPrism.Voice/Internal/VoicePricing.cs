using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <c>AgentPrism:Pricing:Voice</c> bolumunden bir ses cagrisinin maliyetini
/// hesaplar.
/// </summary>
/// <remarks>
/// AgentPrism fiyat <strong>uydurmaz</strong> (K-032). Yapilandirmada karsilik
/// yoksa maliyet <see langword="null"/> kalir — sifir <strong>degil</strong>.
/// Sifir yazmak "bu cagri bedavaydi" demek olurdu.
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
    /// Yapilandirilmis saglayici ve sentez modeli kullanilir. HTTP katmani model
    /// adini bilmez ve bilmemelidir.
    /// </remarks>
    public decimal? ForCharacters(decimal characters)
        => ForCharacters(
            _voiceOptions.Value.Provider,
            _voiceOptions.Value.SynthesisModelId ?? ElevenLabsSpeechClient.DefaultSynthesisModel,
            characters);

    /// <summary>Karakter bazli bir cagrinin maliyetini hesaplar.</summary>
    /// <param name="provider">Ses saglayicisinin adi.</param>
    /// <param name="model">Kullanilan model.</param>
    /// <param name="characters">Faturalanan karakter sayisi.</param>
    /// <returns>Tutar; fiyat tanimsizsa <see langword="null"/>.</returns>
    public decimal? ForCharacters(string provider, string? model, decimal characters)
        => Find(provider, model)?.PerMillionCharacters is { } rate
            ? rate * characters / 1_000_000m
            : null;

    /// <summary>Sure bazli bir cagrinin maliyetini hesaplar.</summary>
    /// <param name="provider">Ses saglayicisinin adi.</param>
    /// <param name="model">Kullanilan model.</param>
    /// <param name="duration">Cozulen sesin suresi.</param>
    /// <returns>Tutar; fiyat tanimsizsa <see langword="null"/>.</returns>
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
