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

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconSchedulingOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Boolean(
            nameof(TraconSchedulingOptions.RunWorker),
            static o => o.RunWorker,
            static (o, v) => o.RunWorker = v);

        read.Int32(
            nameof(TraconSchedulingOptions.MaxConcurrentJobs),
            static o => o.MaxConcurrentJobs,
            static (o, v) => o.MaxConcurrentJobs = v);

        read.TimeSpan(
            nameof(TraconSchedulingOptions.PollInterval),
            static o => o.PollInterval,
            static (o, v) => o.PollInterval = v);

        read.TimeSpan(
            nameof(TraconSchedulingOptions.LeaseDuration),
            static o => o.LeaseDuration,
            static (o, v) => o.LeaseDuration = v);

        read.Int32(
            nameof(TraconSchedulingOptions.MaxAttempts),
            static o => o.MaxAttempts,
            static (o, v) => o.MaxAttempts = v);

        read.Int32(
            nameof(TraconSchedulingOptions.MaxItemsPerJob),
            static o => o.MaxItemsPerJob,
            static (o, v) => o.MaxItemsPerJob = v);

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
    /// A list that is present REPLACES the default. A dictionary entry keeps its
    /// key as written: lanes and handler keys are matched ordinally. A per-lane
    /// budget or a duration that is not readable is left out and named at startup
    /// by <see cref="UnreadableConfigurationValidator{TOptions}"/> — a skipped
    /// budget with no word is exactly the silent failure this method closes.
    /// </para>
    /// </remarks>
    private static void BindSchedulingCollections(IConfigurationSection section, TraconSchedulingOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        read.StringArray(
            nameof(TraconSchedulingOptions.Lanes),
            static o => o.Lanes,
            static (o, v) => o.Lanes = v);

        read.Int32Map(nameof(TraconSchedulingOptions.MaxConcurrentJobsPerLane), options.MaxConcurrentJobsPerLane);

        foreach (var child in section.GetSection(nameof(TraconSchedulingOptions.LaneByHandlerKey)).GetChildren())
        {
            // An empty lane is bound as written, not skipped: the validator
            // names it at startup instead of the mapping silently vanishing.
            options.LaneByHandlerKey[child.Key] = child.Value ?? string.Empty;
        }

        read.StringList(nameof(TraconSchedulingOptions.HttpSchedulableHandlerKeys), options.HttpSchedulableHandlerKeys);

        read.TimeSpanMap(nameof(TraconSchedulingOptions.MaxDurationByHandlerKey), options.MaxDurationByHandlerKey);
    }

    /// <summary>Binds single-executor selection settings from configuration.</summary>
    private static void BindSingletonExecution(IConfigurationSection section, SingletonExecutionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(SingletonExecutionOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.TimeSpan(
            nameof(SingletonExecutionOptions.LeaseDuration),
            static o => o.LeaseDuration,
            static (o, v) => o.LeaseDuration = v);

        if (section[nameof(SingletonExecutionOptions.OwnerId)] is { Length: > 0 } ownerId)
        {
            options.OwnerId = ownerId;
        }
    }

    /// <summary>Binds the <c>Tracon:Quotas</c> section.</summary>
    private static void BindQuotas(IConfigurationSection section, TraconQuotaOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        // Every sub-section is responsible for ITS OWN existence check: an
        // early return silently swallows later sections (see docs/hafiza/cekirdek-calistirma.md).
        if (!section.Exists())
        {
            return;
        }

        read.Boolean(
            nameof(TraconQuotaOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        if (section[nameof(TraconQuotaOptions.TimeZone)] is { Length: > 0 } timeZone)
        {
            options.TimeZone = timeZone;
        }

        read.Boolean(
            nameof(TraconQuotaOptions.AllowOnStoreFailure),
            static o => o.AllowOnStoreFailure,
            static (o, v) => o.AllowOnStoreFailure = v);

        read.Boolean(
            nameof(TraconQuotaOptions.PublishThresholdToRunStream),
            static o => o.PublishThresholdToRunStream,
            static (o, v) => o.PublishThresholdToRunStream = v);

        // F-247: a list that is present REPLACES the default. An entry that is not a
        // number is left out and named at startup with its position and its text
        // (F-572); skipping it with no word started the application with a shorter
        // list than the operator wrote.
        read.Int32List(nameof(TraconQuotaOptions.ThresholdPercents), options.ThresholdPercents);
    }

    /// <summary>Binds the <c>Tracon:RateLimit</c> section.</summary>
    private static void BindRateLimit(IConfigurationSection section, TraconRateLimitOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconRateLimitOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconRateLimitOptions.PermitLimit),
            static o => o.PermitLimit,
            static (o, v) => o.PermitLimit = v);

        read.TimeSpan(
            nameof(TraconRateLimitOptions.Window),
            static o => o.Window,
            static (o, v) => o.Window = v);

        read.Int32(
            nameof(TraconRateLimitOptions.QueueLimit),
            static o => o.QueueLimit,
            static (o, v) => o.QueueLimit = v);

        read.Enum<RateLimitPartitionKind>(
            nameof(TraconRateLimitOptions.Partition),
            static o => o.Partition,
            static (o, v) => o.Partition = v);
    }

    /// <summary>Binds the <c>Tracon:Idempotency</c> section.</summary>
    private static void BindIdempotency(IConfigurationSection section, TraconIdempotencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconIdempotencyOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconIdempotencyOptions.MaxKeyLength),
            static o => o.MaxKeyLength,
            static (o, v) => o.MaxKeyLength = v);
    }

    /// <summary>Binds the <c>Tracon:AsyncRun</c> section.</summary>
    private static void BindAsyncRun(IConfigurationSection section, TraconAsyncRunOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconAsyncRunOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconAsyncRunOptions.MaxAttempts),
            static o => o.MaxAttempts,
            static (o, v) => o.MaxAttempts = v);
    }

    /// <summary>Binds the <c>Tracon:RunReconciliation</c> section.</summary>
    private static void BindRunReconciliation(IConfigurationSection section, RunReconciliationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(RunReconciliationOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.TimeSpan(
            nameof(RunReconciliationOptions.HeartbeatInterval),
            static o => o.HeartbeatInterval,
            static (o, v) => o.HeartbeatInterval = v);

        read.TimeSpan(
            nameof(RunReconciliationOptions.OrphanThreshold),
            static o => o.OrphanThreshold,
            static (o, v) => o.OrphanThreshold = v);

        read.TimeSpan(
            nameof(RunReconciliationOptions.ScanInterval),
            static o => o.ScanInterval,
            static (o, v) => o.ScanInterval = v);

        read.Int32(
            nameof(RunReconciliationOptions.MaxRunsPerScan),
            static o => o.MaxRunsPerScan,
            static (o, v) => o.MaxRunsPerScan = v);
    }

    /// <summary>Binds the <c>Tracon:RunContinuation</c> section.</summary>
    private static void BindRunContinuation(IConfigurationSection section, TraconRunContinuationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconRunContinuationOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconRunContinuationOptions.MaxAttempts),
            static o => o.MaxAttempts,
            static (o, v) => o.MaxAttempts = v);
    }

    /// <summary>Binds the <c>Tracon:Drain</c> section.</summary>
    private static void BindDrain(IConfigurationSection section, TraconDrainOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconDrainOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.TimeSpan(
            nameof(TraconDrainOptions.Timeout),
            static o => o.Timeout,
            static (o, v) => o.Timeout = v);
    }

    /// <summary>Binds the <c>Tracon:StructuredResponse</c> section.</summary>
    private static void BindStructuredResponse(IConfigurationSection section, TraconStructuredResponseOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconStructuredResponseOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconStructuredResponseOptions.MaxRepairAttempts),
            static o => o.MaxRepairAttempts,
            static (o, v) => o.MaxRepairAttempts = v);
    }

    /// <summary>Binds the <c>Tracon:Workspace</c> section.</summary>
    private static void BindWorkspace(IConfigurationSection section, TraconWorkspaceOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Int64(
            nameof(TraconWorkspaceOptions.MaxFileBytes),
            static o => o.MaxFileBytes,
            static (o, v) => o.MaxFileBytes = v);

        read.Int64(
            nameof(TraconWorkspaceOptions.MaxSessionBytes),
            static o => o.MaxSessionBytes,
            static (o, v) => o.MaxSessionBytes = v);

        read.Int64(
            nameof(TraconWorkspaceOptions.MaxTenantBytes),
            static o => o.MaxTenantBytes,
            static (o, v) => o.MaxTenantBytes = v);
    }

    /// <summary>Binds the <c>Tracon:Canary</c> section.</summary>
    private static void BindCanary(IConfigurationSection section, CanaryOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(CanaryOptions.AutoRollbackEnabled),
            static o => o.AutoRollbackEnabled,
            static (o, v) => o.AutoRollbackEnabled = v);

        read.TimeSpan(
            nameof(CanaryOptions.ScanInterval),
            static o => o.ScanInterval,
            static (o, v) => o.ScanInterval = v);
    }
}
