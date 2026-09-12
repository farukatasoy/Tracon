using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconRetentionOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconRetentionOptionsValidator : IValidateOptions<TraconRetentionOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.BatchSize < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconRetentionOptions)}.{nameof(TraconRetentionOptions.BatchSize)} " +
                $"must be at least 1. Actual value: {options.BatchSize}.");
        }

        if (options.BatchDelay < TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconRetentionOptions)}.{nameof(TraconRetentionOptions.BatchDelay)} " +
                $"must not be negative. Actual value: {options.BatchDelay}.");
        }

        foreach (var target in RetentionTargets.All)
        {
            if (options.ForTarget(target) is { MaxAgeDays: < 1 } invalid)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconRetentionOptions)}.{target}.{nameof(RetentionTargetOptions.MaxAgeDays)} " +
                    $"must be at least 1 when specified. Actual value: {invalid.MaxAgeDays}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
