using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="RunReconciliationOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
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
                $"{nameof(RunReconciliationOptions.HeartbeatInterval)}'dan kucuk olamaz -- calisan bir is " +
                "olu ilan edilir. Gelen degerler: " +
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
