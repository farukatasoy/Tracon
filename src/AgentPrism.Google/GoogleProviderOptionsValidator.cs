using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="GoogleProviderOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// <para>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. <c>AgentPrism.Google</c> AOT uyumlu kalmalidir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </para>
/// <para>
/// <strong>Hata mesajlari API anahtarini icermez.</strong>
/// </para>
/// </remarks>
public sealed class GoogleProviderOptionsValidator : IValidateOptions<GoogleProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GoogleProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            (failures ??= []).Add(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.ApiKey)} bos olamaz. " +
                "Anahtari `UseGoogle(apiKey)` cagrisinda verin veya " +
                $"'{GoogleProviderOptions.SectionName}:{nameof(GoogleProviderOptions.ApiKey)}' " +
                "ayarini `dotnet user-secrets` icinde tanimlayin.");
        }

        if (options.Endpoint is { IsAbsoluteUri: false })
        {
            (failures ??= []).Add(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Endpoint)} mutlak bir adres olmalidir. " +
                $"Gelen deger: '{options.Endpoint}'.");
        }

        if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Timeout)} sifirdan buyuk olmalidir. " +
                $"Gelen deger: {timeout}.");
        }

        for (var index = 0; index < options.Models.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(options.Models[index]?.Name))
            {
                (failures ??= []).Add(
                    $"{nameof(GoogleProviderOptions)}.{nameof(GoogleProviderOptions.Models)}[{index}] " +
                    "icin model adi bos olamaz.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
