using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AnthropicProviderOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. <c>AgentPrism.Anthropic</c> AOT uyumlu kalmalidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </para>
/// <para>
/// <strong>Hata mesajlari API anahtarini icermez.</strong> Dogrulama mesajlari
/// gunluge ve baslangic istisnasina gider; anahtarin oraya sizmasi anahtari ifsa eder.
/// </para>
/// </remarks>
public sealed class AnthropicProviderOptionsValidator : IValidateOptions<AnthropicProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AnthropicProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.ApiKey)} cannot be empty. " +
                "Anahtari `UseAnthropic(apiKey)` cagrisinda verin veya " +
                $"'{AnthropicProviderOptions.SectionName}:{nameof(AnthropicProviderOptions.ApiKey)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Endpoint)} mutlak bir adres olmalidir. " +
                $"Actual value: '{options.Endpoint}'.");
        }

        if (options.DefaultMaxOutputTokens <= 0)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.DefaultMaxOutputTokens)} " +
                $"must be greater than zero. Anthropic Messages API'si `max_tokens` alanini zorunlu tutar. " +
                $"Actual value: {options.DefaultMaxOutputTokens}.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Timeout)} must be greater than zero. " +
                $"Actual value: {timeout}.");
        }

        if (options.MaxRetries is { } retries && retries < 0)
        {
            (failures ??= []).Add(
                $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.MaxRetries)} negatif olamaz. " +
                $"Actual value: {retries}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(AnthropicProviderOptions)}.{nameof(AnthropicProviderOptions.Models)}[{index}] " +
                    "icin model adi cannot be empty.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
