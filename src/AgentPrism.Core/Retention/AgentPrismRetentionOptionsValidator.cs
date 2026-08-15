using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismRetentionOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>. Rationale: <c>docs/KARARLAR.md</c>, decision K-006.
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
                $"must not be negative. Actual value: {options.BatchDelay}.");
        }

        foreach (var target in RetentionTargets.All)
        {
            if (options.ForTarget(target) is { MaxAgeDays: < 1 } invalid)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismRetentionOptions)}.{target}.{nameof(RetentionTargetOptions.MaxAgeDays)} " +
                    $"must be at least 1 when specified. Actual value: {invalid.MaxAgeDays}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
