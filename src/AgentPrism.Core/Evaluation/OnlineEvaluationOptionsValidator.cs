using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="OnlineEvaluationOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir
/// ve <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class OnlineEvaluationOptionsValidator : IValidateOptions<OnlineEvaluationOptions>
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
                $"0.0 ile 1.0 arasinda olmalidir. Actual value: {options.SampleRate}.");
        }

        if (options.MaxScoresPerHour < 0)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.MaxScoresPerHour)} " +
                $"negatif olamaz. Actual value: {options.MaxScoresPerHour}.");
        }

        if (options.LowScoreThreshold is < 0 or > 100)
        {
            (failures ??= []).Add(
                $"{nameof(OnlineEvaluationOptions)}.{nameof(OnlineEvaluationOptions.LowScoreThreshold)} " +
                $"0 ile 100 arasinda olmalidir. Actual value: {options.LowScoreThreshold}.");
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
