using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="RunReconciliationOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class RunReconciliationOptionsValidator : IValidateOptions<RunReconciliationOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, RunReconciliationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.HeartbeatInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.HeartbeatInterval)} " +
                $"must be greater than zero. Actual value: {options.HeartbeatInterval}.");
        }

        if (options.OrphanThreshold <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.OrphanThreshold)} " +
                $"must be greater than zero. Actual value: {options.OrphanThreshold}.");
        }

        if (options.OrphanThreshold < options.HeartbeatInterval)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.OrphanThreshold)} " +
                $"must not be less than {nameof(RunReconciliationOptions.HeartbeatInterval)}. A running job " +
                "would be declared orphaned. Actual values: " +
                $"{nameof(RunReconciliationOptions.OrphanThreshold)}={options.OrphanThreshold}, " +
                $"{nameof(RunReconciliationOptions.HeartbeatInterval)}={options.HeartbeatInterval}.");
        }

        if (options.ScanInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.ScanInterval)} " +
                $"must be greater than zero. Actual value: {options.ScanInterval}.");
        }

        // Both the heartbeat writer and the reconciliation scan run on a
        // PeriodicTimer; a period the timer refuses stops the host after
        // startup (F-278). While reconciliation is off neither timer exists.
        if (options.Enabled && !TimerPeriod.IsValid(options.HeartbeatInterval))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.HeartbeatInterval)}",
                options.HeartbeatInterval,
                $" when {nameof(RunReconciliationOptions.Enabled)} is on"));
        }

        if (options.Enabled && !TimerPeriod.IsValid(options.ScanInterval))
        {
            return ValidateOptionsResult.Fail(TimerPeriod.Failure(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.ScanInterval)}",
                options.ScanInterval,
                $" when {nameof(RunReconciliationOptions.Enabled)} is on"));
        }

        if (options.MaxRunsPerScan <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.MaxRunsPerScan)} " +
                $"must be greater than zero. Actual value: {options.MaxRunsPerScan}.");
        }

        return ValidateOptionsResult.Success;
    }
}
