using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AzureOpenAIProviderOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. <c>AgentPrism.Azure</c> AOT uyumlu kalmalidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </para>
/// <para>
/// <strong>Hata mesajlari API anahtarini ve uc adresini icermez.</strong> Dogrulama
/// mesajlari gunluge ve baslangic istisnasina gider.
/// </para>
/// </remarks>
public sealed class AzureOpenAIProviderOptionsValidator : IValidateOptions<AzureOpenAIProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AzureOpenAIProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.Endpoint is null)
        {
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} bos olamaz. " +
                "Azure OpenAI'in tek bir genel adresi yoktur; her kaynagin kendi adresi vardir. " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.Endpoint)}' " +
                "ayarini 'https://<kaynak-adi>.openai.azure.com/' bicimiyle verin.");
        }
        else if (!options.Endpoint.IsAbsoluteUri)
        {
            // Adres degerin kendisi mesaja YAZILMAZ: kurumsal kaynak adi bir
            // topolojiyi acik eder ve dogrulama mesajlari gunluge gider.
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Endpoint)} " +
                "mutlak bir adres olmalidir.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey) && options.CredentialFactory is null)
        {
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.ApiKey)} veya " +
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.CredentialFactory)} " +
                "doldurulmalidir. Anahtari `UseAzureOpenAI(endpoint, apiKey)` cagrisinda verin, " +
                $"'{AzureOpenAIProviderOptions.SectionName}:{nameof(AzureOpenAIProviderOptions.ApiKey)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin veya yonetilen kimlik icin " +
                $"{nameof(AzureOpenAIProviderOptions.CredentialFactory)} verin.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Timeout)} sifirdan buyuk olmalidir. " +
                $"Gelen deger: {timeout}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(AzureOpenAIProviderOptions)}.{nameof(AzureOpenAIProviderOptions.Models)}[{index}] " +
                    "icin deployment adi bos olamaz.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
