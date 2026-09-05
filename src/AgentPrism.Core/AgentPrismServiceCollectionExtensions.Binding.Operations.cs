using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace AgentPrism;

public static partial class AgentPrismServiceCollectionExtensions
{
    /// <summary>Binds scheduling settings from configuration.</summary>
    private static void BindScheduling(IConfigurationSection section, AgentPrismSchedulingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismSchedulingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismSchedulingOptions.RunWorker), out var runWorker))
        {
            options.RunWorker = runWorker;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxConcurrentJobs)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrentJobs))
        {
            options.MaxConcurrentJobs = maxConcurrentJobs;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismSchedulingOptions.PollInterval)],
                CultureInfo.InvariantCulture,
                out var pollInterval))
        {
            options.PollInterval = pollInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismSchedulingOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxItemsPerJob)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxItemsPerJob))
        {
            options.MaxItemsPerJob = maxItemsPerJob;
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

    /// <summary>Binds the <c>AgentPrism:Quotas</c> section.</summary>
    private static void BindQuotas(IConfigurationSection section, AgentPrismQuotaOptions options)
    {
        // Every sub-section is responsible for ITS OWN existence check: an
        // early return silently swallows later sections (see docs/hafiza/cekirdek-calistirma.md).
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(AgentPrismQuotaOptions.TimeZone)] is { Length: > 0 } timeZone)
        {
            options.TimeZone = timeZone;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.AllowOnStoreFailure), out var allowOnFailure))
        {
            options.AllowOnStoreFailure = allowOnFailure;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.PublishThresholdToRunStream), out var publishToRunStream))
        {
            options.PublishThresholdToRunStream = publishToRunStream;
        }

        var thresholds = section.GetSection(nameof(AgentPrismQuotaOptions.ThresholdPercents));

        if (thresholds.Exists())
        {
            var parsed = thresholds.GetChildren()
                .Select(child => int.TryParse(child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) ? percent : -1)
                .Where(percent => percent > 0)
                .ToList();

            if (parsed.Count > 0)
            {
                options.ThresholdPercents.Clear();

                foreach (var percent in parsed)
                {
                    options.ThresholdPercents.Add(percent);
                }
            }
        }
    }

    /// <summary>Binds the <c>AgentPrism:RateLimit</c> section.</summary>
    private static void BindRateLimit(IConfigurationSection section, AgentPrismRateLimitOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRateLimitOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRateLimitOptions.PermitLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var permitLimit))
        {
            options.PermitLimit = permitLimit;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismRateLimitOptions.Window)],
                CultureInfo.InvariantCulture,
                out var window))
        {
            options.Window = window;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRateLimitOptions.QueueLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var queueLimit))
        {
            options.QueueLimit = queueLimit;
        }

        if (Enum.TryParse<RateLimitPartitionKind>(
                section[nameof(AgentPrismRateLimitOptions.Partition)],
                ignoreCase: true,
                out var partition))
        {
            options.Partition = partition;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Idempotency</c> section.</summary>
    private static void BindIdempotency(IConfigurationSection section, AgentPrismIdempotencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismIdempotencyOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismIdempotencyOptions.MaxKeyLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxKeyLength))
        {
            options.MaxKeyLength = maxKeyLength;
        }
    }

    /// <summary>Binds the <c>AgentPrism:AsyncRun</c> section.</summary>
    private static void BindAsyncRun(IConfigurationSection section, AgentPrismAsyncRunOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismAsyncRunOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAsyncRunOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }
    }

    /// <summary>Binds the <c>AgentPrism:RunReconciliation</c> section.</summary>
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

    /// <summary>Binds the <c>AgentPrism:RunContinuation</c> section.</summary>
    private static void BindRunContinuation(IConfigurationSection section, AgentPrismRunContinuationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRunContinuationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRunContinuationOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Drain</c> section.</summary>
    private static void BindDrain(IConfigurationSection section, AgentPrismDrainOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismDrainOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismDrainOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }
    }

    /// <summary>Binds the <c>AgentPrism:StructuredResponse</c> section.</summary>
    private static void BindStructuredResponse(IConfigurationSection section, AgentPrismStructuredResponseOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismStructuredResponseOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismStructuredResponseOptions.MaxRepairAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRepairAttempts))
        {
            options.MaxRepairAttempts = maxRepairAttempts;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Canary</c> section.</summary>
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
