using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconOptions"/> during application startup.
/// </summary>
/// <remarks>
/// Validation is written manually; <c>ValidateDataAnnotations()</c> is not used.
/// DataAnnotations validation uses reflection and produces <c>IL2026</c>.
/// <c>Tracon.Core</c> must remain AOT-compatible.
/// </remarks>
internal sealed class TraconOptionsValidator : IValidateOptions<TraconOptions>
{
    /// <summary>
    /// The longest period a <see cref="PeriodicTimer"/> accepts, in whole
    /// milliseconds (about 49.7 days).
    /// </summary>
    private const long MaxGaugeRefreshMilliseconds = uint.MaxValue - 1L;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.DefaultTenantId))
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.DefaultTenantId)} cannot be empty.");
        }
        else if (!string.Equals(
            options.DefaultTenantId,
            AmbientTenantScope.Normalize(options.DefaultTenantId),
            StringComparison.Ordinal))
        {
            // Rejected rather than folded silently: an operator who wrote
            // "Acme" here and then reads "acme" in the audit trail has no way
            // to find out where the change happened. Stopping at startup is
            // cheap and readable. The default value is already canonical, so
            // no existing setup breaks by accident.
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.DefaultTenantId)} must be canonical " +
                $"(invariant lower-case): '{options.DefaultTenantId}' should be " +
                $"'{AmbientTenantScope.Normalize(options.DefaultTenantId)}'. The tenant identifier is " +
                "matched case-insensitively by normalizing the value, so a non-canonical default would " +
                "never match the rows written for it.");
        }

        if (options.MaxParameterValueLength < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.MaxParameterValueLength)} must be at least 1.");
        }

        var recording = options.RunRecording;

        if (recording is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.RunRecording)} cannot be empty.");
        }
        else if (recording.MaxPayloadLength is < 0 or > 1_048_576)
        {
            (failures ??= []).Add(
                $"{nameof(TraconRunRecordingOptions)}.{nameof(TraconRunRecordingOptions.MaxPayloadLength)} " +
                $"must be between 0 and 1048576. Actual value: {recording.MaxPayloadLength}.");
        }

        var circuitBreaker = options.CircuitBreaker;

        if (circuitBreaker is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.CircuitBreaker)} cannot be empty.");
        }
        else
        {
            if (circuitBreaker.FailureThreshold < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconCircuitBreakerOptions)}.{nameof(TraconCircuitBreakerOptions.FailureThreshold)} " +
                    $"must be at least 1. Actual value: {circuitBreaker.FailureThreshold}.");
            }

            if (circuitBreaker.BreakDuration <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconCircuitBreakerOptions)}.{nameof(TraconCircuitBreakerOptions.BreakDuration)} " +
                    $"must be greater than zero. Actual value: {circuitBreaker.BreakDuration}.");
            }
        }

        var health = options.Health;

        if (health is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.Health)} cannot be empty.");
        }
        else
        {
            if (health.CacheTtl <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconHealthOptions)}.{nameof(TraconHealthOptions.CacheTtl)} " +
                    $"must be greater than zero. Actual value: {health.CacheTtl}.");
            }

            if (health.BackgroundInterval is { } interval && interval <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconHealthOptions)}.{nameof(TraconHealthOptions.BackgroundInterval)} " +
                    $"must be greater than zero when supplied. Actual value: {interval}.");
            }
        }

        ValidateGaugeRefresh(options.Observability, ref failures);

        var skills = options.Skills;

        if (skills is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.Skills)} cannot be empty.");
        }
        else
        {
            if (skills.MaxSkillsPerAgent < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSkillOptions)}.{nameof(TraconSkillOptions.MaxSkillsPerAgent)} " +
                    $"must be at least 1. Actual value: {skills.MaxSkillsPerAgent}.");
            }

            if (skills.MaxInstructionsLength < 1 || skills.MaxResourceContentLength < 1 || skills.MaxResourcesPerSkill < 1)
            {
                (failures ??= []).Add(
                $"{nameof(TraconSkillOptions)} limits must be greater than zero.");
            }

            ValidateScripts(skills.Scripts, ref failures);
        }

        var validation = options.Validation;

        if (validation is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.Validation)} cannot be empty.");
        }
        else if (validation.McpTimeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconValidationOptions)}.{nameof(TraconValidationOptions.McpTimeout)} " +
                $"must be greater than zero. Actual value: {validation.McpTimeout}.");
        }

        ValidatePricing(options.Pricing, ref failures);

        var tools = options.Tools;

        if (tools is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.Tools)} cannot be empty.");
        }
        else
        {
            if (tools.DefaultTimeout <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconToolOptions)}.{nameof(TraconToolOptions.DefaultTimeout)} " +
                    $"must be greater than zero. Actual value: {tools.DefaultTimeout}.");
            }

            if (tools.DefaultMaxOutputBytes is { } maxOutputBytes && maxOutputBytes < TruncatingAIFunction.MinimumEnvelopeBytes)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconToolOptions)}.{nameof(TraconToolOptions.DefaultMaxOutputBytes)} " +
                    $"must be at least {TruncatingAIFunction.MinimumEnvelopeBytes} when supplied — below that, " +
                    $"no tool result could ever fit inside the envelope. Actual value: {maxOutputBytes}.");
            }
        }

        var agentGraph = options.AgentGraph;

        if (agentGraph.ChildDeadline <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconAgentGraphOptions)}.{nameof(TraconAgentGraphOptions.ChildDeadline)} " +
                $"must be greater than zero. Actual value: {agentGraph.ChildDeadline}.");
        }

        if (agentGraph.WaitTimeout <= agentGraph.ChildDeadline)
        {
            (failures ??= []).Add(
                $"{nameof(TraconAgentGraphOptions)}.{nameof(TraconAgentGraphOptions.WaitTimeout)} " +
                $"({agentGraph.WaitTimeout}) must be greater than {nameof(TraconAgentGraphOptions.ChildDeadline)} " +
                $"({agentGraph.ChildDeadline}); otherwise the hard cutoff would fire before the cooperative one ever " +
                "gets a chance to take effect.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Validates the refresh interval of every database-backed gauge that is on.</summary>
    /// <remarks>
    /// An enabled gauge refreshes on a <see cref="PeriodicTimer"/>, and that timer
    /// accepts only a period from one millisecond to
    /// <see cref="MaxGaugeRefreshMilliseconds"/> milliseconds. Without this
    /// check the refresher would fail after startup and, under the default
    /// hosting behavior, stop the host. A gauge that is off creates no timer,
    /// so its interval is not checked.
    /// </remarks>
    private static void ValidateGaugeRefresh(TraconObservabilityOptions? observability, ref List<string>? failures)
    {
        if (observability is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconOptions)}.{nameof(TraconOptions.Observability)} cannot be empty.");
            return;
        }

        if (observability.EnableQuotaUsageGauge && !IsTimerPeriod(observability.QuotaUsageRefreshInterval))
        {
            (failures ??= []).Add(
                $"{nameof(TraconObservabilityOptions)}.{nameof(TraconObservabilityOptions.QuotaUsageRefreshInterval)} " +
                $"must be between 1 and {MaxGaugeRefreshMilliseconds} milliseconds when " +
                $"{nameof(TraconObservabilityOptions.EnableQuotaUsageGauge)} is on. " +
                $"Actual value: {observability.QuotaUsageRefreshInterval}.");
        }

        if (observability.EnableJobQueueDepthGauge && !IsTimerPeriod(observability.JobQueueDepthRefreshInterval))
        {
            (failures ??= []).Add(
                $"{nameof(TraconObservabilityOptions)}.{nameof(TraconObservabilityOptions.JobQueueDepthRefreshInterval)} " +
                $"must be between 1 and {MaxGaugeRefreshMilliseconds} milliseconds when " +
                $"{nameof(TraconObservabilityOptions.EnableJobQueueDepthGauge)} is on. " +
                $"Actual value: {observability.JobQueueDepthRefreshInterval}.");
        }
    }

    /// <summary>Reports whether a <see cref="PeriodicTimer"/> accepts the period.</summary>
    /// <param name="period">The period.</param>
    /// <returns><see langword="true"/> for a period from one millisecond to about 49.7 days.</returns>
    private static bool IsTimerPeriod(TimeSpan period)
        => period >= TimeSpan.FromMilliseconds(1)
           && period.TotalMilliseconds <= MaxGaugeRefreshMilliseconds;

    /// <summary>Validates script-execution options.</summary>
    /// <remarks>
    /// This validation is a security gate. Script execution enabled with
    /// incomplete configuration must fail during <strong>startup</strong>, not at run time.
    /// </remarks>
    private static void ValidateScripts(TraconSkillScriptOptions? scripts, ref List<string>? failures)
    {
        if (scripts is null)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillOptions)}.{nameof(TraconSkillOptions.Scripts)} cannot be empty.");
            return;
        }

        if (!scripts.Enabled)
        {
            return;
        }

        if (!scripts.PlatformIsolationAcknowledged)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillScriptOptions)}.{nameof(TraconSkillScriptOptions.Enabled)} requires " +
                $"{nameof(TraconSkillScriptOptions.PlatformIsolationAcknowledged)} to be true. " +
                "Tracon does not provide operating-system isolation. Network access, file-system isolation, " +
                "CPU and memory quotas, and privilege dropping are the host environment's responsibility. Enable " +
                "script execution only in a container, under an unprivileged user, and with restricted network access.");
        }

        if (scripts.Timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillScriptOptions)}.{nameof(TraconSkillScriptOptions.Timeout)} " +
                $"must be greater than zero. Actual value: {scripts.Timeout}.");
        }

        if (scripts.MaxOutputBytes < 1 || scripts.MaxArgumentBytes < 1 || scripts.MaxScriptContentLength < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillScriptOptions)} size limits must be greater than zero.");
        }

        if (scripts.MaxScriptsPerSkill < 1 || scripts.MaxConcurrentPerTenant < 1 || scripts.MaxConcurrentTotal < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillScriptOptions)} count limits must be at least 1.");
        }

        if (scripts.MaxConcurrentPerTenant > scripts.MaxConcurrentTotal)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillScriptOptions)}.{nameof(TraconSkillScriptOptions.MaxConcurrentPerTenant)} " +
                $"({scripts.MaxConcurrentPerTenant}) cannot exceed the total limit ({scripts.MaxConcurrentTotal}).");
        }

        if (scripts.SearchDepth < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSkillScriptOptions)}.{nameof(TraconSkillScriptOptions.SearchDepth)} " +
                $"must be at least 1. Actual value: {scripts.SearchDepth}.");
        }

        foreach (var pair in scripts.Interpreters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSkillScriptOptions)}.{nameof(TraconSkillScriptOptions.Interpreters)} " +
                    "contains an empty extension or interpreter path.");
                break;
            }
        }
    }

    /// <summary>
    /// Validates that configuration price overrides are not negative. A negative
    /// price would silently corrupt cost reporting.
    /// </summary>
    private static void ValidatePricing(TraconPricingOptions? pricing, ref List<string>? failures)
    {
        if (pricing is null)
        {
            return;
        }

        foreach (var (providerName, models) in pricing.Providers)
        {
            foreach (var (modelName, price) in models)
            {
                if (price.InputCostPerMillionTokens is < 0
                    || price.OutputCostPerMillionTokens is < 0
                    || price.CachedInputCostPerMillionTokens is < 0)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: price for '{providerName}:{modelName}' cannot be negative.");
                }

                // 🚨 K-034 (MT-CORE-065): BindPricing now adds a record when both
                // values are empty. This explicitly rejects a price written with
                // a key other than Input or Output, such as the C# property name
                // InputCostPerMillionTokens, rather than silently discarding it.
                // 🚨 CachedInput is DELIBERATELY absent from this condition: a model
                // priced with only a cache rate has no base price at all, which is
                // the same silent-typo fault this check exists to catch.
                if (price.InputCostPerMillionTokens is null && price.OutputCostPerMillionTokens is null)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: '{providerName}:{modelName}' contains neither " +
                        "'Input' nor 'Output'. Check the key name.");
                }
            }
        }

        foreach (var (providerName, models) in pricing.Voice)
        {
            foreach (var (modelName, price) in models)
            {
                if (price.PerMillionCharacters is < 0 || price.PerMinute is < 0)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: price for 'Voice:{providerName}:{modelName}' cannot be negative.");
                }

                // 🚨 Same rationale: see the Providers validation above (MT-CORE-065).
                if (price.PerMillionCharacters is null && price.PerMinute is null)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: 'Voice:{providerName}:{modelName}' contains neither " +
                        $"'{nameof(VoicePriceOverride.PerMillionCharacters)}' nor " +
                        $"'{nameof(VoicePriceOverride.PerMinute)}'. Check the key name.");
                }
            }
        }

        foreach (var (providerName, models) in pricing.Images)
        {
            foreach (var (modelName, price) in models)
            {
                if (price.PerImage is < 0 || price.OutputCostPerMillionTokens is < 0)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: price for 'Images:{providerName}:{modelName}' cannot be negative.");
                }

                if (price.PerImage is not null && price.OutputCostPerMillionTokens is not null)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: 'Images:{providerName}:{modelName}' cannot contain both " +
                        $"'{nameof(ImagePriceOverride.PerImage)}' and " +
                        $"'{nameof(ImagePriceOverride.OutputCostPerMillionTokens)}'.");
                }

                if (price.PerImage is null && price.OutputCostPerMillionTokens is null)
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconPricingOptions)}: 'Images:{providerName}:{modelName}' contains neither " +
                        $"'{nameof(ImagePriceOverride.PerImage)}' nor " +
                        $"'{nameof(ImagePriceOverride.OutputCostPerMillionTokens)}'. Check the key name.");
                }

                foreach (var (size, multiplier) in price.SizeMultipliers)
                {
                    if (string.IsNullOrWhiteSpace(size) || multiplier < 0)
                    {
                        (failures ??= []).Add(
                            $"{nameof(TraconPricingOptions)}: image size multiplier for " +
                            $"'Images:{providerName}:{modelName}' must have a name and cannot be negative.");
                        break;
                    }
                }
            }
        }
    }
}
