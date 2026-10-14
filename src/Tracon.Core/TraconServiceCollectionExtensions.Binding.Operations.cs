using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>Binds scheduling settings from configuration.</summary>
    private static void BindScheduling(IConfigurationSection section, TraconSchedulingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconSchedulingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(TraconSchedulingOptions.RunWorker), out var runWorker))
        {
            options.RunWorker = runWorker;
        }

        if (int.TryParse(
                section[nameof(TraconSchedulingOptions.MaxConcurrentJobs)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrentJobs))
        {
            options.MaxConcurrentJobs = maxConcurrentJobs;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconSchedulingOptions.PollInterval)],
                CultureInfo.InvariantCulture,
                out var pollInterval))
        {
            options.PollInterval = pollInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconSchedulingOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (int.TryParse(
                section[nameof(TraconSchedulingOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }

        if (int.TryParse(
                section[nameof(TraconSchedulingOptions.MaxItemsPerJob)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxItemsPerJob))
        {
            options.MaxItemsPerJob = maxItemsPerJob;
        }

        BindSchedulingCollections(section, options);
    }

    /// <summary>Binds the five collection keys of the <c>Tracon:Scheduling</c> section.</summary>
    /// <remarks>
    /// <para>
    /// These were listed as configuration keys and once bound nowhere, so a
    /// worker's lane budget in <c>appsettings.json</c> was silently ignored and
    /// the worker leased every lane.
    /// </para>
    /// <para>
    /// A list that is present REPLACES the default, the same rule as
    /// <see cref="BindList"/>. A dictionary entry keeps its key as written:
    /// lanes and handler keys are matched ordinally. A per-lane budget that is
    /// not a number is bound as zero rather than skipped, so
    /// <see cref="TraconSchedulingOptionsValidator"/> names it at startup — a
    /// skipped budget is exactly the silent failure this method closes.
    /// </para>
    /// </remarks>
    private static void BindSchedulingCollections(IConfigurationSection section, TraconSchedulingOptions options)
    {
        var lanes = section.GetSection(nameof(TraconSchedulingOptions.Lanes));

        if (lanes.Exists())
        {
            var values = lanes.GetChildren()
                .Select(static child => child.Value)
                .Where(static value => value is { Length: > 0 })
                .Select(static value => value!)
                .ToArray();

            if (values.Length > 0)
            {
                options.Lanes = values;
            }
        }

        foreach (var child in section.GetSection(nameof(TraconSchedulingOptions.MaxConcurrentJobsPerLane)).GetChildren())
        {
            options.MaxConcurrentJobsPerLane[child.Key] = int.TryParse(
                child.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var max)
                ? max
                : 0;
        }

        foreach (var child in section.GetSection(nameof(TraconSchedulingOptions.LaneByHandlerKey)).GetChildren())
        {
            // An empty lane is bound as written, not skipped: the validator
            // names it at startup instead of the mapping silently vanishing.
            options.LaneByHandlerKey[child.Key] = child.Value ?? string.Empty;
        }

        BindList(
            section.GetSection(nameof(TraconSchedulingOptions.HttpSchedulableHandlerKeys)),
            options.HttpSchedulableHandlerKeys);

        // F-323: the F-385 rule. A duration that does not parse is bound as zero,
        // not skipped, so the validator names it at startup; a skipped entry would
        // leave that key's jobs without the limit the operator wrote.
        foreach (var child in section.GetSection(nameof(TraconSchedulingOptions.MaxDurationByHandlerKey)).GetChildren())
        {
            options.MaxDurationByHandlerKey[child.Key] = TimeSpan.TryParse(
                child.Value,
                CultureInfo.InvariantCulture,
                out var maxDuration)
                ? maxDuration
                : TimeSpan.Zero;
        }
    }

    /// <summary>Binds single-executor selection settings from configuration.</summary>
    private static void BindSingletonExecution(IConfigurationSection section, SingletonExecutionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(SingletonExecutionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(SingletonExecutionOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (section[nameof(SingletonExecutionOptions.OwnerId)] is { Length: > 0 } ownerId)
        {
            options.OwnerId = ownerId;
        }
    }

    /// <summary>Binds the <c>Tracon:Quotas</c> section.</summary>
    private static void BindQuotas(IConfigurationSection section, TraconQuotaOptions options)
    {
        // Every sub-section is responsible for ITS OWN existence check: an
        // early return silently swallows later sections (see docs/hafiza/cekirdek-calistirma.md).
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconQuotaOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(TraconQuotaOptions.TimeZone)] is { Length: > 0 } timeZone)
        {
            options.TimeZone = timeZone;
        }

        if (TryReadBool(section, nameof(TraconQuotaOptions.AllowOnStoreFailure), out var allowOnFailure))
        {
            options.AllowOnStoreFailure = allowOnFailure;
        }

        if (TryReadBool(section, nameof(TraconQuotaOptions.PublishThresholdToRunStream), out var publishToRunStream))
        {
            options.PublishThresholdToRunStream = publishToRunStream;
        }

        // F-247, the F-385 rule: a list that is present REPLACES the default, and
        // an entry that is not a number is bound as zero rather than skipped, so
        // TraconQuotaOptionsValidator names it at startup. Skipping it (and a zero
        // or negative entry with it) started the application with a shorter list
        // than the operator wrote. The text is kept by index so the message quotes
        // what was written, not the zero that stands in for it.
        var thresholds = section.GetSection(nameof(TraconQuotaOptions.ThresholdPercents)).GetChildren().ToList();

        if (thresholds.Count > 0)
        {
            options.ThresholdPercents.Clear();

            foreach (var child in thresholds)
            {
                if (int.TryParse(child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                {
                    options.ThresholdPercents.Add(percent);
                }
                else
                {
                    options.UnreadableThresholdPercents[options.ThresholdPercents.Count] = child.Value ?? string.Empty;
                    options.ThresholdPercents.Add(0);
                }
            }
        }
    }

    /// <summary>Binds the <c>Tracon:RateLimit</c> section.</summary>
    private static void BindRateLimit(IConfigurationSection section, TraconRateLimitOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconRateLimitOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconRateLimitOptions.PermitLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var permitLimit))
        {
            options.PermitLimit = permitLimit;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconRateLimitOptions.Window)],
                CultureInfo.InvariantCulture,
                out var window))
        {
            options.Window = window;
        }

        if (int.TryParse(
                section[nameof(TraconRateLimitOptions.QueueLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var queueLimit))
        {
            options.QueueLimit = queueLimit;
        }

        if (Enum.TryParse<RateLimitPartitionKind>(
                section[nameof(TraconRateLimitOptions.Partition)],
                ignoreCase: true,
                out var partition))
        {
            options.Partition = partition;
        }
    }

    /// <summary>Binds the <c>Tracon:Idempotency</c> section.</summary>
    private static void BindIdempotency(IConfigurationSection section, TraconIdempotencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconIdempotencyOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconIdempotencyOptions.MaxKeyLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxKeyLength))
        {
            options.MaxKeyLength = maxKeyLength;
        }
    }

    /// <summary>Binds the <c>Tracon:AsyncRun</c> section.</summary>
    private static void BindAsyncRun(IConfigurationSection section, TraconAsyncRunOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconAsyncRunOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconAsyncRunOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }
    }

    /// <summary>Binds the <c>Tracon:RunReconciliation</c> section.</summary>
    private static void BindRunReconciliation(IConfigurationSection section, RunReconciliationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(RunReconciliationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(RunReconciliationOptions.HeartbeatInterval)],
                CultureInfo.InvariantCulture,
                out var heartbeatInterval))
        {
            options.HeartbeatInterval = heartbeatInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(RunReconciliationOptions.OrphanThreshold)],
                CultureInfo.InvariantCulture,
                out var orphanThreshold))
        {
            options.OrphanThreshold = orphanThreshold;
        }

        if (TimeSpan.TryParse(
                section[nameof(RunReconciliationOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }

        if (int.TryParse(
                section[nameof(RunReconciliationOptions.MaxRunsPerScan)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRunsPerScan))
        {
            options.MaxRunsPerScan = maxRunsPerScan;
        }
    }

    /// <summary>Binds the <c>Tracon:RunContinuation</c> section.</summary>
    private static void BindRunContinuation(IConfigurationSection section, TraconRunContinuationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconRunContinuationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconRunContinuationOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }
    }

    /// <summary>Binds the <c>Tracon:Drain</c> section.</summary>
    private static void BindDrain(IConfigurationSection section, TraconDrainOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconDrainOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconDrainOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }
    }

    /// <summary>Binds the <c>Tracon:StructuredResponse</c> section.</summary>
    private static void BindStructuredResponse(IConfigurationSection section, TraconStructuredResponseOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconStructuredResponseOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconStructuredResponseOptions.MaxRepairAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRepairAttempts))
        {
            options.MaxRepairAttempts = maxRepairAttempts;
        }
    }

    /// <summary>Binds the <c>Tracon:Canary</c> section.</summary>
    private static void BindCanary(IConfigurationSection section, CanaryOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(CanaryOptions.AutoRollbackEnabled), out var autoRollbackEnabled))
        {
            options.AutoRollbackEnabled = autoRollbackEnabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(CanaryOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }
    }
}
