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
                $"en az 1 olmalidir. Gelen deger: {options.MaxConcurrentJobs}.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.PollInterval)} " +
                $"sifirdan buyuk olmalidir. Gelen deger: {options.PollInterval}.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.LeaseDuration)} " +
                $"sifirdan buyuk olmalidir. Gelen deger: {options.LeaseDuration}.");
        }

        if (options.MaxAttempts < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxAttempts)} " +
                $"en az 1 olmalidir. Gelen deger: {options.MaxAttempts}.");
        }

        if (options.MaxItemsPerJob < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxItemsPerJob)} " +
                $"en az 1 olmalidir. Gelen deger: {options.MaxItemsPerJob}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
