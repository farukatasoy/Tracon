using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismOptions"/> during application startup.
/// </summary>
/// <remarks>
/// Validation is written manually; <c>ValidateDataAnnotations()</c> is not used.
/// DataAnnotations validation uses reflection and produces <c>IL2026</c>.
/// <c>AgentPrism.Core</c> must remain AOT-compatible.
/// </remarks>
public sealed class AgentPrismOptionsValidator : IValidateOptions<AgentPrismOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AgentPrismOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (string.IsNullOrWhiteSpace(options.DefaultTenantId))
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.DefaultTenantId)} cannot be empty.");
        }

        var recording = options.RunRecording;

        if (recording is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.RunRecording)} cannot be empty.");
        }
        else if (recording.MaxPayloadLength is < 0 or > 1_048_576)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismRunRecordingOptions)}.{nameof(AgentPrismRunRecordingOptions.MaxPayloadLength)} " +
                $"must be between 0 and 1048576. Actual value: {recording.MaxPayloadLength}.");
        }

        var circuitBreaker = options.CircuitBreaker;

        if (circuitBreaker is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.CircuitBreaker)} cannot be empty.");
        }
        else
        {
            if (circuitBreaker.FailureThreshold < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismCircuitBreakerOptions)}.{nameof(AgentPrismCircuitBreakerOptions.FailureThreshold)} " +
                    $"must be at least 1. Actual value: {circuitBreaker.FailureThreshold}.");
            }

            if (circuitBreaker.BreakDuration <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismCircuitBreakerOptions)}.{nameof(AgentPrismCircuitBreakerOptions.BreakDuration)} " +
                    $"must be greater than zero. Actual value: {circuitBreaker.BreakDuration}.");
            }
        }

        var health = options.Health;

        if (health is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.Health)} cannot be empty.");
        }
        else
        {
            if (health.CacheTtl <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismHealthOptions)}.{nameof(AgentPrismHealthOptions.CacheTtl)} " +
                    $"must be greater than zero. Actual value: {health.CacheTtl}.");
            }

            if (health.BackgroundInterval is { } interval && interval <= TimeSpan.Zero)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismHealthOptions)}.{nameof(AgentPrismHealthOptions.BackgroundInterval)} " +
                    $"must be greater than zero when supplied. Actual value: {interval}.");
            }
        }

        var skills = options.Skills;

        if (skills is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.Skills)} cannot be empty.");
        }
        else
        {
            if (skills.MaxSkillsPerAgent < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSkillOptions)}.{nameof(AgentPrismSkillOptions.MaxSkillsPerAgent)} " +
                    $"must be at least 1. Actual value: {skills.MaxSkillsPerAgent}.");
            }

            if (skills.MaxInstructionsLength < 1 || skills.MaxResourceContentLength < 1 || skills.MaxResourcesPerSkill < 1)
            {
                (failures ??= []).Add(
                $"{nameof(AgentPrismSkillOptions)} limits must be greater than zero.");
            }

            ValidateScripts(skills.Scripts, ref failures);
        }

        var validation = options.Validation;

        if (validation is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.Validation)} cannot be empty.");
        }
        else if (validation.McpTimeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismValidationOptions)}.{nameof(AgentPrismValidationOptions.McpTimeout)} " +
                $"must be greater than zero. Actual value: {validation.McpTimeout}.");
        }

        ValidatePricing(options.Pricing, ref failures);

        var tools = options.Tools;

        if (tools is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismOptions)}.{nameof(AgentPrismOptions.Tools)} cannot be empty.");
        }
        else if (tools.DefaultTimeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismToolOptions)}.{nameof(AgentPrismToolOptions.DefaultTimeout)} " +
                $"must be greater than zero. Actual value: {tools.DefaultTimeout}.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Validates script-execution options.</summary>
    /// <remarks>
    /// This validation is a security gate. Script execution enabled with
    /// incomplete configuration must fail during <strong>startup</strong>, not at run time.
    /// </remarks>
    private static void ValidateScripts(AgentPrismSkillScriptOptions? scripts, ref List<string>? failures)
    {
        if (scripts is null)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillOptions)}.{nameof(AgentPrismSkillOptions.Scripts)} cannot be empty.");
            return;
        }

        if (!scripts.Enabled)
        {
            return;
        }

        if (!scripts.PlatformIsolationAcknowledged)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.Enabled)} requires " +
                $"{nameof(AgentPrismSkillScriptOptions.PlatformIsolationAcknowledged)} to be true. " +
                "AgentPrism does not provide operating-system isolation. Network access, file-system isolation, " +
                "CPU and memory quotas, and privilege dropping are the host environment's responsibility. Enable " +
                "script execution only in a container, under an unprivileged user, and with restricted network access.");
        }

        if (scripts.Timeout <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.Timeout)} " +
                $"must be greater than zero. Actual value: {scripts.Timeout}.");
        }

        if (scripts.MaxOutputBytes < 1 || scripts.MaxArgumentBytes < 1 || scripts.MaxScriptContentLength < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)} size limits must be greater than zero.");
        }

        if (scripts.MaxScriptsPerSkill < 1 || scripts.MaxConcurrentPerTenant < 1 || scripts.MaxConcurrentTotal < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)} count limits must be at least 1.");
        }

        if (scripts.MaxConcurrentPerTenant > scripts.MaxConcurrentTotal)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.MaxConcurrentPerTenant)} " +
                $"({scripts.MaxConcurrentPerTenant}) cannot exceed the total limit ({scripts.MaxConcurrentTotal}).");
        }

        if (scripts.SearchDepth < 1)
        {
            (failures ??= []).Add(
                $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.SearchDepth)} " +
                $"must be at least 1. Actual value: {scripts.SearchDepth}.");
        }

        foreach (var pair in scripts.Interpreters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSkillScriptOptions)}.{nameof(AgentPrismSkillScriptOptions.Interpreters)} " +
                    "contains an empty extension or interpreter path.");
                break;
            }
        }
    }

    /// <summary>
    /// Validates that configuration price overrides are not negative. A negative
    /// price would silently corrupt cost reporting.
    /// </summary>
    private static void ValidatePricing(AgentPrismPricingOptions? pricing, ref List<string>? failures)
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
                        $"{nameof(AgentPrismPricingOptions)}: price for '{providerName}:{modelName}' cannot be negative.");
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
                        $"{nameof(AgentPrismPricingOptions)}: '{providerName}:{modelName}' ne 'Input' ne 'Output' " +
                        "contains neither value. Check the key name.");
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
                        $"{nameof(AgentPrismPricingOptions)}: price for 'Voice:{providerName}:{modelName}' cannot be negative.");
                }

                // 🚨 Same rationale: see the Providers validation above (MT-CORE-065).
                if (price.PerMillionCharacters is null && price.PerMinute is null)
                {
                    (failures ??= []).Add(
                        $"{nameof(AgentPrismPricingOptions)}: 'Voice:{providerName}:{modelName}' ne " +
                        $"'{nameof(VoicePriceOverride.PerMillionCharacters)}' ne '{nameof(VoicePriceOverride.PerMinute)}' " +
                        "contains neither value. Check the key name.");
                }
            }
        }
    }
}
