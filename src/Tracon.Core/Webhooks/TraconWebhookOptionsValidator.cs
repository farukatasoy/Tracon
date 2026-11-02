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

        // Every delivery attempt arms this timeout; an out-of-range value threw
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

        // The delivery counts the headers it adds and stops when the count EQUALS the limit, so a negative
        // limit never stops it, while the endpoint compares with greater-than and refuses every subscription.
        if (options.MaxExtraHeaders < 0)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.MaxExtraHeaders)} " +
                $"must not be negative (0 allows no extra header). Actual value: {options.MaxExtraHeaders}.");
        }

        if (options.SignatureTolerance < TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.SignatureTolerance)} " +
                $"must not be negative. Actual value: {options.SignatureTolerance}.");
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

            // The binder leaves out an entry it cannot read and names it itself.
            (failures ??= []).Add(
                $"{nameof(TraconWebhookOptions)}.{nameof(TraconWebhookOptions.RetryDelays)} " +
                $"values must be greater than zero. Actual value: {delay}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
