using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="OnlineEvaluationOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class OnlineEvaluationOptionsValidator : IValidateOptions<OnlineEvaluationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OnlineEvaluationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.SampleRate is < 0.0 or > 1.0)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.SampleRate)} " +
                $"must be between 0.0 and 1.0. Actual value: {options.SampleRate}.");
        }

        if (options.MaxScoresPerHour < 0)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.MaxScoresPerHour)} " +
                $"must not be negative. Actual value: {options.MaxScoresPerHour}.");
        }

        if (options.LowScoreThreshold is < 0 or > 100)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.LowScoreThreshold)} " +
                $"must be between 0 and 100. Actual value: {options.LowScoreThreshold}.");
        }

        if (options.MinSampleSize < 1)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.MinSampleSize)} " +
                $"must be at least 1. Actual value: {options.MinSampleSize}.");
        }

        if (options.EvaluationWindow <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.EvaluationWindow)} " +
                $"must be greater than zero. Actual value: {options.EvaluationWindow}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
