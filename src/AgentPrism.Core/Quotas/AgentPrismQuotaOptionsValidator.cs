using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismQuotaOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismQuotaOptionsValidator : IValidateOptions<AgentPrismQuotaOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismQuotaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        // Saat dilimi BASLANGICTA dogrulanir. Calisma aninda ResolveTimeZone()
        // UTC'ye duser: yanlis bir ad yuzunden tum trafigin kesilmesi kabul
        // edilemez. Ama hatanin sessiz kalmasi da kabul edilemez — burada
        // uygulamayi baslatmadan soyleriz.
        if (string.IsNullOrWhiteSpace(options.TimeZone))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismQuotaOptions)}.{nameof(AgentPrismQuotaOptions.TimeZone)} cannot be empty.");
        }
        else
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
            }
            catch (Exception exception) when (
                exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismQuotaOptions)}.{nameof(AgentPrismQuotaOptions.TimeZone)} " +
                    $"gecerli bir saat dilimi degil: '{options.TimeZone}'.");
            }
        }

        foreach (var percent in options.ThresholdPercents)
        {
            if (percent is <= 0 or > 100)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismQuotaOptions)}.{nameof(AgentPrismQuotaOptions.ThresholdPercents)} " +
                    $"degerleri 1-100 araliginda olmalidir. Actual value: {percent}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
