using System.Buffers;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Stores run records and the event stream in a SQL database.
/// </summary>
/// <remarks>
/// <para>
/// Events are <em>append-only</em>. <see cref="RunEventWriter"/>
/// produces the sequence number; the store only writes it.
/// </para>
/// <para>
/// The behavior contract is identical to <see cref="InMemoryRunStore"/> and is
/// protected by shared contract tests. All operations are scoped to
/// <see cref="ITenantContext.TenantId"/>.
/// </para>
/// </remarks>
internal sealed class SqlRunStore : IRunStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new run store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public SqlRunStore(
        SqlStoreContext context,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;
    }

    /// <summary>The gateway to provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    /// <remarks>
    /// <c>_sql.InsertRun</c> is an UPSERT (UPDATES on a conflict over
    /// <c>id</c>). For a queued run, this method is called TWICE with the SAME
    /// <see cref="RunStartInfo.RunId"/> — first with <see cref="RunStatus.Queued"/>
    /// (the HTTP layer), then when the worker actually runs the job (this time
    /// with the default <see cref="RunStatus.Running"/>). A plain INSERT would
    /// make the second call raise a primary-key conflict.
    /// </remarks>
    public async ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        var record = new RunRecord
        {
            Id = info.RunId,
            AgentName = info.AgentName,
            Kind = info.Kind,
            WorkflowName = info.WorkflowName,
            Status = info.Status,
            StartedAt = info.StartedAt,
            TenantId = info.TenantId ?? _tenantContext.TenantId,
            UserId = info.UserId,
            Labels = info.Labels is { Count: > 0 } ? info.Labels : null,
            SessionId = info.SessionId,
            ModelId = info.ModelId,
            ModelProvider = info.ModelProvider,
            IsStreaming = info.IsStreaming,
            ParentRunId = info.ParentRunId,
            RootRunId = info.RootRunId,
            Depth = info.Depth,
            AgentVersion = info.AgentVersion,
            ExperimentId = info.ExperimentId,
            Variant = info.Variant,
            ReplayOfRunId = info.ReplayOfRunId,
            ContinuedFromRunId = info.ContinuedFromRunId,
        };

        var command = CreateCommand(_sql.InsertRun);
        DbHelpers.Add(command, "id", record.Id);
        DbHelpers.Add(command, "tenant_id", record.TenantId!);
        DbHelpers.Add(command, "agent_name", record.AgentName);
        AddNullableText(command, "session_id", record.SessionId);
        AddNullableText(command, "model_id", record.ModelId);
        AddNullableText(command, "model_provider", record.ModelProvider);
        DbHelpers.Add(command, "status", (short)record.Status);
        Dialect.AddTimestamp(command, "started_at", record.StartedAt);
        DbHelpers.Add(command, "is_streaming", record.IsStreaming);
        AddNullableUuid(command, "parent_run_id", record.ParentRunId);
        AddNullableUuid(command, "root_run_id", record.RootRunId);
        DbHelpers.Add(command, "kind", (short)record.Kind);
        AddNullableText(command, "workflow_name", record.WorkflowName);
        AddNullableInt32(command, "agent_version", record.AgentVersion);
        AddNullableUuid(command, "experiment_id", record.ExperimentId);
        AddNullableText(command, "variant", record.Variant);
        AddNullableUuid(command, "replay_of_run_id", record.ReplayOfRunId);
        AddNullableUuid(command, "continued_from_run_id", record.ContinuedFromRunId);
        AddNullableText(command, "user_id", record.UserId);
        Dialect.AddJsonb(command, "labels", JsonStringMapCodec.Serialize(record.Labels));

        // The depth column is smallint; its source is the budget's MaxDepth
        // value and never comes close to the short limit in any deployment.
        DbHelpers.Add(command, "depth", (short)Math.Clamp(record.Depth, 0, short.MaxValue));

        // The UPSERT's OUTPUT/RETURNING clause reports the COALESCED user_id
        // and labels actually persisted, not the raw values `record` was
        // built from -- on a second StartRunAsync of a queued run, `record`
        // carries whatever this call's own info gave it (often null
        // attribution), while the row itself preserved the FIRST call's
        // attribution. The return value must match what GetRunAsync would
        // report immediately after.
        var coalesced = await DbHelpers.ReadSingleAsync(command, ReadCoalescedAttribution, cancellationToken)
            .ConfigureAwait(false);

        if (coalesced is not null)
        {
            record = record with
            {
                UserId = coalesced.UserId,
                Labels = coalesced.Labels,
            };
        }

        return record;
    }

    /// <summary>Maps the <c>user_id</c>/<c>labels</c> row returned by the <c>StartRunAsync</c> UPSERT.</summary>
    private static CoalescedAttribution ReadCoalescedAttribution(DbDataReader reader)
        => new(
            DbHelpers.GetNullableString(reader, 0),
            JsonStringMapCodec.Deserialize(DbHelpers.GetNullableString(reader, 1)));

    /// <summary>The attribution fields the <c>StartRunAsync</c> UPSERT reports back after its own COALESCE.</summary>
    private sealed record CoalescedAttribution(string? UserId, IReadOnlyDictionary<string, string>? Labels);

    /// <inheritdoc />
    public async ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        var command = CreateCommand(_sql.InsertRunEvent);
        DbHelpers.Add(command, "run_id", runEvent.RunId);
        DbHelpers.Add(command, "seq", runEvent.Sequence);
        DbHelpers.Add(command, "type", (short)runEvent.Type);
        Dialect.AddText(command, "text", ProtectedValue.Write(_context, ProtectedColumn.RunEventText, runEvent.Text));
        AddNullableText(command, "tool_name", runEvent.ToolName);
        AddNullableText(command, "tool_call_id", runEvent.ToolCallId);
        Dialect.AddText(command, "payload", ProtectedValue.Write(_context, ProtectedColumn.RunEventPayload, runEvent.Payload));
        AddNullableText(command, "custom_type", runEvent.CustomType);
        Dialect.AddTimestamp(command, "created_at", runEvent.Timestamp);

        // 🚨 EXPECTED tenant, NOT the ambient tenant. RunStartInfo.TenantId can
        // deliberately override the ambient tenant (this is how workflows and
        // the job queue work), so filtering by the ambient tenant would drop
        // legitimate writes. NULL means no check.
        // Rationale: K-355.
        AddNullableText(command, "tenant_id", runEvent.TenantId);

        int affected;

        try
        {
            affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (Dialect.IsForeignKeyViolation(ex))
        {
            throw new AgentPrismException(
                $"Run with id '{runEvent.RunId}' was not found. " +
                "StartRunAsync must be called before adding an event.",
                ex);
        }
        catch (DbException ex) when (Dialect.IsUniqueViolation(ex))
        {
            // run_events' only unique constraint is (run_id, seq): a
            // duplicate here always means a caller reused a sequence
            // number RunEventWriter had already assigned. That is a
            // caller error, not a legitimate retry -- rejecting it (rather
            // than silently ignoring or overwriting) keeps the append-only
            // sequence gap-free and matches the in-memory store.
            throw new AgentPrismException(
                $"Run '{runEvent.RunId}' already has an event with sequence '{runEvent.Sequence}'. " +
                "The event was not written.",
                ex);
        }

        if (affected == 0)
        {
            throw new AgentPrismException(
                $"Run with id '{runEvent.RunId}' was not found or does not belong to the expected " +
                $"tenant ('{runEvent.TenantId}'). The event was not written.");
        }
    }

    /// <inheritdoc />
    public async ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        var command = CreateCommand(_sql.UpdateRunCompletion);
        DbHelpers.Add(command, "id", completion.RunId);
        DbHelpers.Add(command, "status", (short)completion.Status);
        Dialect.AddTimestamp(command, "completed_at", completion.CompletedAt);
        DbHelpers.Add(command, "event_count", completion.EventCount);
        AddNullableInt64(command, "input_tokens", completion.Usage?.InputTokens);
        AddNullableInt64(command, "output_tokens", completion.Usage?.OutputTokens);
        AddNullableInt64(command, "total_tokens", completion.Usage?.TotalTokens);

        // 🚨 These four are counted INSIDE the totals above (the
        // Microsoft.Extensions.AI contract) and are written beside them, never
        // added to them. A counter the provider did not report stays NULL:
        // writing zero claims a measurement that was never made.
        AddNullableInt64(command, "cached_input_tokens", completion.Usage?.CachedInputTokens);
        AddNullableInt64(command, "reasoning_tokens", completion.Usage?.ReasoningTokens);
        AddNullableInt64(command, "audio_input_tokens", completion.Usage?.AudioInputTokens);
        AddNullableInt64(command, "audio_output_tokens", completion.Usage?.AudioOutputTokens);
        AddNullableText(command, "error_type", completion.Error?.Type);
        AddNullableText(command, "error_message", completion.Error?.Message);
        Dialect.AddInt16(command, "error_class", completion.Error?.Class is { } errorClass ? (short)errorClass : null);
        AddNullableText(command, "error_fingerprint", completion.Error?.Fingerprint);
        AddNullableDecimal(command, "input_cost", completion.Cost?.InputCost);
        AddNullableDecimal(command, "input_price_per_mtok", completion.Cost?.InputPricePerMillionTokens);
        AddNullableDecimal(command, "output_cost", completion.Cost?.OutputCost);
        AddNullableDecimal(command, "output_price_per_mtok", completion.Cost?.OutputPricePerMillionTokens);
        AddNullableDecimal(command, "cached_input_cost", completion.Cost?.CachedInputCost);
        AddNullableDecimal(command, "cached_input_price_per_mtok", completion.Cost?.CachedInputPricePerMillionTokens);
        AddNullableText(command, "cost_currency", completion.Cost?.Currency);
        Dialect.AddInt16(command, "pricing_source", completion.Cost is { } cost ? (short)cost.Source : null);

        // NULL leaves the column at the value StartRunAsync already wrote
        // (phase 62) — the SQL text COALESCEs it, this is not a conditional here.
        AddNullableText(command, "model_id", completion.ModelId);
        AddNullableText(command, "model_provider", completion.ModelProvider);

        // EXPECTED tenant (K-355). NULL means no check.
        AddNullableText(command, "tenant_id", completion.TenantId);

        var affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        if (affected == 0)
        {
            throw new AgentPrismException(
                $"Run with id '{completion.RunId}' was not found" +
                (completion.TenantId is null
                    ? "."
                    : $" or does not belong to the expected tenant ('{completion.TenantId}')."));
        }
    }

    /// <inheritdoc />
    public async ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.UpdateRunCost);
        DbHelpers.Add(command, "id", runId);
        AddNullableDecimal(command, "input_cost", cost?.InputCost);
        AddNullableDecimal(command, "input_price_per_mtok", cost?.InputPricePerMillionTokens);
        AddNullableDecimal(command, "output_cost", cost?.OutputCost);
        AddNullableDecimal(command, "output_price_per_mtok", cost?.OutputPricePerMillionTokens);
        AddNullableDecimal(command, "cached_input_cost", cost?.CachedInputCost);
        AddNullableDecimal(command, "cached_input_price_per_mtok", cost?.CachedInputPricePerMillionTokens);
        AddNullableText(command, "cost_currency", cost?.Currency);
        Dialect.AddInt16(command, "pricing_source", cost is { } value ? (short)value.Source : null);

        // EXPECTED tenant (K-355). The maintenance endpoint
        // (POST /api/stats/recalculate-costs) gets ids from a query already
        // FILTERED by tenant and carries the same tenant here too, so the
        // two-phase path has no race.
        AddNullableText(command, "tenant_id", tenantId);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "The ids come from the CALLING process's OWN IRunCancellationRegistry ledger and are " +
        "already scoped to runs that process is actually executing; this is a maintenance signal, it reads no data.")]
    public async ValueTask TouchHeartbeatAsync(
        IReadOnlyCollection<Guid> runIds,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        // A single bulk UPDATE (WHERE id IN (array)) would require converting
        // the array parameter to JSON text on SQLite/SQL Server; this carried a
        // MISMATCH risk between uuid casing (K-191) and array serialization
        // (System.Text.Json, lowercase). The number of CONCURRENTLY running
        // runs in this process is small (IRunCancellationRegistry.ActiveRunIds);
        // N single UPDATEs in a loop give the same result without that risk.
        foreach (var runId in runIds)
        {
            var command = CreateCommand(_sql.TouchRunHeartbeat);
            DbHelpers.Add(command, "id", runId);
            Dialect.AddTimestamp(command, "at", at);
            DbHelpers.Add(command, "status_running", (short)RunStatus.Running);

            await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    [TenantAgnostic(
        "This is a maintenance job and scans orphaned rows across ALL tenants; filtering by the " +
        "ambient tenant would leave other tenants' rows stuck in Running forever.")]
    public async ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var command = CreateCommand(_sql.ClaimOrphanedRuns);
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        Dialect.AddTimestamp(command, "now", now);
        DbHelpers.Add(command, "error_class", (short)RunErrorClass.Infrastructure);
        DbHelpers.Add(command, "error_fingerprint", OrphanedFingerprint);
        DbHelpers.Add(command, "status_running", (short)RunStatus.Running);
        Dialect.AddTimestamp(command, "stale_before", staleBefore);
        DbHelpers.Add(command, "max", Math.Max(max, 0));

        var claimed = await DbHelpers.ReadListAsync(command, ReadOrphanedRun, cancellationToken).ConfigureAwait(false);

        // The RunEventWriter no longer exists in that process; we write the
        // event here. The next scan will NEVER see this run_id again (its
        // status is now Failed), so N separate INSERTs (instead of a single
        // batched write) are not a hot-path cost here -- this is a maintenance
        // job bounded by MaxRunsPerScan that runs once a minute.
        foreach (var record in claimed)
        {
            var eventCommand = CreateCommand(_sql.InsertOrphanRunEvent);
            DbHelpers.Add(eventCommand, "run_id", record.Id);
            DbHelpers.Add(eventCommand, "type", (short)RunEventType.RunFailed);
            Dialect.AddText(eventCommand, "text", ProtectedValue.Write(_context, ProtectedColumn.RunEventText, record.Error?.Message));
            Dialect.AddTimestamp(eventCommand, "created_at", now);

            await DbHelpers.ExecuteAsync(eventCommand, cancellationToken).ConfigureAwait(false);
        }

        return claimed;
    }

    /// <summary>
    /// The clustering fingerprint for orphaned run errors. A fixed string --
    /// NOT a SHA-256 hash; the rationale matches the constant of the same
    /// name in <see cref="InMemoryRunStore"/> (AgentPrism.Core's ErrorFingerprint
    /// is not reachable from this assembly).
    /// </summary>
    private const string OrphanedFingerprint = "orphaned";

    /// <inheritdoc />
    public async ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRun);
        DbHelpers.Add(command, "id", runId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers.ReadSingleAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(
        RunQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectRuns);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        AddNullableText(command, "agent_name", query.AgentName);
        Dialect.AddInt16(command, "status", (short?)query.Status);
        Dialect.AddInt16(command, "kind", (short?)query.Kind);
        AddNullableText(command, "session_id", query.SessionId);
        AddNullableText(command, "error_type", query.ErrorType);
        AddNullableText(command, "user_id", query.UserId);
        AddLabelFilter(command, query.LabelKey, query.LabelValue);
        Dialect.AddTimestamp(command, "started_after", query.StartedAfter);
        AddNullableUuid(command, "parent_run_id", query.ParentRunId);
        AddNullableUuid(command, "root_run_id", query.RootRunId);
        DbHelpers.Add(command, "only_root_runs", query.OnlyRootRuns);
        DbHelpers.Add(command, "skip", Math.Max(query.Skip, 0));
        DbHelpers.Add(command, "take", Math.Max(query.Take, 0));

        return await DbHelpers.ReadListAsync(command, ReadRun, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectRunStatistics);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        AddNullableText(command, "agent_name", query.AgentName);
        AddNullableText(command, "user_id", query.UserId);
        AddLabelFilter(command, query.LabelKey, query.LabelValue);
        Dialect.AddTimestamp(command, "started_after", query.StartedAfter);
        DbHelpers.Add(command, "max_agents", Math.Max(query.MaxAgents, 0));
        DbHelpers.Add(command, "status_running", (short)RunStatus.Running);
        DbHelpers.Add(command, "status_completed", (short)RunStatus.Completed);
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        DbHelpers.Add(command, "status_canceled", (short)RunStatus.Canceled);
        DbHelpers.Add(command, "status_awaiting", (short)RunStatus.AwaitingInput);
        DbHelpers.Add(command, "kind_eval", (short)RunKind.Eval);
        DbHelpers.Add(command, "pricing_source_unknown", (short)PricingSource.Unknown);
        DbHelpers.Add(command, "kind_binary", (short)RunScoreKind.Binary);
        DbHelpers.Add(command, "top_clusters", (long)TopErrorClusterCount);

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                // First result set: the overall summary. The aggregation query
                // always returns exactly one row.
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return EmptyStatistics;
                }

                var total = reader.GetInt64(0);
                var completed = reader.GetInt64(1);
                var failed = reader.GetInt64(2);
                var canceled = reader.GetInt64(3);
                var running = reader.GetInt64(4);
                var awaitingInput = reader.GetInt64(5);
                var inputTokens = reader.GetInt64(6);
                var outputTokens = reader.GetInt64(7);
                var totalTokens = reader.GetInt64(8);
                var totalCost = DbHelpers.GetNullableDecimal(reader, 9);
                var currency = DbHelpers.GetNullableString(reader, 10);
                var runsWithUnknownPricing = reader.GetInt64(11);
                var scoredRuns = reader.GetInt64(12);
                var positiveRate = reader.IsDBNull(13) ? (double?)null : reader.GetDouble(13);

                // 🚨 14-17: appended in phase 68 so the fixed positions above do
                // not move. Counted INSIDE inputTokens/outputTokens, reported
                // beside them.
                var cachedInputTokens = reader.GetInt64(14);
                var reasoningTokens = reader.GetInt64(15);
                var audioInputTokens = reader.GetInt64(16);
                var audioOutputTokens = reader.GetInt64(17);

                // Second result set: the breakdown by agent.
                var byAgent = new List<RunAgentStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byAgent.Add(new RunAgentStatistics
                        {
                            AgentName = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            FailedRuns = reader.GetInt64(2),
                            TotalTokens = reader.GetInt64(3),
                        });
                    }
                }

                // Third result set: the breakdown by model. Runs with a NULL
                // model_id are excluded from the query but are still counted
                // in the totals.
                var byModel = new List<RunModelStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byModel.Add(new RunModelStatistics
                        {
                            ModelId = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            InputTokens = reader.GetInt64(2),
                            OutputTokens = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                            TotalCost = DbHelpers.GetNullableDecimal(reader, 5),
                        });
                    }
                }

                // Fourth result set: the breakdown by version. Runs with a
                // NULL agent_version are excluded from the query but are still
                // counted in the totals.
                var byVersion = new List<RunVersionStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byVersion.Add(new RunVersionStatistics
                        {
                            AgentName = reader.GetString(0),
                            Version = reader.GetInt32(1),
                            TotalRuns = reader.GetInt64(2),
                            FailedRuns = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                        });
                    }
                }

                // Fifth result set: the breakdown by error class (Phase 44).
                var errorTotals = new List<(RunErrorClass Class, long TotalRuns)>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        errorTotals.Add(((RunErrorClass)reader.GetInt16(0), reader.GetInt64(1)));
                    }
                }

                // Sixth result set: the top three fingerprints per class,
                // returned SORTED by class (the SQL text guarantees this).
                var clustersByClass = new Dictionary<RunErrorClass, List<RunErrorCluster>>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var errorClass = (RunErrorClass)reader.GetInt16(0);

                        if (!clustersByClass.TryGetValue(errorClass, out var clusters))
                        {
                            clusters = [];
                            clustersByClass[errorClass] = clusters;
                        }

                        clusters.Add(new RunErrorCluster
                        {
                            Fingerprint = reader.GetString(1),
                            Count = reader.GetInt64(2),
                            SampleMessage = reader.GetString(3),
                            SampleRunId = reader.GetGuid(4),
                            LastSeenAt = DbHelpers.GetTimestamp(reader, 5),
                        });
                    }
                }

                // Seventh result set: the breakdown by user (phase 68). Runs
                // that carry no user are excluded by the query but are still
                // counted in the totals.
                var byUser = new List<RunUserStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byUser.Add(new RunUserStatistics
                        {
                            UserId = reader.GetString(0),
                            TotalRuns = reader.GetInt64(1),
                            FailedRuns = reader.GetInt64(2),
                            TotalTokens = reader.GetInt64(3),
                            TotalCost = DbHelpers.GetNullableDecimal(reader, 4),
                        });
                    }
                }

                // Eighth result set: the breakdown by label. 🚨 One row per
                // distinct key/value pair, so these rows do NOT sum to
                // TotalRuns -- a run carrying three labels appears three times.
                var byLabel = new List<RunLabelStatistics>();

                if (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        byLabel.Add(new RunLabelStatistics
                        {
                            Key = reader.GetString(0),
                            Value = reader.GetString(1),
                            TotalRuns = reader.GetInt64(2),
                            FailedRuns = reader.GetInt64(3),
                            TotalTokens = reader.GetInt64(4),
                            TotalCost = DbHelpers.GetNullableDecimal(reader, 5),
                        });
                    }
                }

                var byErrorClass = errorTotals
                    .Select(pair => new RunErrorStatistics
                    {
                        Class = pair.Class,
                        TotalRuns = pair.TotalRuns,
                        TopClusters = clustersByClass.TryGetValue(pair.Class, out var clusters)
                            ? clusters
                            : [],
                    })
                    .ToList();

                return new RunStatistics
                {
                    TotalRuns = total,
                    CompletedRuns = completed,
                    FailedRuns = failed,
                    CanceledRuns = canceled,
                    RunningRuns = running,
                    AwaitingInputRuns = awaitingInput,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = totalTokens,
                    CachedInputTokens = cachedInputTokens,
                    ReasoningTokens = reasoningTokens,
                    AudioInputTokens = audioInputTokens,
                    AudioOutputTokens = audioOutputTokens,
                    TotalCost = totalCost,
                    Currency = currency,
                    RunsWithUnknownPricing = runsWithUnknownPricing,
                    ScoredRuns = scoredRuns,
                    PositiveRate = positiveRate,
                    ByAgent = byAgent,
                    ByModel = byModel,
                    ByVersion = byVersion,
                    ByUser = byUser,
                    ByLabel = byLabel,
                    ByErrorClass = byErrorClass,
                };
            }
        }
    }

    /// <summary>The upper limit applied when selecting the top cluster set for one error class.</summary>
    private const int TopErrorClusterCount = 3;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        RunTimeSeriesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        RunTimeSeriesBucketing.Validate(query.From, query.To, query.Bucket);

        var command = CreateCommand(_sql.SelectRunTimeSeries);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddTimestamp(command, "from_ts", query.From);
        Dialect.AddTimestamp(command, "to_ts", query.To);
        DbHelpers.Add(command, "bucket_unit", query.Bucket == TimeSeriesBucket.Hour ? "hour" : "day");
        Dialect.AddInterval(command, "bucket_step", RunTimeSeriesBucketing.StepFor(query.Bucket));
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        AddNullableText(command, "agent_name", query.AgentName);
        AddNullableText(command, "model_id", query.ModelId);
        Dialect.AddInt16(command, "kind", (short?)query.Kind);

        return await DbHelpers.ReadListAsync(command, ReadTimeSeriesPoint, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectRunEvents);
        DbHelpers.Add(command, "run_id", runId);
        DbHelpers.Add(command, "from_sequence", fromSequence);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        await using (command.ConfigureAwait(false))
        {
            var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return ReadEvent(reader);
                }
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var command = CreateCommand(_sql.InsertToolInvocation);
        DbHelpers.Add(command, "id", invocation.Id);
        DbHelpers.Add(command, "run_id", invocation.RunId);
        DbHelpers.Add(command, "tool_name", invocation.ToolName);
        AddNullableText(command, "tool_call_id", invocation.ToolCallId);
        AddNullableText(command, "source", invocation.Source);
        Dialect.AddText(command, "arguments", ProtectedValue.Write(_context, ProtectedColumn.ToolArguments, invocation.Arguments));
        Dialect.AddText(command, "result", ProtectedValue.Write(_context, ProtectedColumn.ToolResult, invocation.Result));
        // Duration is stored in milliseconds in the `integer` column; a tool
        // call longer than 24 days is unrealistic and does not overflow.
        Dialect.AddInt32(command, "duration_ms", invocation.Duration is { } duration
                ? (int)Math.Clamp(duration.TotalMilliseconds, 0, int.MaxValue)
                : null);
        AddNullableText(command, "error", invocation.Error);
        Dialect.AddTimestamp(command, "created_at", invocation.CreatedAt);

        // Non-token usage measurement (Phase 28). Most calls carry no
        // measurement and all five columns stay NULL. If the price is
        // undefined, `cost` is written as NULL, not ZERO.
        AddNullableText(command, "usage_unit", invocation.Usage?.Unit);
        Dialect.AddDecimal(command, "usage_quantity", invocation.Usage?.Quantity);
        Dialect.AddNullableBoolean(command, "usage_estimated", invocation.Usage?.IsEstimated);
        Dialect.AddDecimal(command, "cost", invocation.Usage?.Cost);
        AddNullableText(command, "cost_currency", invocation.Usage?.Currency);

        // Authorization decision and timeout marker (Phase 69). Both NOT
        // NULL: a call that is neither denied nor timed out writes false,
        // not NULL — this is a settled fact about the call, not an unknown.
        Dialect.AddBoolean(command, "authorization_denied", invocation.AuthorizationDenied);
        Dialect.AddBoolean(command, "timed_out", invocation.TimedOut);

        // EXPECTED tenant (K-355). NULL means no check.
        AddNullableText(command, "tenant_id", invocation.TenantId);

        int affected;

        try
        {
            affected = await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException ex) when (Dialect.IsForeignKeyViolation(ex))
        {
            throw new AgentPrismException(
                $"Run with id '{invocation.RunId}' was not found. " +
                "StartRunAsync must be called before recording a tool call.",
                ex);
        }

        if (affected == 0)
        {
            throw new AgentPrismException(
                $"Run with id '{invocation.RunId}' was not found or does not belong to the expected " +
                $"tenant ('{invocation.TenantId}'). The tool call was not written.");
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(_sql.SelectToolInvocations);
        DbHelpers.Add(command, "run_id", runId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        return await DbHelpers
            .ReadListAsync(command, ReadToolInvocation, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
        ToolUsageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectToolUsage);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        Dialect.AddTimestamp(command, "started_after", query.StartedAfter);
        DbHelpers.Add(command, "max_tools", Math.Max(query.MaxTools, 0));

        return await DbHelpers
            .ReadListAsync(command, ReadToolUsage, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectExperimentResults);
        DbHelpers.Add(command, "tenant_id", query.TenantId ?? _tenantContext.TenantId);
        DbHelpers.Add(command, "experiment_id", query.ExperimentId);
        DbHelpers.Add(command, "status_completed", (short)RunStatus.Completed);
        DbHelpers.Add(command, "status_failed", (short)RunStatus.Failed);
        DbHelpers.Add(command, "status_canceled", (short)RunStatus.Canceled);
        DbHelpers.Add(command, "score_kind_numeric", (short)RunScoreKind.Numeric);

        return await DbHelpers
            .ReadListAsync(command, ReadExperimentVariantResult, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The neutral result for the summary query when it returns no row.</summary>
    private static RunStatistics EmptyStatistics { get; } = new()
    {
        TotalRuns = 0,
        CompletedRuns = 0,
        FailedRuns = 0,
        CanceledRuns = 0,
        RunningRuns = 0,
    };

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private RunEvent ReadEvent(DbDataReader reader)
        => new()
        {
            RunId = reader.GetGuid(0),
            Sequence = reader.GetInt64(1),
            Type = (RunEventType)reader.GetInt16(2),
            Text = ProtectedValue.Read(_context, DbHelpers.GetNullableString(reader, 3)),
            ToolName = DbHelpers.GetNullableString(reader, 4),
            ToolCallId = DbHelpers.GetNullableString(reader, 5),
            Payload = ProtectedValue.Read(_context, DbHelpers.GetNullableString(reader, 6)),
            Timestamp = DbHelpers.GetTimestamp(reader, 7),
            CustomType = DbHelpers.GetNullableString(reader, 8),
        };

    /// <summary>
    /// Reads the RETURNING/OUTPUT columns of <c>ClaimOrphanedRuns</c>. Deliberately
    /// SEPARATE from <see cref="ReadRun"/>: that one relies on a LATERAL/OUTER
    /// APPLY join for tree totals, which orphan reclamation does not need to
    /// pay for -- a run that has never completed has neither usage nor cost
    /// to aggregate.
    /// </summary>
    private static RunRecord ReadOrphanedRun(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            AgentName = reader.GetString(2),
            SessionId = DbHelpers.GetNullableString(reader, 3),
            Status = (RunStatus)reader.GetInt16(4),
            StartedAt = DbHelpers.GetTimestamp(reader, 5),
            CompletedAt = reader.IsDBNull(6) ? null : DbHelpers.GetTimestamp(reader, 6),
            IsStreaming = reader.GetBoolean(7),
            ModelId = DbHelpers.GetNullableString(reader, 8),
            Kind = (RunKind)reader.GetInt16(9),
            WorkflowName = DbHelpers.GetNullableString(reader, 10),
            AgentVersion = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            ExperimentId = reader.IsDBNull(12) ? null : reader.GetGuid(12),
            Variant = DbHelpers.GetNullableString(reader, 13),
            ReplayOfRunId = reader.IsDBNull(14) ? null : reader.GetGuid(14),
            ParentRunId = reader.IsDBNull(15) ? null : reader.GetGuid(15),
            RootRunId = reader.IsDBNull(16) ? null : reader.GetGuid(16),
            Depth = reader.GetInt16(17),
            EventCount = reader.GetInt64(18),
            Error = new RunError
            {
                Type = reader.GetString(19),
                Message = DbHelpers.GetNullableString(reader, 20) ?? string.Empty,
                Class = reader.IsDBNull(21) ? null : (RunErrorClass)reader.GetInt16(21),
                Fingerprint = DbHelpers.GetNullableString(reader, 22),
            },

            // 🚨 23: Added at the END in Phase 87. NULL on every row written
            // before the column existed, and on every run that is not a continuation.
            ContinuedFromRunId = reader.IsDBNull(23) ? null : reader.GetGuid(23),
        };

    private static RunRecord ReadRun(DbDataReader reader)
    {
        var errorType = DbHelpers.GetNullableString(reader, RunOrdinals.ErrorType);
        var ownUsage = ReadUsage(reader);
        var ownCost = ReadCost(reader);

        return new RunRecord
        {
            Id = reader.GetGuid(RunOrdinals.Id),
            TenantId = reader.GetString(RunOrdinals.TenantId),
            AgentName = reader.GetString(RunOrdinals.AgentName),
            SessionId = DbHelpers.GetNullableString(reader, RunOrdinals.SessionId),
            Status = (RunStatus)reader.GetInt16(RunOrdinals.Status),
            StartedAt = DbHelpers.GetTimestamp(reader, RunOrdinals.StartedAt),
            CompletedAt = reader.IsDBNull(RunOrdinals.CompletedAt) ? null : DbHelpers.GetTimestamp(reader, RunOrdinals.CompletedAt),
            IsStreaming = reader.GetBoolean(RunOrdinals.IsStreaming),
            Usage = ownUsage,
            EventCount = reader.GetInt64(RunOrdinals.EventCount),
            ModelId = DbHelpers.GetNullableString(reader, RunOrdinals.ModelId),
            ModelProvider = DbHelpers.GetNullableString(reader, RunOrdinals.ModelProvider),
            ParentRunId = reader.IsDBNull(RunOrdinals.ParentRunId) ? null : reader.GetGuid(RunOrdinals.ParentRunId),
            RootRunId = reader.IsDBNull(RunOrdinals.RootRunId) ? null : reader.GetGuid(RunOrdinals.RootRunId),
            Depth = reader.GetInt16(RunOrdinals.Depth),
            ChildRunCount = reader.IsDBNull(RunOrdinals.ChildCount) ? 0 : reader.GetInt32(RunOrdinals.ChildCount),
            TreeUsage = ReadTreeUsage(reader, ownUsage),
            Kind = (RunKind)reader.GetInt16(RunOrdinals.Kind),
            WorkflowName = DbHelpers.GetNullableString(reader, RunOrdinals.WorkflowName),
            AgentVersion = reader.IsDBNull(RunOrdinals.AgentVersion) ? null : reader.GetInt32(RunOrdinals.AgentVersion),
            ExperimentId = reader.IsDBNull(RunOrdinals.ExperimentId) ? null : reader.GetGuid(RunOrdinals.ExperimentId),
            Variant = DbHelpers.GetNullableString(reader, RunOrdinals.Variant),
            Cost = ownCost,
            TreeCost = ReadTreeCost(reader, ownCost),

            // Added at the END in Phase 47. NULL on old rows; that means
            // the row is not a replay.
            ReplayOfRunId = reader.IsDBNull(RunOrdinals.ReplayOfRunId) ? null : reader.GetGuid(RunOrdinals.ReplayOfRunId),

            // Appended at the END in Phase 68. NULL on every row
            // written before the columns existed, and on every run of an
            // application that registers no IRunAttributionContext (K-014 -- no
            // backfill).
            UserId = DbHelpers.GetNullableString(reader, RunOrdinals.UserId),
            Labels = JsonStringMapCodec.Deserialize(DbHelpers.GetNullableString(reader, RunOrdinals.Labels)),

            // Columns ALWAYS appended at the end (Phase 44). On old
            // rows error_class is NULL -- it falls into the Unknown bucket
            // (RunStatistics.ByErrorClass, K-014 -- no backfill).
            Error = errorType is null
                ? null
                : new RunError
                {
                    Type = errorType,
                    Message = DbHelpers.GetNullableString(reader, RunOrdinals.ErrorMessage) ?? string.Empty,
                    Class = reader.IsDBNull(RunOrdinals.ErrorClass) ? null : (RunErrorClass)reader.GetInt16(RunOrdinals.ErrorClass),
                    Fingerprint = DbHelpers.GetNullableString(reader, RunOrdinals.ErrorFingerprint),
                },

            // Added at the END in Phase 87 (after tree.cost_cached_input,
            // the last column Phase 68 appended). NULL on every row written
            // before the column existed, and on every run that is not a continuation.
            ContinuedFromRunId = reader.IsDBNull(RunOrdinals.ContinuedFromRunId) ? null : reader.GetGuid(RunOrdinals.ContinuedFromRunId),
        };
    }

    /// <summary>Combines the tree's token total with the record's own usage.</summary>
    /// <remarks>
    /// The subquery only aggregates <em>descendant</em> runs; the record's own
    /// usage is added here. If neither the record nor the tree has usage, the
    /// value stays <see langword="null"/>: writing zero would make "the
    /// provider reported no tokens" indistinguishable from "no tokens were
    /// spent at all".
    /// </remarks>
    private static RunUsage? ReadTreeUsage(DbDataReader reader, RunUsage? ownUsage)
    {
        var descendantRows = reader.IsDBNull(RunOrdinals.UsageRows) ? 0 : reader.GetInt64(RunOrdinals.UsageRows);

        if (descendantRows == 0 && ownUsage is null)
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = (reader.IsDBNull(RunOrdinals.TreeInputTokens) ? 0 : reader.GetInt64(RunOrdinals.TreeInputTokens)) + (ownUsage?.InputTokens ?? 0),
            OutputTokens = (reader.IsDBNull(RunOrdinals.TreeOutputTokens) ? 0 : reader.GetInt64(RunOrdinals.TreeOutputTokens)) + (ownUsage?.OutputTokens ?? 0),
            TotalTokens = (reader.IsDBNull(RunOrdinals.TreeTotalTokens) ? 0 : reader.GetInt64(RunOrdinals.TreeTotalTokens)) + (ownUsage?.TotalTokens ?? 0),

            // 🚨 The tree cache/reasoning/audio ordinals use a NULL-PRESERVING
            // sum, unlike the three totals above: the SQL text deliberately
            // does NOT COALESCE them to zero, so "nobody in this tree reported
            // cache usage" survives as null instead of becoming an observed zero.
            CachedInputTokens = AddTreeCounter(reader, RunOrdinals.TreeCachedInputTokens, ownUsage?.CachedInputTokens),
            ReasoningTokens = AddTreeCounter(reader, RunOrdinals.TreeReasoningTokens, ownUsage?.ReasoningTokens),
            AudioInputTokens = AddTreeCounter(reader, RunOrdinals.TreeAudioInputTokens, ownUsage?.AudioInputTokens),
            AudioOutputTokens = AddTreeCounter(reader, RunOrdinals.TreeAudioOutputTokens, ownUsage?.AudioOutputTokens),
        };
    }

    /// <summary>Adds a descendant total to the record's own counter, staying null when NEITHER exists.</summary>
    private static long? AddTreeCounter(DbDataReader reader, int ordinal, long? own)
    {
        var descendants = reader.IsDBNull(ordinal) ? (long?)null : reader.GetInt64(ordinal);

        return descendants is null && own is null ? null : (descendants ?? 0) + (own ?? 0);
    }

    /// <remarks>
    /// The token breakdown ordinals participate in the "did the provider report
    /// anything at all" test below: a provider that reports ONLY a cache count
    /// still measured something, and returning <see langword="null"/> would
    /// throw that measurement away.
    /// </remarks>
    private static RunUsage? ReadUsage(DbDataReader reader)
    {
        if (reader.IsDBNull(RunOrdinals.InputTokens) && reader.IsDBNull(RunOrdinals.OutputTokens) && reader.IsDBNull(RunOrdinals.TotalTokens)
            && reader.IsDBNull(RunOrdinals.CachedInputTokens) && reader.IsDBNull(RunOrdinals.ReasoningTokens)
            && reader.IsDBNull(RunOrdinals.AudioInputTokens) && reader.IsDBNull(RunOrdinals.AudioOutputTokens))
        {
            return null;
        }

        return new RunUsage
        {
            InputTokens = reader.IsDBNull(RunOrdinals.InputTokens) ? null : reader.GetInt64(RunOrdinals.InputTokens),
            OutputTokens = reader.IsDBNull(RunOrdinals.OutputTokens) ? null : reader.GetInt64(RunOrdinals.OutputTokens),
            TotalTokens = reader.IsDBNull(RunOrdinals.TotalTokens) ? null : reader.GetInt64(RunOrdinals.TotalTokens),
            CachedInputTokens = reader.IsDBNull(RunOrdinals.CachedInputTokens) ? null : reader.GetInt64(RunOrdinals.CachedInputTokens),
            ReasoningTokens = reader.IsDBNull(RunOrdinals.ReasoningTokens) ? null : reader.GetInt64(RunOrdinals.ReasoningTokens),
            AudioInputTokens = reader.IsDBNull(RunOrdinals.AudioInputTokens) ? null : reader.GetInt64(RunOrdinals.AudioInputTokens),
            AudioOutputTokens = reader.IsDBNull(RunOrdinals.AudioOutputTokens) ? null : reader.GetInt64(RunOrdinals.AudioOutputTokens),
        };
    }

    /// <summary>
    /// Reads the record's own cost. A NULL <c>pricing_source</c> means the
    /// model was never known at all (different from
    /// <see cref="PricingSource.Unknown"/>): in that case it returns
    /// <see langword="null"/>.
    /// </summary>
    private static RunCost? ReadCost(DbDataReader reader)
    {
        if (reader.IsDBNull(RunOrdinals.PricingSource))
        {
            return null;
        }

        return new RunCost
        {
            InputCost = DbHelpers.GetNullableDecimal(reader, RunOrdinals.InputCost),
            InputPricePerMillionTokens = DbHelpers.GetNullableDecimal(reader, RunOrdinals.InputPricePerMillionTokens),
            OutputCost = DbHelpers.GetNullableDecimal(reader, RunOrdinals.OutputCost),
            OutputPricePerMillionTokens = DbHelpers.GetNullableDecimal(reader, RunOrdinals.OutputPricePerMillionTokens),

            // Appended in phase 68. NULL when no cache rate was
            // configured -- which is NOT the same as PricingSource.Unknown.
            CachedInputCost = DbHelpers.GetNullableDecimal(reader, RunOrdinals.CachedInputCost),
            CachedInputPricePerMillionTokens = DbHelpers.GetNullableDecimal(reader, RunOrdinals.CachedInputPricePerMillionTokens),
            Currency = DbHelpers.GetNullableString(reader, RunOrdinals.CostCurrency),
            Source = (PricingSource)reader.GetInt16(RunOrdinals.PricingSource),
        };
    }

    /// <summary>Combines the tree's cost total with the record's own cost.</summary>
    /// <remarks>Same rationale as <see cref="ReadTreeUsage"/>.</remarks>
    private static RunTreeCost? ReadTreeCost(DbDataReader reader, RunCost? ownCost)
    {
        var pricedDescendants = reader.IsDBNull(RunOrdinals.PricingRows) ? 0 : reader.GetInt64(RunOrdinals.PricingRows);

        if (pricedDescendants == 0 && ownCost is null)
        {
            return null;
        }

        var inputCost = DbHelpers.GetNullableDecimal(reader, RunOrdinals.TreeCostInput);
        var outputCost = DbHelpers.GetNullableDecimal(reader, RunOrdinals.TreeCostOutput);

        if (ownCost?.InputCost is { } ownInput)
        {
            inputCost = (inputCost ?? 0) + ownInput;
        }

        if (ownCost?.OutputCost is { } ownOutput)
        {
            outputCost = (outputCost ?? 0) + ownOutput;
        }

        var unknownPricing = (reader.IsDBNull(RunOrdinals.UnknownPricingRows) ? 0 : reader.GetInt64(RunOrdinals.UnknownPricingRows))
            + (ownCost?.Source == PricingSource.Unknown ? 1 : 0);

        // The descendants' cache charge: a THIRD addend of the tree total, not
        // a subset of InputCost -- every run's own InputCost already excludes
        // its cached tokens.
        var cachedInputCost = DbHelpers.GetNullableDecimal(reader, RunOrdinals.TreeCostCachedInput);

        if (ownCost?.CachedInputCost is { } ownCached)
        {
            cachedInputCost = (cachedInputCost ?? 0) + ownCached;
        }

        return new RunTreeCost
        {
            InputCost = inputCost,
            OutputCost = outputCost,
            CachedInputCost = cachedInputCost,
            Currency = DbHelpers.GetNullableString(reader, RunOrdinals.TreeCostCurrency) ?? ownCost?.Currency,
            RunsWithUnknownPricing = unknownPricing,
        };
    }

    private ToolInvocationRecord ReadToolInvocation(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            RunId = reader.GetGuid(1),
            ToolName = reader.GetString(2),
            ToolCallId = DbHelpers.GetNullableString(reader, 3),
            Source = DbHelpers.GetNullableString(reader, 4),
            Arguments = ProtectedValue.Read(_context, DbHelpers.GetNullableString(reader, 5)),
            Result = ProtectedValue.Read(_context, DbHelpers.GetNullableString(reader, 6)),
            Duration = reader.IsDBNull(7) ? null : TimeSpan.FromMilliseconds(reader.GetInt32(7)),
            Error = DbHelpers.GetNullableString(reader, 8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
            Usage = ReadToolCallUsage(reader),

            // Indexes 15-16 (Phase 69). SQLite has no boolean type and
            // returns long (0/1) instead (K-195), the same reason
            // ReadToolCallUsage below reads `usage_estimated` through
            // DbHelpers.ToBoolean rather than reader.GetBoolean.
            AuthorizationDenied = DbHelpers.ToBoolean(reader.GetValue(15)),
            TimedOut = DbHelpers.ToBoolean(reader.GetValue(16)),
        };

    /// <summary>
    /// Reads the non-token measurement columns (indexes 10-14).
    /// </summary>
    /// <remarks>
    /// If the unit is empty, the call reported no measurement at all and no
    /// object is constructed — returning an empty <see cref="ToolCallUsage"/>
    /// would mean "measured, but zero". The boolean value is read with
    /// <c>DbHelpers.ToBoolean</c>: SQLite has no boolean type and returns
    /// <c>long</c> (0/1) instead.
    /// </remarks>
    private static ToolCallUsage? ReadToolCallUsage(DbDataReader reader)
    {
        if (DbHelpers.GetNullableString(reader, 10) is not { Length: > 0 } unit)
        {
            return null;
        }

        return new ToolCallUsage
        {
            Unit = unit,
            Quantity = DbHelpers.GetNullableDecimal(reader, 11) ?? 0m,
            IsEstimated = !reader.IsDBNull(12) && DbHelpers.ToBoolean(reader.GetValue(12)),
            Cost = DbHelpers.GetNullableDecimal(reader, 13),
            Currency = DbHelpers.GetNullableString(reader, 14),
        };
    }

    private static ToolUsage ReadToolUsage(DbDataReader reader)
        => new()
        {
            ToolName = reader.GetString(0),
            TotalCalls = reader.GetInt64(1),
            FailedCalls = reader.GetInt64(2),
            AverageDurationMs = reader.IsDBNull(3) ? null : reader.GetDouble(3),
            LastCalledAt = reader.IsDBNull(4) ? null : DbHelpers.GetTimestamp(reader, 4),
        };

    private static TimeSeriesPoint ReadTimeSeriesPoint(DbDataReader reader)
        => new()
        {
            Bucket = DbHelpers.GetTimestamp(reader, 0),
            Runs = reader.GetInt64(1),
            FailedRuns = reader.GetInt64(2),
            InputTokens = reader.GetInt64(3),
            OutputTokens = reader.GetInt64(4),
            Cost = DbHelpers.GetNullableDecimal(reader, 5),
            AverageDurationMs = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        };

    private static ExperimentVariantResult ReadExperimentVariantResult(DbDataReader reader)
        => new()
        {
            Variant = reader.GetString(0),
            Version = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            TotalRuns = reader.GetInt64(2),
            CompletedRuns = reader.GetInt64(3),
            FailedRuns = reader.GetInt64(4),
            CanceledRuns = reader.GetInt64(5),
            InputTokens = reader.GetInt64(6),
            OutputTokens = reader.GetInt64(7),
            TotalTokens = reader.GetInt64(8),
            AverageDurationMs = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            TotalCost = DbHelpers.GetNullableDecimal(reader, 10),
            Currency = DbHelpers.GetNullableString(reader, 11),
            AverageScore = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        };

    /// <summary>
    /// Binds the label filter parameters.
    /// </summary>
    /// <remarks>
    /// A value with no key is DROPPED rather than applied: a value alone
    /// names no dimension, and every dialect's predicate is gated on the key
    /// being non-null, so leaving the value bound would look like an active
    /// filter that quietly does nothing. Same rationale as the parent/root
    /// contradiction in <see cref="RunQuery.ParentRunId"/>.
    /// </remarks>
    private void AddLabelFilter(DbCommand command, string? labelKey, string? labelValue)
    {
        var key = labelKey is { Length: > 0 } ? labelKey : null;

        Dialect.AddText(command, "label_key", key);
        Dialect.AddText(command, "label_value", key is null ? null : labelValue);
    }

    private void AddNullableText(DbCommand command, string name, string? value)
        => Dialect.AddText(command, name, value);

    private void AddNullableUuid(DbCommand command, string name, Guid? value)
        => Dialect.AddUuid(command, name, value);

    private void AddNullableInt64(DbCommand command, string name, long? value)
        => Dialect.AddInt64(command, name, value);

    private void AddNullableInt32(DbCommand command, string name, int? value)
        => Dialect.AddInt32(command, name, value);

    private void AddNullableDecimal(DbCommand command, string name, decimal? value)
        => Dialect.AddDecimal(command, name, value);
}
