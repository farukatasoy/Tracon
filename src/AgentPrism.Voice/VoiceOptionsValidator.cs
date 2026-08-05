using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="VoiceOptions"/> degerlerini uygulama BASLARKEN dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilir: <c>ValidateDataAnnotations()</c> yansima kullanir ve
/// AOT uyumlulugunu bozar (bkz. <c>docs/hafiza/build-ve-analyzer.md</c>).
/// <para>
/// 🚨 Hata mesaji API anahtarini <strong>tasimaz</strong>. Anahtarin yanlis
/// oldugunu soylerken degerini yazmak, hatayi gorene sirri da vermek olurdu.
/// </para>
/// </remarks>
public sealed class VoiceOptionsValidator : IValidateOptions<VoiceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, VoiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: saglayici adi bos olamaz.");
        }
        else if (!string.Equals(options.Provider, VoiceProviderNames.ElevenLabs, StringComparison.OrdinalIgnoreCase))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: '{options.Provider}' saglayicisi taninmiyor. " +
                $"Yerlesik saglayici: '{VoiceProviderNames.ElevenLabs}'. " +
                "Kendi uygulamanizi kaydetmek icin ISpeechSynthesizer/ISpeechTranscriber servislerini " +
                "UseVoice cagrisindan ONCE kaydedin; kayitli bir uygulama korunur.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: API anahtari verilmedi. " +
                "Anahtar bir sirdir: `dotnet user-secrets set \"AgentPrism:Voice:ApiKey\" \"...\"`.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: adres mutlak olmalidir.");
        }

        if (options.MaxCharactersPerRequest <= 0)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: karakter siniri sifirdan buyuk olmalidir.");
        }

        if (options.MaxConcurrentRequests <= 0)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: eszamanli istek siniri sifirdan buyuk olmalidir.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputFormat))
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: cikti bicimi bos olamaz.");
        }
        else if (!IsStorableFormat(options.OutputFormat))
        {
            (failures ??= []).Add(
                $"{nameof(VoiceOptions)}: '{options.OutputFormat}' bicimi ek olarak saklanamaz. " +
                "Ham PCM ve u-law ciktilari dosya basligi tasimaz; ek deposu turu sihirli bayttan " +
                $"dogrular ve boyle bir icerigi reddeder. Konteyner tasiyan bir bicim kullanin " +
                $"(ornegin '{VoiceOptions.DefaultOutputFormat}').");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add($"{nameof(VoiceOptions)}: zaman asimi sifirdan buyuk olmalidir.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Bicimin ek deposuna yazilabilecek bir konteyner tasiyip tasimadigini soyler.
    /// </summary>
    /// <param name="outputFormat">Saglayicinin bicim adi.</param>
    /// <returns>Saklanabilirse <see langword="true"/>.</returns>
    /// <remarks>
    /// <c>internal</c>: birim testleri ag cagrisi olmadan dogrular. Kural
    /// bicim adinin onekine bakar — saglayicilar <c>mp3_44100_128</c>,
    /// <c>pcm_16000</c>, <c>ulaw_8000</c> gibi adlar kullanir.
    /// </remarks>
    internal static bool IsStorableFormat(string outputFormat)
        => !outputFormat.StartsWith("pcm", StringComparison.OrdinalIgnoreCase)
           && !outputFormat.StartsWith("ulaw", StringComparison.OrdinalIgnoreCase)
           && !outputFormat.StartsWith("alaw", StringComparison.OrdinalIgnoreCase);
}
