using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="RunReconciliationOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>. Rationale: <c>docs/KARARLAR.md</c>, decision K-006.
/// </remarks>
public sealed class RunReconciliationOptionsValidator : IValidateOptions<RunReconciliationOptions>
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

        if (options.MaxRunsPerScan <= 0)
        {
            return ValidateOptionsResult.Fail(
                $"{nameof(RunReconciliationOptions)}.{nameof(RunReconciliationOptions.MaxRunsPerScan)} " +
                $"must be greater than zero. Actual value: {options.MaxRunsPerScan}.");
        }

        return ValidateOptionsResult.Success;
    }
}
