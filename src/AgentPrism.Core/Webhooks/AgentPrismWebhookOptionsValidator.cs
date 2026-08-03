using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismWebhookOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismWebhookOptionsValidator : IValidateOptions<AgentPrismWebhookOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismWebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.Timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.Timeout)} " +
                $"sifirdan buyuk olmalidir. Gelen deger: {options.Timeout}.");
        }

        if (options.MaxResponseBytes < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.MaxResponseBytes)} " +
                $"en az 1 olmalidir. Gelen deger: {options.MaxResponseBytes}.");
        }

        if (options.DisableAfterConsecutiveFailures < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.DisableAfterConsecutiveFailures)} " +
                $"en az 1 olmalidir. Gelen deger: {options.DisableAfterConsecutiveFailures}.");
        }

        // Merdivenin uzunlugu ayni zamanda en fazla deneme sayisidir; bos bir
        // liste "hic deneme yapma" anlamina gelirdi.
        if (options.RetryDelays.Count == 0)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.RetryDelays)} " +
                "en az bir gecikme icermelidir.");
        }

        foreach (var delay in options.RetryDelays)
        {
            if (delay <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismWebhookOptions)}.{nameof(AgentPrismWebhookOptions.RetryDelays)} " +
                    $"degerleri sifirdan buyuk olmalidir. Gelen deger: {delay}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
