using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconInboundTriggerOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// A prefix of blanks is a boundary no key passes: every trigger secret reference is refused, with a message about the key and not about the setting.
/// </para>
/// <para>
/// The three limits must be positive. A body limit below one refuses every request that has a body (<c>413</c>), a
/// tolerance of zero or less calls every request stale (<c>401</c>), and a rate below one was raised to one request per
/// minute where it is read: each looked like a setting that was applied and meant something else.
/// </para>
/// <para>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </para>
/// </remarks>
internal sealed class TraconInboundTriggerOptionsValidator : IValidateOptions<TraconInboundTriggerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconInboundTriggerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        OptionText.AddWhenOnlyWhiteSpace(options.AllowedConfigurationPrefix, nameof(TraconInboundTriggerOptions), nameof(TraconInboundTriggerOptions.AllowedConfigurationPrefix), ref failures);

        if (options.TimestampTolerance <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconInboundTriggerOptions)}.{nameof(TraconInboundTriggerOptions.TimestampTolerance)} must be greater than zero: " +
                $"a request is stale when its timestamp drifts further than this, so every request would be refused. Actual value: {options.TimestampTolerance}.");
        }

        if (options.MaxBodyBytes < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconInboundTriggerOptions)}.{nameof(TraconInboundTriggerOptions.MaxBodyBytes)} must be at least 1: " +
                $"every request with a body would be refused as too large. Actual value: {options.MaxBodyBytes}.");
        }

        if (options.MaxRequestsPerMinute < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconInboundTriggerOptions)}.{nameof(TraconInboundTriggerOptions.MaxRequestsPerMinute)} must be at least 1. " +
                $"Actual value: {options.MaxRequestsPerMinute}.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
