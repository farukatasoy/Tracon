using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="TraconSchedulingOptions"/> when the application starts.
/// </summary>
/// <remarks>
/// <para>
/// This validation is handwritten. <c>ValidateDataAnnotations()</c> uses reflection
/// and produces <c>IL2026</c>.
/// </para>
/// <para>
/// The handler registry is optional because most checks need none: it is there for the one that asks whether a
/// handler is registered for a key. Without it that one check is skipped, as it is in a unit test of the other rules.
/// </para>
/// </remarks>
/// <param name="registry">The registered handlers; <see langword="null"/> skips the check that a named key has one.</param>
internal sealed class TraconSchedulingOptionsValidator(JobHandlerRegistry? registry = null) : IValidateOptions<TraconSchedulingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TraconSchedulingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string>? failures = null;

        if (options.MaxConcurrentJobs < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxConcurrentJobs)} " +
                $"must be at least 1. Actual value: {options.MaxConcurrentJobs}.");
        }

        if (options.PollInterval <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.PollInterval)} " +
                $"must be greater than zero. Actual value: {options.PollInterval}.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.LeaseDuration)} " +
                $"must be greater than zero. Actual value: {options.LeaseDuration}.");
        }

        // The worker polls on a PeriodicTimer and renews each job's lease on a
        // second one at a third of the lease; a period the timer refuses stops the
        // host (poll) or silently loses the lease (renewal) after startup
        // A process that runs no worker creates neither timer.
        if (options.Enabled && options.RunWorker)
        {
            if (options.PollInterval > TimeSpan.Zero && !TimerPeriod.IsValid(options.PollInterval))
            {
                (failures ??= []).Add(TimerPeriod.Failure(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.PollInterval)}",
                    options.PollInterval,
                    " while the worker runs"));
            }

            if (options.LeaseDuration > TimeSpan.Zero && !TimerPeriod.IsValid(options.LeaseDuration / 3))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.LeaseDuration)} " +
                    $"must be between 3 and {TimerPeriod.MaxMilliseconds * 3} milliseconds while the worker " +
                    $"runs: the lease is renewed at a third of its duration on a timer. Actual value: {options.LeaseDuration}.");
            }
        }

        if (options.MaxAttempts < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxAttempts)} " +
                $"must be at least 1. Actual value: {options.MaxAttempts}.");
        }

        if (options.MaxItemsPerJob < 1)
        {
            (failures ??= []).Add(
                $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxItemsPerJob)} " +
                $"must be at least 1. Actual value: {options.MaxItemsPerJob}.");
        }

        if (options.Lanes is { } lanes)
        {
            foreach (var lane in lanes)
            {
                if (!JobLanes.IsValidName(lane))
                {
                    (failures ??= []).Add(
                        $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.Lanes)} " +
                        $"contains '{lane}', which is not a valid lane name.");
                }
            }
        }

        foreach (var (lane, max) in options.MaxConcurrentJobsPerLane)
        {
            if (!JobLanes.IsValidName(lane))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxConcurrentJobsPerLane)} " +
                    $"contains '{lane}', which is not a valid lane name.");
            }

            if (max < 1)
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxConcurrentJobsPerLane)}" +
                    $"['{lane}'] must be at least 1. Actual value: {max}.");
            }
        }

        foreach (var (handlerKey, lane) in options.LaneByHandlerKey)
        {
            if (!JobHandlerKeys.IsValidKey(handlerKey))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.LaneByHandlerKey)} " +
                    $"contains the key '{handlerKey}', which is not a valid job handler key.");
            }

            if (!JobLanes.IsValidName(lane))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.LaneByHandlerKey)} " +
                    $"contains '{lane}', which is not a valid lane name.");
            }
        }

        // The worker arms each value as an attempt's deadline, and a
        // timer refuses a value outside its range where it is armed - while a job is
        // already running. Checked whether or not this process runs the worker: the
        // map describes jobs, and another process's worker reads the same section.
        foreach (var (handlerKey, maxDuration) in options.MaxDurationByHandlerKey)
        {
            if (!JobHandlerKeys.IsValidKey(handlerKey))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxDurationByHandlerKey)} " +
                    $"contains the key '{handlerKey}', which is not a valid job handler key.");
            }

            if (!TimerPeriod.IsValid(maxDuration))
            {
                (failures ??= []).Add(TimerPeriod.Failure(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxDurationByHandlerKey)}['{handlerKey}']",
                    maxDuration));
            }
        }

        foreach (var handlerKey in options.HttpSchedulableHandlerKeys)
        {
            if (!JobHandlerKeys.IsValidKey(handlerKey))
            {
                (failures ??= []).Add(
                    $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.HttpSchedulableHandlerKeys)} " +
                    $"contains '{handlerKey}', which is not a valid job handler key.");
            }
        }

        ValidateTenantCap(options, ref failures);

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Checks the per-tenant ceiling: a ceiling and the keys it covers come together, every key belongs to a registered
    /// handler, and the ceiling is at least one.
    /// </summary>
    /// <remarks>
    /// A key nobody registered caps nothing and says nothing, and a ceiling over no keys (or keys with no ceiling) is a
    /// setting that does nothing. Each is a configuration mistake that would otherwise look like a working limit, so
    /// each stops the host.
    /// </remarks>
    private void ValidateTenantCap(TraconSchedulingOptions options, ref List<string>? failures)
    {
        const string KeysName = $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.TenantCapHandlerKeys)}";
        const string CeilingName = $"{nameof(TraconSchedulingOptions)}.{nameof(TraconSchedulingOptions.MaxLeasedPerTenant)}";

        if (options.MaxLeasedPerTenant is { } ceiling && ceiling < 1)
        {
            (failures ??= []).Add($"{CeilingName} must be at least 1. Actual value: {ceiling}.");
        }

        if (options.MaxLeasedPerTenant is not null && options.TenantCapHandlerKeys.Count == 0)
        {
            (failures ??= []).Add($"{CeilingName} is set but {KeysName} is empty, so the ceiling would apply to no job.");
        }

        if (options.MaxLeasedPerTenant is null && options.TenantCapHandlerKeys.Count > 0)
        {
            (failures ??= []).Add($"{KeysName} names handler keys but {CeilingName} is not set, so no ceiling would apply.");
        }

        foreach (var handlerKey in options.TenantCapHandlerKeys)
        {
            if (!JobHandlerKeys.IsValidKey(handlerKey))
            {
                (failures ??= []).Add($"{KeysName} contains '{handlerKey}', which is not a valid job handler key.");
            }
            else if (registry is not null && !registry.IsRegistered(handlerKey))
            {
                (failures ??= []).Add(
                    $"{KeysName} contains '{handlerKey}', but no job handler is registered for that key " +
                    "(AddJobHandler). A ceiling over a key nothing is registered for would cap no job.");
            }
        }
    }
}
