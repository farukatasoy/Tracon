using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="CanaryOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class CanaryOptionsValidator : IValidateOptions<CanaryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, CanaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The evaluation loop scans on a PeriodicTimer; a period it refuses
        // stops the host after startup (F-278). With automatic rollback off no
        // timer exists, so the value is not checked.
        if (options.AutoRollbackEnabled && !TimerPeriod.IsValid(options.ScanInterval))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(CanaryOptions)}.{nameof(CanaryOptions.ScanInterval)}",
                options.ScanInterval,
                $" when {nameof(CanaryOptions.AutoRollbackEnabled)} is on"));
        }

        return ValidateOptionsResult.Success;
    }
}
