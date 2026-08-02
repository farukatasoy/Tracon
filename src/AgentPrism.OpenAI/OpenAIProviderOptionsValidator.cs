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
/// </remarks>
public sealed class OpenAIProviderOptionsValidator : IValidateOptions<OpenAIProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(OpenAIProviderOptions)}.{nameof(OpenAIProviderOptions.ApiKey)} bos olamaz. " +
                "Anahtari `UseOpenAI(apiKey)` cagrisinda verin veya " +
                $"'{OpenAIProviderOptions.SectionName}:{nameof(OpenAIProviderOptions.ApiKey)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
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
