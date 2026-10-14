using System.Globalization;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconWebhookOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconWebhookOptionsValidator : IValidateOptions<TraconWebhookOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconWebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        // F-280: every delivery attempt arms this timeout; an out-of-range value threw
        // on every attempt.
        if (!TimerPeriod.IsValid(options.Timeout))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.Timeout)}",
                options.Timeout));
        }

        if (options.MaxResponseBytes < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.MaxResponseBytes)} " +
                $"must be at least 1. Actual value: {options.MaxResponseBytes}.");
        }

        if (options.DisableAfterConsecutiveFailures < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.DisableAfterConsecutiveFailures)} " +
                $"must be at least 1. Actual value: {options.DisableAfterConsecutiveFailures}.");
        }

        // The ladder length is also the maximum attempt count. An empty list would
        // mean "do not make any attempt".
        if (options.RetryDelays.Count == 0)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.RetryDelays)} " +
                "must contain at least one delay.");
        }

        for (var index = 0; index < options.RetryDelays.Count; index++)
        {
            var delay = options.RetryDelays[index];

            if (delay > TimeSpan.Zero)
            {
                continue;
            }

            // An entry the binder could not read stands here as zero; quote the
            // text the operator wrote instead of the zero (F-247).
            (failures ??= []).Add(delay == TimeSpan.Zero && options.UnreadableRetryDelays.TryGetValue(index, out var text)
                ? $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.RetryDelays)}[{index.ToString(CultureInfo.InvariantCulture)}] " +
                  $"is '{text}', which is not a TimeSpan (for example 00:05:00). Values must be greater than zero."
                : $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.RetryDelays)} " +
                  $"values must be greater than zero. Actual value: {delay}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
