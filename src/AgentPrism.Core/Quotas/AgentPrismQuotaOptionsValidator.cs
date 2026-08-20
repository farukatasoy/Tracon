using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismQuotaOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
public sealed class AgentPrismQuotaOptionsValidator : IValidateOptions<AgentPrismQuotaOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismQuotaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        // Validate the time zone at startup. ResolveTimeZone() falls back to UTC at
        // run time. An invalid name must not stop all traffic, but the error must not
        // be silent either, so report it before the application starts.
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
                    $"is not a valid time zone: '{options.TimeZone}'.");
            }
        }

        foreach (var percent in options.ThresholdPercents)
        {
            if (percent is <= 0 or > 100)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismQuotaOptions)}.{nameof(AgentPrismQuotaOptions.ThresholdPercents)} " +
                $"values must be between 1 and 100. Actual value: {percent}.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
