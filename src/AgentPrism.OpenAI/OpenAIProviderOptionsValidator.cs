using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="OpenAIProviderOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. <c>AgentPrism.OpenAI</c> AOT uyumlu kalmalidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </para>
/// <para>
/// <strong>Hata mesajlari API anahtarini icermez.</strong> Dogrulama mesajlari
/// gunluge ve baslangic istisnasina gider; anahtarin oraya sizmasi anahtari ifsa eder.
/// </para>
/// <para>
/// Bu dogrulayici hem <c>UseOpenAI()</c>'nin adsiz (varsayilan) ayar ornegini hem
/// <c>UseOpenAICompatible()</c>'in adlandirilmis ornklerini denetler.
/// <see cref="Validate(string?, OpenAIProviderOptions)"/>'un <c>name</c> parametresi
/// ikisini ayirt eder: adsiz ornekte (<c>name</c> bos) API anahtari zorunludur; resmi
/// OpenAI anahtarsiz calismaz. Adlandirilmis bir ornekte (uyumlu saglayici) API
/// anahtari <strong>isteğe baglidir</strong> (yerel sunucular istemez) ama
/// <see cref="OpenAIProviderOptions.Endpoint"/> zorunludur — boş birakilirsa istek
/// sessizce resmi OpenAI adresine giderdi.
/// </para>
/// </remarks>
public sealed class OpenAIProviderOptionsValidator : IValidateOptions<OpenAIProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var isDefaultInstance = string.IsNullOrEmpty(name);
        List<string>? failures = null;

        if (isDefaultInstance && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.ApiKey)} bos olamaz. " +
                "Anahtari `UseOpenAI(apiKey)` cagrisinda verin veya " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.ApiKey)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        if (!isDefaultInstance && options.Endpoint is null)
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Endpoint)} uyumlu " +
                "saglayicilar icin zorunludur. Bos birakilirsa istek sessizce resmi OpenAI adresine " +
                "giderdi. `UseOpenAICompatible(ad, o => o.Endpoint = new Uri(\"https://...\"))` ile verin.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Endpoint)} mutlak bir adres olmalidir. " +
                $"Gelen deger: '{options.Endpoint}'.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Timeout)} sifirdan buyuk olmalidir. " +
                $"Gelen deger: {timeout}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.Models)}[{index}] " +
                    "icin model adi bos olamaz.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
