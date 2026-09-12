namespace Tracon;

/// <summary>
/// Run lifecycle: start, completion, cost updates, heartbeat, orphan claim
/// and the <see cref="InMemoryRunStore.MaxRuns"/> trim.
/// </summary>
internal sealed partial class InMemoryRunStore
{
    /// <inheritdoc />
    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default)
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
            // 🚨 Set BELOW, after the upsert check: on the second StartRunAsync
            // of a queued run the caller (a background worker) usually does not
            // know the user, and overwriting would erase what the first write
            // got right. The SQL providers COALESCE for the same reason.
            UserId = info.UserId,
            Labels = info.Labels,
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

        // 🚨 Phase 46: for a queued run, this method is called TWICE with the
        // SAME identity (first Queued, then again when the worker moves it to
        // Running). This is an UPSERT: if the row already exists, do NOT
        // RESET the event/tool-invocation logs (even if no event has been
        // written yet), and do not enqueue it a SECOND time.
        var isNew = !_runs.TryGetValue(record.Id, out var previous);

        if (!isNew)
        {
            record = record with
            {
                UserId = record.UserId ?? previous!.UserId,
                Labels = record.Labels ?? previous!.Labels,
            };
        }

        _runs[record.Id] = record;

        if (isNew)
        {
            _events[record.Id] = [];
            _toolInvocations[record.Id] = [];
            _insertionOrder.Enqueue(record.Id);
        }

        TrimIfNeeded();

        return new ValueTask<RunRecord>(record);
    }

    /// <inheritdoc />
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completion);

        if (!_runs.TryGetValue(completion.RunId, out var existing))
        {
            throw new TraconException($"No run with id '{completion.RunId}' was found.");
        }

        // EXPECTED tenant check (K-355).
        EnsureExpectedTenant(completion.RunId, completion.TenantId, "The run was not completed.");

        _runs[completion.RunId] = existing with
        {
            Status = completion.Status,
            CompletedAt = completion.CompletedAt,
            EventCount = completion.EventCount,
            Usage = completion.Usage,
            Error = completion.Error,
            Cost = completion.Cost,
            ModelId = completion.ModelId ?? existing.ModelId,
            ModelProvider = completion.ModelProvider ?? existing.ModelProvider,
        };

        return default;
    }

    /// <inheritdoc />
    public ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // If the run was dropped (MaxRuns), the call is silently discarded:
        // this is a maintenance cost and must not interrupt the run.
        if (_runs.TryGetValue(runId, out var existing))
        {
            // If the EXPECTED tenant does not match, the write is SILENTLY
            // skipped — on the SQL side too, the WHERE condition updates zero
            // rows and no error is thrown (K-355).
            if (tenantId is not null
                && !string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
            {
                return default;
            }

            _runs[runId] = existing with { Cost = cost };
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask TouchHeartbeatAsync(
        IReadOnlyCollection<Guid> runIds,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        foreach (var runId in runIds)
        {
            // Meaningful only for Running rows; an identity that does not
            // exist or is in another status is silently skipped (a maintenance signal).
            if (_runs.TryGetValue(runId, out var run) && run.Status == RunStatus.Running)
            {
                _heartbeats[runId] = at;
            }
        }

        return default;
    }

    /// <summary>
    /// The clustering fingerprint for orphaned-run errors. See the rationale
    /// at the usage site.
    /// </summary>
    private const string OrphanedFingerprint = "orphaned";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var candidates = _runs.Values
            .Where(record => record.Status == RunStatus.Running)
            .Select(record => (Record: record, LastSeen: _heartbeats.TryGetValue(record.Id, out var hb) ? hb : record.StartedAt))
            .Where(candidate => candidate.LastSeen < staleBefore)
            .OrderBy(static candidate => candidate.LastSeen)
            .Take(Math.Max(max, 0))
            .ToList();

        var claimed = new List<RunRecord>(candidates.Count);

        foreach (var (record, lastSeen) in candidates)
        {
            var message = $"The process running the run is not responding; last heartbeat: {lastSeen:O}.";
            var error = new RunError
            {
                Type = "orphaned",
                Message = message,
                Class = RunErrorClass.Infrastructure,

                // A fixed string -- NOT a SHA-256 hash. Every orphaned run is
                // the SAME failure (the process is not responding); there is
                // no need for ErrorFingerprint.Compute to normalize the
                // variable timestamp in the message text, and the SQL stores
                // (a separate assembly, which cannot reach ErrorFingerprint)
                // use this constant verbatim -- behavioral equality between
                // the two implementations is achieved SIMPLY this way.
                Fingerprint = OrphanedFingerprint,
            };

            var updated = record with
            {
                Status = RunStatus.Failed,
                CompletedAt = now,
                Error = error,
                EventCount = record.EventCount + 1,
            };

            // Another path may have modified the record at the same time (for
            // example, the run completed normally right at this moment);
            // even though such a race is very unlikely, TryUpdate provides
            // atomic protection -- if missed, the row is retried on the next pass.
            if (!_runs.TryUpdate(record.Id, updated, record))
            {
                continue;
            }

            _heartbeats.TryRemove(record.Id, out _);

            if (_events.TryGetValue(record.Id, out var log))
            {
                lock (log)
                {
                    log.Add(new RunEvent
                    {
                        RunId = record.Id,
                        Sequence = log.Count,
                        Type = RunEventType.RunFailed,
                        Timestamp = now,
                        Text = message,
                    });
                }
            }

            claimed.Add(updated);
        }

        return new ValueTask<IReadOnlyList<RunRecord>>(claimed);
    }

    private void TrimIfNeeded()
    {
        while (_runs.Count > MaxRuns && _insertionOrder.TryDequeue(out var oldest))
        {
            _runs.TryRemove(oldest, out _);
            _events.TryRemove(oldest, out _);
            _toolInvocations.TryRemove(oldest, out _);
            _heartbeats.TryRemove(oldest, out _);
        }
    }
}
