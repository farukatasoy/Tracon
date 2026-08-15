using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismSchedulingOptions"/> ayarlarini uygulama baslarken dogrular.
/// </summary>
/// <remarks>
/// Dogrulama elle yazilmistir; <c>ValidateDataAnnotations()</c> yansimaya dayanir ve
/// <c>IL2026</c> uretir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismSchedulingOptionsValidator : IValidateOptions<AgentPrismSchedulingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismSchedulingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.MaxConcurrentJobs < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxConcurrentJobs)} " +
                $"must be at least 1. Actual value: {options.MaxConcurrentJobs}.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.PollInterval)} " +
                $"must be greater than zero. Actual value: {options.PollInterval}.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.LeaseDuration)} " +
                $"must be greater than zero. Actual value: {options.LeaseDuration}.");
        }

        if (options.MaxAttempts < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxAttempts)} " +
                $"must be at least 1. Actual value: {options.MaxAttempts}.");
        }

        if (options.MaxItemsPerJob < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxItemsPerJob)} " +
                $"must be at least 1. Actual value: {options.MaxItemsPerJob}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
