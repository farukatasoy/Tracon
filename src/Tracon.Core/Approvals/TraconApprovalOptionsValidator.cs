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

        // 🚨 Expiration is ON by default and scans on a PeriodicTimer. Without
        // this check `ScanInterval=00:00:00` let the host report "Application
        // started" and then stopped it (F-278). While expiration is off no
        // timer exists, so the value is not checked.
        if (options.ExpirationEnabled && !TimerPeriod.IsValid(options.ScanInterval))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(TraconApprovalOptions)}.{nameof(TraconApprovalOptions.ScanInterval)}",
                options.ScanInterval,
                $" when {nameof(TraconApprovalOptions.ExpirationEnabled)} is on"));
        }

        return ValidateOptionsResult.Success;
    }
}
