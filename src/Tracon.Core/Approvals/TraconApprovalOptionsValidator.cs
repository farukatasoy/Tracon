using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconApprovalOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class TraconApprovalOptionsValidator : IValidateOptions<TraconApprovalOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconApprovalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        // 🚨 Expiration is ON by default and scans on a PeriodicTimer. Without
        // this check `ScanInterval=00:00:00` let the host report "Application
        // started" and then stopped it. While expiration is off no
        // timer exists, so the value is not checked.
        if (options.ExpirationEnabled && !TimerPeriod.IsValid(options.ScanInterval))
        {
            (failures ??= []).Add(TimerPeriod.Failure(
                $"{nameof(TraconApprovalOptions)}.{nameof(TraconApprovalOptions.ScanInterval)}",
                options.ScanInterval,
                $" when {nameof(TraconApprovalOptions.ExpirationEnabled)} is on"));
        }

        // A scan that may close no request closes none, and no approval ever expires.
        if (options.ExpirationEnabled && options.MaxPerScan < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconApprovalOptions)}.{nameof(TraconApprovalOptions.MaxPerScan)} " +
                $"must be at least 1 while {nameof(TraconApprovalOptions.ExpirationEnabled)} is on. Actual value: {options.MaxPerScan}.");
        }

        // The expiry of a new request is the time it is made plus this duration, so zero or less expires it
        // before anyone can answer.
        if (options.DefaultExpiration <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconApprovalOptions)}.{nameof(TraconApprovalOptions.DefaultExpiration)} " +
                $"must be greater than zero. Actual value: {options.DefaultExpiration}.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
