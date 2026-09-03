using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates <see cref="AgentPrismSchedulingOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </remarks>
internal sealed class AgentPrismSchedulingOptionsValidator : IValidateOptions<AgentPrismSchedulingOptions>
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

        if (options.Lanes is { } lanes)
        {
            foreach (var lane in lanes)
            {
                if (!JobLanes.IsValidName(lane))
                {
                    (failures ??= []).Add(
                        $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.Lanes)} " +
                        $"contains '{lane}', which is not a valid lane name.");
                }
            }
        }

        foreach (var (lane, max) in options.MaxConcurrentJobsPerLane)
        {
            if (!JobLanes.IsValidName(lane))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxConcurrentJobsPerLane)} " +
                    $"contains '{lane}', which is not a valid lane name.");
            }

            if (max < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.MaxConcurrentJobsPerLane)}" +
                    $"['{lane}'] must be at least 1. Actual value: {max}.");
            }
        }

        foreach (var (handlerKey, lane) in options.LaneByHandlerKey)
        {
            if (!JobHandlerKeys.IsValidKey(handlerKey))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.LaneByHandlerKey)} " +
                    $"contains the key '{handlerKey}', which is not a valid job handler key.");
            }

            if (!JobLanes.IsValidName(lane))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.LaneByHandlerKey)} " +
                    $"contains '{lane}', which is not a valid lane name.");
            }
        }

        foreach (var handlerKey in options.HttpSchedulableHandlerKeys)
        {
            if (!JobHandlerKeys.IsValidKey(handlerKey))
            {
                (failures ??= []).Add(
                    $"{nameof(AgentPrismSchedulingOptions)}.{nameof(AgentPrismSchedulingOptions.HttpSchedulableHandlerKeys)} " +
                    $"contains '{handlerKey}', which is not a valid job handler key.");
            }
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
