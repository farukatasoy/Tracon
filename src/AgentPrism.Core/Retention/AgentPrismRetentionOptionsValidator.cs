using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismRetentionOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismRetentionOptionsValidator : IValidateOptions<AgentPrismRetentionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.BatchSize < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismRetentionOptions)}.{nameof(AgentPrismRetentionOptions.BatchSize)} " +
                $"must be at least 1. Actual value: {options.BatchSize}.");
        }

        if (options.BatchDelay < TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismRetentionOptions)}.{nameof(AgentPrismRetentionOptions.BatchDelay)} " +
                $"negatif olamaz. Actual value: {options.BatchDelay}.");
        }

        foreach (var target in RetentionTargets.All)
        {
            if (options.ForTarget(target) is { MaxAgeDays: < 1 } invalid)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismRetentionOptions)}.{target}.{nameof(RetentionTargetOptions.MaxAgeDays)} " +
                    $"belirtiliyorsa must be at least 1. Actual value: {invalid.MaxAgeDays}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
