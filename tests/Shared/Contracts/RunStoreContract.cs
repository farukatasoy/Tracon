
namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IRunStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the PostgreSQL store must pass the same scenarios.
/// </remarks>
public abstract class RunStoreContract : TenantIsolationContract<IRunStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// The tenant is not a parameter on the interface; it is read from
    /// <see cref="ITenantContext"/>. Each hook therefore sets the current tenant first.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId, name));
        await Store.AppendEventAsync(TestData.Event(runId, 0));

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = (Guid)key;
        var record = await Store.GetRunAsync(runId);

        // The event stream must carry the same boundary; if the record is not
        // visible, its events must not be visible either.
        var events = 0;

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            _ = runEvent;
            events++;
        }

        (events > 0).ShouldBe(record is not null);

        return record is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.QueryRunsAsync(new RunQuery())).Count;
    }

    [Fact]
    public async Task StartRunAsync_upserts_when_called_twice_with_the_same_id()
    {
        // Phase 46: a queued run is first written as Queued, then written again
        // with the SAME id (default Running) once the worker actually picks up
        // the job. The second call must NOT open a new row -- it must UPDATE
        // the existing row.
        var runId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(runId) with { Status = RunStatus.Queued });

        var queued = await Store.GetRunAsync(runId);
        queued.ShouldNotBeNull();
        queued!.Status.ShouldBe(RunStatus.Queued);

        await Store.StartRunAsync(TestData.Run(runId));

        var running = await Store.GetRunAsync(runId);
        running.ShouldNotBeNull();
        running!.Status.ShouldBe(RunStatus.Running);

        (await Store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
            .Count(record => record.Id == runId).ShouldBe(1);
    }

    [Fact]
    public async Task Events_are_read_in_sequence_order()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        for (var i = 0; i < 5; i++)
        {
            await Store.AppendEventAsync(TestData.Event(runId, i));
        }

        var sequences = new List<long>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId, fromSequence: 2))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public async Task Event_fields_round_trip()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.ToolInvoking,
            Timestamp = DateTimeOffset.UtcNow,
            Text = "text",
            ToolName = "get_order_status",
            ToolCallId = "call-1",
            Payload = "orderId=42",
        });

        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        var single = events.ShouldHaveSingleItem();
        single.Type.ShouldBe(RunEventType.ToolInvoking);
        single.Text.ShouldBe("text");
        single.ToolName.ShouldBe("get_order_status");
        single.ToolCallId.ShouldBe("call-1");
        single.Payload.ShouldBe("orderId=42");
    }

    [Fact]
    public async Task Event_cannot_be_appended_to_a_nonexistent_run()
        => await Should.ThrowAsync<AgentPrismException>(
            async () => await Store.AppendEventAsync(TestData.Event(AgentPrismId.NewId(), 0)));

    [Fact]
    public async Task Nonexistent_run_cannot_be_completed()
        => await Should.ThrowAsync<AgentPrismException>(
            async () => await Store.CompleteRunAsync(new RunCompletion
            {
                RunId = AgentPrismId.NewId(),
                Status = RunStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            }));

    [Fact]
    public async Task Nonexistent_run_returns_empty_events()
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(AgentPrismId.NewId()))
        {
            events.Add(runEvent);
        }

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Completion_updates_the_summary()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var completedAt = DateTimeOffset.UtcNow;

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = completedAt,
            EventCount = 7,
            Usage = new RunUsage { InputTokens = 10, OutputTokens = 20, TotalTokens = 30 },
            Error = new RunError { Type = "System.InvalidOperationException", Message = "boom" },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Status.ShouldBe(RunStatus.Failed);
        record.EventCount.ShouldBe(7);
        record.CompletedAt.ShouldNotBeNull();
        record.Usage.ShouldNotBeNull();
        record.Usage.InputTokens.ShouldBe(10);
        record.Usage.OutputTokens.ShouldBe(20);
        record.Usage.TotalTokens.ShouldBe(30);
        record.Error.ShouldNotBeNull();
        record.Error.Type.ShouldBe("System.InvalidOperationException");
        record.Error.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task Completion_overrides_the_model_id_when_a_fallback_answered()
    {
        // Phase 62, F-44: runs.model_id is written at start from the PRIMARY
        // binding, before it is known whether a ModelBinding.Fallbacks link
        // will answer instead; CompleteRunAsync corrects it so
        // GetStatisticsAsync's ByModel breakdown groups by the model that
        // really ran.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "primary-model" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            ModelId = "fallback-model",
        });

        (await Store.GetRunAsync(runId))!.ModelId.ShouldBe("fallback-model");
    }

    [Fact]
    public async Task Completion_leaves_the_model_id_unchanged_when_no_override_is_given()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "primary-model" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        (await Store.GetRunAsync(runId))!.ModelId.ShouldBe("primary-model");
    }

    [Fact]
    public async Task Completion_stores_error_class_and_fingerprint()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError
            {
                Type = "content_filtered",
                Message = "response filtered",
                Class = RunErrorClass.ContentFiltered,
                Fingerprint = "abc123",
            },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Error.ShouldNotBeNull();
        record.Error.Class.ShouldBe(RunErrorClass.ContentFiltered);
        record.Error.Fingerprint.ShouldBe("abc123");
    }

    [Fact]
    public async Task Token_info_stays_null_when_absent()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        (await Store.GetRunAsync(runId))!.Usage.ShouldBeNull();
    }

    // ---------------------------------------------------------------------
    // Phase 68 -- run attribution and the token breakdown.
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Attribution_round_trips()
    {
        var runId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(runId) with
        {
            UserId = "user-1",
            Labels = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["team"] = "payments",
                ["ticket"] = "OPS-1",
            },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.UserId.ShouldBe("user-1");
        record.Labels.ShouldNotBeNull();
        record.Labels.Count.ShouldBe(2);
        record.Labels["team"].ShouldBe("payments");
        record.Labels["ticket"].ShouldBe("OPS-1");
    }

    [Fact]
    public async Task A_run_without_attribution_reads_back_as_null_not_as_an_empty_map()
    {
        // The default behaviour of an application that registers no
        // IRunAttributionContext. An empty dictionary would make "no labels" and
        // "labels were considered and there were none" indistinguishable.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.UserId.ShouldBeNull();
        record.Labels.ShouldBeNull();
    }

    [Fact]
    public async Task A_second_start_without_attribution_does_not_erase_the_first_one()
    {
        // 🚨 The queued-run path: the placeholder row is written inside the HTTP
        // request where the user IS known, then rewritten by a background worker
        // where it is NOT. A plain overwrite would erase it.
        var runId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(runId) with
        {
            Status = RunStatus.Queued,
            UserId = "user-1",
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" },
        });

        await Store.StartRunAsync(TestData.Run(runId));

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.UserId.ShouldBe("user-1");
        record.Labels.ShouldNotBeNull();
        record.Labels["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task Query_filters_by_user()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { UserId = "ada" });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { UserId = "grace" });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()));

        var records = await Store.QueryRunsAsync(new RunQuery { UserId = "ada" });

        records.Count.ShouldBe(1);
        records[0].UserId.ShouldBe("ada");
    }

    [Fact]
    public async Task Query_filters_by_a_label_key_and_value_together()
    {
        // 🚨 The key alone is not the filter: asking for team=payments must not
        // return team=billing.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" },
        });

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "billing" },
        });

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()));

        var records = await Store.QueryRunsAsync(new RunQuery { LabelKey = "team", LabelValue = "payments" });

        records.Count.ShouldBe(1);
        records[0].Labels!["team"].ShouldBe("payments");
    }

    [Fact]
    public async Task A_label_key_without_a_value_matches_any_value_of_that_key()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" },
        });

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "billing" },
        });

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with
        {
            Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["env"] = "prod" },
        });

        var records = await Store.QueryRunsAsync(new RunQuery { LabelKey = "team" });

        records.Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_token_breakdown_round_trips_and_stays_null_when_it_was_never_reported()
    {
        var withBreakdown = AgentPrismId.NewId();
        var withoutBreakdown = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(withBreakdown));
        await Store.StartRunAsync(TestData.Run(withoutBreakdown));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = withBreakdown,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                CachedInputTokens = 40,
                ReasoningTokens = 30,
                AudioInputTokens = 20,
                AudioOutputTokens = 10,
            },
        });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = withoutBreakdown,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 },
        });

        var reported = (await Store.GetRunAsync(withBreakdown))!.Usage.ShouldNotBeNull();
        reported.CachedInputTokens.ShouldBe(40);
        reported.ReasoningTokens.ShouldBe(30);
        reported.AudioInputTokens.ShouldBe(20);
        reported.AudioOutputTokens.ShouldBe(10);

        // 🚨 The load-bearing half: NOT reported must read back as null, never 0.
        var silent = (await Store.GetRunAsync(withoutBreakdown))!.Usage.ShouldNotBeNull();
        silent.CachedInputTokens.ShouldBeNull();
        silent.ReasoningTokens.ShouldBeNull();
        silent.AudioInputTokens.ShouldBeNull();
        silent.AudioOutputTokens.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_that_reports_only_a_cache_count_still_produces_a_usage_record()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { CachedInputTokens = 0 },
        });

        var usage = (await Store.GetRunAsync(runId))!.Usage.ShouldNotBeNull();
        usage.CachedInputTokens.ShouldBe(0);
        usage.InputTokens.ShouldBeNull();
    }

    [Fact]
    public async Task The_cache_charge_round_trips_and_counts_toward_the_total()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "m" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 },
            Cost = new RunCost
            {
                InputCost = 1m,
                OutputCost = 2m,
                CachedInputCost = 0.5m,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Cost.ShouldNotBeNull();
        record.Cost.CachedInputCost.ShouldBe(0.5m);

        // 🚨 The tree total must include the cache charge as a THIRD addend.
        // Omitting it under-reports every tree that hit the prompt cache.
        record.TreeCost.ShouldNotBeNull();
        record.TreeCost.CachedInputCost.ShouldBe(0.5m);

        var statistics = await Store.GetStatisticsAsync(new RunStatisticsQuery());
        statistics.TotalCost.ShouldBe(3.5m);
    }

    [Fact]
    public async Task Statistics_break_down_by_user()
    {
        await CompleteRunWithAttributionAsync("ada", labels: null, totalTokens: 10, failed: false);
        await CompleteRunWithAttributionAsync("ada", labels: null, totalTokens: 20, failed: true);
        await CompleteRunWithAttributionAsync("grace", labels: null, totalTokens: 5, failed: false);
        await CompleteRunWithAttributionAsync(userId: null, labels: null, totalTokens: 7, failed: false);

        var statistics = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        // Runs with no user stay OUT of the breakdown but stay IN the totals.
        statistics.TotalRuns.ShouldBe(4);
        statistics.TotalTokens.ShouldBe(42);

        statistics.ByUser.Count.ShouldBe(2);

        var ada = statistics.ByUser.Single(user => string.Equals(user.UserId, "ada", StringComparison.Ordinal));
        ada.TotalRuns.ShouldBe(2);
        ada.FailedRuns.ShouldBe(1);
        ada.TotalTokens.ShouldBe(30);
    }

    [Fact]
    public async Task Statistics_break_down_by_label_and_the_rows_do_not_partition_the_runs()
    {
        // 🚨 One run carrying two labels contributes to TWO rows. The rows
        // therefore do NOT sum to TotalRuns, unlike every other breakdown.
        await CompleteRunWithAttributionAsync(
            userId: null,
            labels: new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments", ["env"] = "prod" },
            totalTokens: 10,
            failed: false);

        await CompleteRunWithAttributionAsync(
            userId: null,
            labels: new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" },
            totalTokens: 20,
            failed: true);

        var statistics = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        statistics.TotalRuns.ShouldBe(2);
        statistics.ByLabel.Count.ShouldBe(2);

        var team = statistics.ByLabel.Single(label =>
            string.Equals(label.Key, "team", StringComparison.Ordinal));

        team.Value.ShouldBe("payments");
        team.TotalRuns.ShouldBe(2);
        team.FailedRuns.ShouldBe(1);
        team.TotalTokens.ShouldBe(30);

        statistics.ByLabel.Sum(static label => label.TotalRuns).ShouldBeGreaterThan(statistics.TotalRuns);
    }

    [Fact]
    public async Task Statistics_narrow_to_one_user_when_asked()
    {
        await CompleteRunWithAttributionAsync("ada", labels: null, totalTokens: 10, failed: false);
        await CompleteRunWithAttributionAsync("grace", labels: null, totalTokens: 20, failed: false);

        var statistics = await Store.GetStatisticsAsync(new RunStatisticsQuery { UserId = "ada" });

        statistics.TotalRuns.ShouldBe(1);
        statistics.TotalTokens.ShouldBe(10);
        statistics.ByUser.ShouldHaveSingleItem().UserId.ShouldBe("ada");
    }

    [Fact]
    public async Task Statistics_narrow_to_one_label_when_asked()
    {
        await CompleteRunWithAttributionAsync(
            userId: null,
            labels: new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" },
            totalTokens: 10,
            failed: false);

        await CompleteRunWithAttributionAsync(
            userId: null,
            labels: new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "billing" },
            totalTokens: 20,
            failed: false);

        var statistics = await Store.GetStatisticsAsync(
            new RunStatisticsQuery { LabelKey = "team", LabelValue = "payments" });

        statistics.TotalRuns.ShouldBe(1);
        statistics.TotalTokens.ShouldBe(10);
    }

    [Fact]
    public async Task Statistics_total_the_token_breakdown_beside_the_input_and_output_totals()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                CachedInputTokens = 40,
                ReasoningTokens = 30,
            },
        });

        var statistics = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        statistics.InputTokens.ShouldBe(100);
        statistics.OutputTokens.ShouldBe(50);

        // Counted INSIDE the two totals above; a caller that adds them double counts.
        statistics.CachedInputTokens.ShouldBe(40);
        statistics.ReasoningTokens.ShouldBe(30);
    }

    [Fact]
    public async Task Tree_usage_sums_the_breakdown_across_child_runs()
    {
        // 🚨 The phase 20 lesson applied to four new fields at once: a field can
        // be added to the record, written to the row, read back correctly, and
        // STILL be dropped by the tree aggregation. This is the only test that
        // looks at that specific seam.
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId) with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = rootId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                CachedInputTokens = 40,
                ReasoningTokens = 5,
            },
        });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = childId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage
            {
                InputTokens = 10,
                OutputTokens = 5,
                TotalTokens = 15,
                CachedInputTokens = 3,
            },
        });

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.ChildRunCount.ShouldBe(1);

        root.TreeUsage.ShouldNotBeNull();
        root.TreeUsage.InputTokens.ShouldBe(110);
        root.TreeUsage.CachedInputTokens.ShouldBe(43);

        // Only the root reported reasoning tokens; a child reporting none must
        // not reset the tree total to null.
        root.TreeUsage.ReasoningTokens.ShouldBe(5);

        // Neither run reported audio, so the tree must say "not measured".
        root.TreeUsage.AudioInputTokens.ShouldBeNull();
        root.TreeUsage.AudioOutputTokens.ShouldBeNull();
    }

    [Fact]
    public async Task Tree_cost_adds_the_cache_charge_of_every_run_in_the_tree()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId) with { ModelId = "m" });
        await Store.StartRunAsync(TestData.Run(childId) with
        {
            ModelId = "m",
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteWithCostAsync(rootId, input: 1m, output: 2m, cached: 0.5m);
        await CompleteWithCostAsync(childId, input: 0.25m, output: 0.25m, cached: 0.25m);

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.TreeCost.ShouldNotBeNull();
        root.TreeCost.InputCost.ShouldBe(1.25m);
        root.TreeCost.OutputCost.ShouldBe(2.25m);

        // 🚨 The third addend. Its absence would silently under-report the tree.
        root.TreeCost.CachedInputCost.ShouldBe(0.75m);
    }

    private async Task CompleteWithCostAsync(Guid runId, decimal input, decimal output, decimal cached)
        => await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 1, OutputTokens = 1, TotalTokens = 2 },
            Cost = new RunCost
            {
                InputCost = input,
                OutputCost = output,
                CachedInputCost = cached,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

    [Fact]
    public async Task Every_cost_total_includes_the_cache_charge()
    {
        // 🚨 The seam an independent audit found open: `cached_input_cost` had
        // been added to the summary and the tree but NOT to the time series or
        // the experiment results, so the SAME cost answered differently
        // depending on which query — and which store — you asked. One test,
        // every total, all four stores.
        var experimentId = AgentPrismId.NewId();
        var runId = AgentPrismId.NewId();
        var startedAt = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(runId) with
        {
            ModelId = "m",
            StartedAt = startedAt,
            ExperimentId = experimentId,
            Variant = "a",
        });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = startedAt.AddSeconds(1),
            Usage = new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 },
            Cost = new RunCost
            {
                InputCost = 1m,
                OutputCost = 2m,
                CachedInputCost = 0.5m,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

        // A two-term sum would answer 3 everywhere below; the right answer is 3.5.
        const decimal Expected = 3.5m;

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).TotalCost.ShouldBe(Expected);

        var points = await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery
        {
            From = startedAt.AddHours(-1),
            To = startedAt.AddHours(1),
            Bucket = TimeSeriesBucket.Hour,
        });

        points.Sum(static point => point.Cost ?? 0m).ShouldBe(Expected);

        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId });

        results.ShouldHaveSingleItem().TotalCost.ShouldBe(Expected);

        var record = await Store.GetRunAsync(runId);
        record.ShouldNotBeNull();
        record.Cost!.Total().ShouldBe(Expected);
        record.TreeCost!.Total().ShouldBe(Expected);
    }

    private async Task CompleteRunWithAttributionAsync(
        string? userId,
        IReadOnlyDictionary<string, string>? labels,
        long totalTokens,
        bool failed)
    {
        var runId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(runId) with { UserId = userId, Labels = labels });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = failed ? RunStatus.Failed : RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { TotalTokens = totalTokens },
            Error = failed ? new RunError { Type = "T", Message = "m" } : null,
        });
    }

    [Fact]
    public async Task Query_filters_by_agent_name()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "beta"));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var results = await Store.QueryRunsAsync(new RunQuery { AgentName = "alpha" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static run => run.AgentName == "alpha");
    }

    [Fact]
    public async Task Query_filters_by_status()
    {
        var completed = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(completed));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = completed,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var results = await Store.QueryRunsAsync(new RunQuery { Status = RunStatus.Completed });

        results.ShouldHaveSingleItem().Id.ShouldBe(completed);
    }

    [Fact]
    public async Task Query_sorts_newest_first()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-10) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-5) });

        var results = await Store.QueryRunsAsync(new RunQuery());

        results.Count.ShouldBe(3);
        results[0].StartedAt.ShouldBe(now, TimeSpan.FromMilliseconds(1));
        results[^1].StartedAt.ShouldBe(now.AddMinutes(-10), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Query_applies_paging()
    {
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-i) });
        }

        var page = await Store.QueryRunsAsync(new RunQuery { Skip = 1, Take = 2 });

        page.Count.ShouldBe(2);
        page[0].StartedAt.ShouldBe(now.AddMinutes(-1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Query_filters_by_start_time()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-30) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = now });

        var results = await Store.QueryRunsAsync(new RunQuery { StartedAfter = now.AddMinutes(-10) });

        results.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Query_filters_by_error_type()
    {
        // HATA-S3-007: before RunQuery.ErrorType was added, this filter was
        // silently ignored and ALL runs were returned.
        var blockedId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(blockedId));
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = blockedId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "content_blocked", Message = "blocked" },
        });

        var otherId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(otherId));
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = otherId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "upstream_error", Message = "provider error" },
        });

        var results = await Store.QueryRunsAsync(new RunQuery { ErrorType = "content_blocked" });

        results.ShouldHaveSingleItem().Id.ShouldBe(blockedId);
    }

    [Fact]
    public async Task Nonexistent_run_returns_null()
        => (await Store.GetRunAsync(AgentPrismId.NewId())).ShouldBeNull();

    [Fact]
    public async Task Summary_aggregates_statuses_and_tokens()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 });
        await CompleteRunAsync("alpha", RunStatus.Failed, new RunUsage { InputTokens = 2, OutputTokens = 1, TotalTokens = 3 });
        await CompleteRunAsync("beta", RunStatus.Canceled, usage: null);
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "beta"));

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalRuns.ShouldBe(4);
        stats.CompletedRuns.ShouldBe(1);
        stats.FailedRuns.ShouldBe(1);
        stats.CanceledRuns.ShouldBe(1);
        stats.RunningRuns.ShouldBe(1);
        stats.InputTokens.ShouldBe(12);
        stats.OutputTokens.ShouldBe(6);
        stats.TotalTokens.ShouldBe(18);
    }

    [Fact]
    public async Task Summary_excludes_eval_runs()
    {
        // Eval case runs are synthetic test calls; they must not pollute
        // normal statistics (docs/arsiv/fazlar/18-DEGERLENDIRME.md, open question 4).
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);

        var evalRunId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(evalRunId, "alpha") with { Kind = RunKind.Eval });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = evalRunId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 1_000, OutputTokens = 1_000, TotalTokens = 2_000 },
        });

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalRuns.ShouldBe(1);
        stats.CompletedRuns.ShouldBe(1);
        stats.TotalTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Summary_computes_error_rate_only_over_completed_runs()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);
        await CompleteRunAsync("alpha", RunStatus.Failed, usage: null);

        // A still-running run must not enter the denominator.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ErrorRate.ShouldNotBeNull();
        stats.ErrorRate.Value.ShouldBe(0.5, 0.0001);
    }

    [Fact]
    public async Task Summary_returns_no_error_rate_when_no_run_has_completed()
    {
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).ErrorRate.ShouldBeNull();
    }

    [Fact]
    public async Task Summary_computes_breakdown_by_error_class()
    {
        // Two different clusters in the same class: "fp-a" occurs twice, "fp-b" occurs once.
        await FailedRunAsync(RunErrorClass.ContentFiltered, "fp-a", "message a");
        await FailedRunAsync(RunErrorClass.ContentFiltered, "fp-a", "message a");
        await FailedRunAsync(RunErrorClass.ContentFiltered, "fp-b", "message b");
        await FailedRunAsync(RunErrorClass.Timeout, "fp-c", "message c");

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());
        var byClass = stats.ByErrorClass.ToDictionary(static entry => entry.Class);

        byClass[RunErrorClass.ContentFiltered].TotalRuns.ShouldBe(3);
        byClass[RunErrorClass.ContentFiltered].TopClusters.Count.ShouldBe(2);

        // The most frequent cluster (fp-a, 2 runs) comes first.
        byClass[RunErrorClass.ContentFiltered].TopClusters[0].Fingerprint.ShouldBe("fp-a");
        byClass[RunErrorClass.ContentFiltered].TopClusters[0].Count.ShouldBe(2);
        byClass[RunErrorClass.ContentFiltered].TopClusters[1].Fingerprint.ShouldBe("fp-b");
        byClass[RunErrorClass.ContentFiltered].TopClusters[1].Count.ShouldBe(1);

        byClass[RunErrorClass.Timeout].TotalRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Summary_returns_at_most_three_failure_clusters_per_class()
    {
        for (var i = 0; i < 5; i++)
        {
            await FailedRunAsync(RunErrorClass.ProviderError, $"fp-{i}", $"message {i}");
        }

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());
        var providerError = stats.ByErrorClass.Single(static entry => entry.Class == RunErrorClass.ProviderError);

        providerError.TotalRuns.ShouldBe(5);
        providerError.TopClusters.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Summary_rows_with_no_error_class_appear_in_the_unknown_bucket()
    {
        // Simulates a row written before the error class was added: Class and
        // Fingerprint are DELIBERATELY empty (K-014 -- historical records are
        // not backfilled). The query must not crash, and the row must appear
        // in the Unknown bucket.
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "AgentPrism.AgentPrismCompilationException", Message = "legacy record" },
        });

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByErrorClass.ShouldContain(static entry => entry.Class == RunErrorClass.Unknown && entry.TotalRuns == 1);
    }

    [Fact]
    public async Task Summary_sorts_agent_breakdown_by_run_count()
    {
        await CompleteRunAsync("low-usage", RunStatus.Completed, usage: null);
        await CompleteRunAsync("high-usage", RunStatus.Completed, new RunUsage { TotalTokens = 100 });
        await CompleteRunAsync("high-usage", RunStatus.Failed, new RunUsage { TotalTokens = 50 });

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByAgent.Count.ShouldBe(2);
        stats.ByAgent[0].AgentName.ShouldBe("high-usage");
        stats.ByAgent[0].TotalRuns.ShouldBe(2);
        stats.ByAgent[0].FailedRuns.ShouldBe(1);
        stats.ByAgent[0].TotalTokens.ShouldBe(150);
        stats.ByAgent[1].AgentName.ShouldBe("low-usage");
    }

    [Fact]
    public async Task Summary_filters_by_agent_name()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);
        await CompleteRunAsync("beta", RunStatus.Completed, usage: null);

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery { AgentName = "alpha" });

        stats.TotalRuns.ShouldBe(1);
        stats.ByAgent.ShouldHaveSingleItem().AgentName.ShouldBe("alpha");
    }

    [Fact]
    public async Task Summary_filters_by_start_time()
    {
        var now = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha") with { StartedAt = now.AddHours(-2) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha") with { StartedAt = now });

        var stats = await Store.GetStatisticsAsync(
            new RunStatisticsQuery { StartedAfter = now.AddHours(-1) });

        stats.TotalRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Summary_limits_agent_breakdown()
    {
        await CompleteRunAsync("alpha", RunStatus.Completed, usage: null);
        await CompleteRunAsync("beta", RunStatus.Completed, usage: null);
        await CompleteRunAsync("gamma", RunStatus.Completed, usage: null);

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery { MaxAgents = 2 });

        stats.TotalRuns.ShouldBe(3);
        stats.ByAgent.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Empty_store_summary_returns_zero()
    {
        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalRuns.ShouldBe(0);
        stats.TotalTokens.ShouldBe(0);
        stats.ByAgent.ShouldBeEmpty();
        stats.ErrorRate.ShouldBeNull();
    }

    // --- Version and experiment breakdown (Phase 19.2-19.3) ---

    [Fact]
    public async Task Summary_computes_version_breakdown()
    {
        var v1 = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v1, "alpha") with { AgentVersion = 1 });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = v1,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { TotalTokens = 10 },
        });

        var v2A = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v2A, "alpha") with { AgentVersion = 2 });
        await Store.CompleteRunAsync(new RunCompletion { RunId = v2A, Status = RunStatus.Failed, CompletedAt = DateTimeOffset.UtcNow });

        var v2B = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v2B, "alpha") with { AgentVersion = 2 });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = v2B,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { TotalTokens = 20 },
        });

        // A run with an unknown version must not enter the breakdown.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha"));

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.ByVersion.Count.ShouldBe(2);

        var version1 = stats.ByVersion.Single(static v => v.Version == 1);
        version1.AgentName.ShouldBe("alpha");
        version1.TotalRuns.ShouldBe(1);
        version1.FailedRuns.ShouldBe(0);
        version1.TotalTokens.ShouldBe(10);

        var version2 = stats.ByVersion.Single(static v => v.Version == 2);
        version2.TotalRuns.ShouldBe(2);
        version2.FailedRuns.ShouldBe(1);
        version2.TotalTokens.ShouldBe(20);
    }

    [Fact]
    public async Task Experiment_results_are_summarized_per_arm()
    {
        var experimentId = Guid.NewGuid();

        var controlRun = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(controlRun, "alpha") with
        {
            AgentVersion = 1,
            ExperimentId = experimentId,
            Variant = "control",
        });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = controlRun,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 5, OutputTokens = 5, TotalTokens = 10 },
        });

        var v2Run = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(v2Run, "alpha") with
        {
            AgentVersion = 2,
            ExperimentId = experimentId,
            Variant = "v2",
        });
        await Store.CompleteRunAsync(new RunCompletion { RunId = v2Run, Status = RunStatus.Failed, CompletedAt = DateTimeOffset.UtcNow });

        // A run from another experiment must not enter this experiment's results.
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "alpha") with
        {
            ExperimentId = Guid.NewGuid(),
            Variant = "control",
        });

        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId });

        results.Count.ShouldBe(2);

        var control = results.Single(static r => string.Equals(r.Variant, "control", StringComparison.Ordinal));
        control.Version.ShouldBe(1);
        control.TotalRuns.ShouldBe(1);
        control.CompletedRuns.ShouldBe(1);
        control.TotalTokens.ShouldBe(10);
        control.AverageDurationMs.ShouldNotBeNull();

        var v2 = results.Single(static r => string.Equals(r.Variant, "v2", StringComparison.Ordinal));
        v2.Version.ShouldBe(2);
        v2.FailedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task Experiment_results_return_empty_for_an_experiment_with_no_traffic()
    {
        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = Guid.NewGuid() });

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tree_fields_round_trip()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "researcher") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        var child = await Store.GetRunAsync(childId);

        child.ShouldNotBeNull();
        child.ParentRunId.ShouldBe(rootId);
        child.RootRunId.ShouldBe(rootId);
        child.Depth.ShouldBe(1);

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.ParentRunId.ShouldBeNull();

        // The root record's root_run_id field stays EMPTY. Writing a value that
        // points the root at itself would turn the "is it a root or a child"
        // question into a second condition in every query.
        root.RootRunId.ShouldBeNull();
        root.Depth.ShouldBe(0);
        root.ChildRunCount.ShouldBe(1);
    }

    [Fact]
    public async Task Workflow_run_lives_in_the_same_table()
    {
        // There is NO SEPARATE TABLE for workflow runs (Phase 15). The
        // distinction is made with the `kind` column, and the agents inside a
        // workflow are attached under the same row using Phase 12's tree
        // mechanism.
        var workflowRunId = AgentPrismId.NewId();
        var agentRunId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(workflowRunId, "review") with
        {
            Kind = RunKind.Workflow,
            WorkflowName = "review",
        });

        await Store.StartRunAsync(TestData.Run(agentRunId, "writer") with
        {
            ParentRunId = workflowRunId,
            RootRunId = workflowRunId,
            Depth = 1,
        });

        var workflowRun = await Store.GetRunAsync(workflowRunId);

        workflowRun.ShouldNotBeNull();
        workflowRun.Kind.ShouldBe(RunKind.Workflow);
        workflowRun.WorkflowName.ShouldBe("review");
        workflowRun.ChildRunCount.ShouldBe(1);

        // Agent rows keep the default kind; legacy records are read the same way.
        var agentRun = await Store.GetRunAsync(agentRunId);

        agentRun.ShouldNotBeNull();
        agentRun.Kind.ShouldBe(RunKind.Agent);
        agentRun.WorkflowName.ShouldBeNull();
    }

    [Fact]
    public async Task List_returns_only_root_runs_by_default()
    {
        var rootId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId(), "researcher") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        var roots = await Store.QueryRunsAsync(new RunQuery());

        roots.ShouldHaveSingleItem().Id.ShouldBe(rootId);

        var all = await Store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false });

        all.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Query_session_filter_with_includeChildren_also_returns_child_runs()
    {
        // HATA-S2-001: SessionId is set only on the ROOT run (K-217); a child
        // run's own SessionId is NULL. Direct equality, when combined with
        // "includeChildren=true", used to match no child runs at all.
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();
        var yabanciRootId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId) with { SessionId = "session-1" });
        await Store.StartRunAsync(TestData.Run(childId, "researcher") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });
        await Store.StartRunAsync(TestData.Run(yabanciRootId) with { SessionId = "session-2" });

        var rootOnly = await Store.QueryRunsAsync(new RunQuery { SessionId = "session-1" });

        rootOnly.ShouldHaveSingleItem().Id.ShouldBe(rootId);

        var withChildren = await Store.QueryRunsAsync(new RunQuery { SessionId = "session-1", OnlyRootRuns = false });

        withChildren.Select(static run => run.Id).ShouldBe([rootId, childId], ignoreOrder: true);
    }

    [Fact]
    public async Task Parent_filter_overrides_root_filter()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "researcher") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        // OnlyRootRuns defaults to true; it is deliberately ignored when a
        // parent filter is given. Silently returning an empty list would be a
        // hard-to-debug behavior.
        var children = await Store.QueryRunsAsync(new RunQuery { ParentRunId = rootId });

        children.ShouldHaveSingleItem().Id.ShouldBe(childId);
    }

    [Fact]
    public async Task Tree_query_returns_the_root_and_all_its_descendants()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();
        var grandChildId = AgentPrismId.NewId();
        var yabanciId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "researcher") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });
        await Store.StartRunAsync(TestData.Run(grandChildId, "summarizer") with
        {
            ParentRunId = childId,
            RootRunId = rootId,
            Depth = 2,
        });
        await Store.StartRunAsync(TestData.Run(yabanciId, "other"));

        var tree = await Store.QueryRunsAsync(new RunQuery { RootRunId = rootId, OnlyRootRuns = false });

        tree.Select(static run => run.Id).ShouldBe([rootId, childId, grandChildId], ignoreOrder: true);
    }

    [Fact]
    public async Task Tree_total_combines_tokens_of_root_and_descendants()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId, "researcher") with
        {
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteAsync(rootId, new RunUsage { InputTokens = 10, OutputTokens = 5, TotalTokens = 15 });
        await CompleteAsync(childId, new RunUsage { InputTokens = 30, OutputTokens = 20, TotalTokens = 50 });

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.Usage!.TotalTokens.ShouldBe(15);

        // The tree total already includes the root's own usage -- do not add
        // Usage and TreeUsage together yourself.
        root.TreeUsage!.TotalTokens.ShouldBe(65);
        root.TreeUsage.InputTokens.ShouldBe(40);
        root.TreeUsage.OutputTokens.ShouldBe(25);

        var child = await Store.GetRunAsync(childId);

        // For a run with no descendants, the tree total equals its own usage.
        child!.TreeUsage!.TotalTokens.ShouldBe(50);
    }

    [Fact]
    public async Task Tree_total_stays_null_when_no_tokens_are_reported()
    {
        var rootId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await CompleteAsync(rootId, usage: null);

        var root = await Store.GetRunAsync(rootId);

        // Writing zero would make "the provider did not report tokens"
        // indistinguishable from "no tokens were spent at all".
        root!.TreeUsage.ShouldBeNull();
    }

    // --- Cost (Phase 20) ---

    [Fact]
    public async Task Cost_is_written_and_read_back()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "gpt-x" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 },
            Cost = new RunCost
            {
                InputCost = 0.1234567891m,
                OutputCost = 0.9876543219m,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

        var record = await Store.GetRunAsync(runId);

        record.ShouldNotBeNull();
        record.Cost.ShouldNotBeNull();
        record.Cost.Source.ShouldBe(PricingSource.Catalog);
        record.Cost.Currency.ShouldBe("USD");
        // numeric(20,10) must round-trip without rounding.
        record.Cost.InputCost.ShouldBe(0.1234567891m);
        record.Cost.OutputCost.ShouldBe(0.9876543219m);
    }

    [Fact]
    public async Task Cost_fields_are_null_but_source_is_written_as_unknown_when_pricing_is_undefined()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "never-priced-model" });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { Source = PricingSource.Unknown },
        });

        var record = await Store.GetRunAsync(runId);

        record!.Cost.ShouldNotBeNull();
        record.Cost.Source.ShouldBe(PricingSource.Unknown);
        // NOT zero: unknown pricing must not be confused with zero cost.
        record.Cost.InputCost.ShouldBeNull();
        record.Cost.OutputCost.ShouldBeNull();
    }

    [Fact]
    public async Task Cost_does_not_exist_at_all_when_model_is_unknown()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        // Cost is never passed: scenario of a code agent with no model attached.
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        var record = await Store.GetRunAsync(runId);

        // When the model is entirely unknown, Cost is NULL; this differs from
        // "model is known but pricing is undefined" (Source=Unknown, still a
        // populated RunCost).
        record!.Cost.ShouldBeNull();
    }

    [Fact]
    public async Task Tree_cost_is_not_summed_with_its_own_cost_separate_fields()
    {
        var rootId = AgentPrismId.NewId();
        var childId = AgentPrismId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId) with { ModelId = "gpt-x" });
        await Store.StartRunAsync(TestData.Run(childId, "researcher") with
        {
            ModelId = "gpt-x",
            ParentRunId = rootId,
            RootRunId = rootId,
            Depth = 1,
        });

        await CompleteWithCostAsync(rootId, 1m, 1m);
        await CompleteWithCostAsync(childId, 2m, 3m);

        var root = await Store.GetRunAsync(rootId);

        root.ShouldNotBeNull();
        root.Cost!.InputCost.ShouldBe(1m);
        root.Cost.OutputCost.ShouldBe(1m);

        // The tree total also includes the root's OWN cost; the two are not
        // summed and shown separately (same pattern as RunTreeUsage).
        root.TreeCost!.InputCost.ShouldBe(3m);
        root.TreeCost.OutputCost.ShouldBe(4m);

        var child = await Store.GetRunAsync(childId);

        // For a run with no descendants, the tree total equals its own cost.
        child!.TreeCost!.InputCost.ShouldBe(2m);
        child.TreeCost.OutputCost.ShouldBe(3m);
    }

    [Fact]
    public async Task Summary_aggregates_cost_and_reports_the_undefined_count()
    {
        await CompleteRunWithCostAsync("alpha", "gpt-x", 1m, 1m, PricingSource.Catalog);
        await CompleteRunWithCostAsync("alpha", "gpt-x", 2m, 2m, PricingSource.Catalog);
        await CompleteRunWithCostAsync("beta", "unpriced-model", null, null, PricingSource.Unknown);

        var stats = await Store.GetStatisticsAsync(new RunStatisticsQuery());

        stats.TotalCost.ShouldBe(6m);
        stats.Currency.ShouldBe("USD");
        stats.RunsWithUnknownPricing.ShouldBe(1);

        var model = stats.ByModel.Single(static m => string.Equals(m.ModelId, "gpt-x", StringComparison.Ordinal));
        model.TotalCost.ShouldBe(6m);
    }

    [Fact]
    public async Task Experiment_results_include_cost()
    {
        var experimentId = Guid.NewGuid();

        var controlRun = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(controlRun, "alpha") with
        {
            ModelId = "gpt-x",
            ExperimentId = experimentId,
            Variant = "control",
        });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = controlRun,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { InputCost = 1m, OutputCost = 2m, Currency = "USD", Source = PricingSource.Catalog },
        });

        var results = await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId });

        var control = results.ShouldHaveSingleItem();
        control.TotalCost.ShouldBe(3m);
        control.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task Time_series_fills_empty_buckets()
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-3);
        var to = from.AddHours(3);

        // Deliberately, no run is placed into the middle bucket (from+1h).
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = from.AddMinutes(5) });
        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = from.AddHours(2).AddMinutes(5) });

        var points = await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery { From = from, To = to, Bucket = TimeSeriesBucket.Hour });

        points.Count.ShouldBe(3);
        points[0].Runs.ShouldBe(1);
        points[1].Runs.ShouldBe(0);
        points[2].Runs.ShouldBe(1);
    }

    [Fact]
    public async Task Time_series_does_not_exclude_eval_runs()
    {
        // Unlike /api/stats (K-141), the time series does NOT exclude
        // Eval/Workflow runs by default (see docs/KARARLAR.md K-152).
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-1);
        var to = from.AddHours(1);

        await Store.StartRunAsync(TestData.Run(AgentPrismId.NewId()) with { StartedAt = from.AddMinutes(5), Kind = RunKind.Eval });

        var points = await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery { From = from, To = to, Bucket = TimeSeriesBucket.Hour });

        points.ShouldHaveSingleItem().Runs.ShouldBe(1);
    }

    [Fact]
    public async Task Time_series_throws_when_the_bucket_limit_is_exceeded()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;

        var exception = await Should.ThrowAsync<AgentPrismException>(async () =>
            await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery { From = from, To = to, Bucket = TimeSeriesBucket.Hour }));

        exception.Message.ShouldContain("500");
    }

    [Fact]
    public async Task Cost_recalculation_updates_the_target_row()
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { ModelId = "gpt-x" });
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { Source = PricingSource.Unknown },
        });

        await Store.UpdateRunCostAsync(runId, new RunCost
        {
            InputCost = 5m,
            OutputCost = 5m,
            Currency = "USD",
            Source = PricingSource.Configuration,
        });

        var record = await Store.GetRunAsync(runId);

        record!.Cost!.Source.ShouldBe(PricingSource.Configuration);
        record.Cost.InputCost.ShouldBe(5m);
    }

    private async Task CompleteWithCostAsync(Guid runId, decimal inputCost, decimal outputCost)
        => await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost
            {
                InputCost = inputCost,
                OutputCost = outputCost,
                Currency = "USD",
                Source = PricingSource.Catalog,
            },
        });

    private async Task CompleteRunWithCostAsync(
        string agentName,
        string modelId,
        decimal? inputCost,
        decimal? outputCost,
        PricingSource source)
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId, agentName) with { ModelId = modelId });

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Cost = new RunCost { InputCost = inputCost, OutputCost = outputCost, Currency = "USD", Source = source },
        });
    }

    private async Task CompleteAsync(Guid runId, RunUsage? usage)
        => await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = usage,
        });

    private async Task CompleteRunAsync(string agentName, RunStatus status, RunUsage? usage)
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId, agentName));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = status,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = usage,
        });
    }

    private async Task FailedRunAsync(RunErrorClass errorClass, string fingerprint, string message)
    {
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError
            {
                Type = "test_error",
                Message = message,
                Class = errorClass,
                Fingerprint = fingerprint,
            },
        });
    }

    [Fact]
    public async Task Summary_and_time_series_do_not_leak_across_tenants()
    {
        var now = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(-1);

        AmbientTenant.TenantId = TenantA;
        var experimentId = Guid.NewGuid();
        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with
        {
            StartedAt = from.AddMinutes(5),
            ExperimentId = experimentId,
            Variant = "control",
        });

        AmbientTenant.TenantId = TenantB;

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).TotalRuns.ShouldBe(0);
        (await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery
        {
            From = from,
            To = from.AddHours(1),
            Bucket = TimeSeriesBucket.Hour,
        })).Sum(static point => point.Runs).ShouldBe(0);
        (await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId })).ShouldBeEmpty();

        AmbientTenant.TenantId = TenantA;

        (await Store.GetStatisticsAsync(new RunStatisticsQuery())).TotalRuns.ShouldBe(1);
        (await Store.GetTimeSeriesAsync(new RunTimeSeriesQuery
        {
            From = from,
            To = from.AddHours(1),
            Bucket = TimeSeriesBucket.Hour,
        })).Sum(static point => point.Runs).ShouldBe(1);
        (await Store.GetExperimentResultsAsync(new ExperimentResultsQuery { ExperimentId = experimentId })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Run_record_is_stamped_with_the_current_tenant()
    {
        // Moved from IsolationTests.cs (Phase 41): now runs across all four
        // test runs, not just PostgreSQL.
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        var record = await Store.StartRunAsync(TestData.Run(runId));

        record.TenantId.ShouldBe(TenantA);
        (await Store.GetRunAsync(runId))!.TenantId.ShouldBe(TenantA);
    }

    // --- EXPECTED tenant on sub-write paths (K-355) ---
    //
    // 🚨 These four tests close a defect. From Phase 41 until 2026-08-08,
    // AppendEventAsync / CompleteRunAsync / UpdateRunCostAsync /
    // RecordToolInvocationAsync carried NO tenant filter at all. Filtering by
    // the ambient tenant was tried and REVERTED: RunStartInfo.TenantId
    // deliberately overrides the ambient tenant (this is how workflows and
    // the job queue operate), and the filter was dropping legitimate writes.
    // The fix is not the ambient tenant but the EXPECTED tenant carried by
    // the call.
    //
    // Each test is two-directional: the wrong tenant is rejected, the CORRECT
    // tenant succeeds. A one-directional check would also pass a broken
    // condition that rejects every write.

    [Fact]
    public async Task AppendEventAsync_does_not_write_with_the_wrong_expected_tenant()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Should.ThrowAsync<AgentPrismException>(async () =>
            await Store.AppendEventAsync(TestData.Event(runId, 0) with { TenantId = TenantB }));

        // The correct tenant succeeds; the condition is not too narrow.
        await Store.AppendEventAsync(TestData.Event(runId, 0) with { TenantId = TenantA });

        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        events.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CompleteRunAsync_does_not_close_with_the_wrong_expected_tenant()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            TenantId = TenantB,
        }));

        (await Store.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running);

        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            TenantId = TenantA,
        });

        (await Store.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task UpdateRunCostAsync_does_not_update_with_the_wrong_expected_tenant()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var cost = new RunCost
        {
            InputCost = 1.5m,
            OutputCost = 2.5m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        };

        // This is a maintenance path: it is SILENTLY skipped for the wrong
        // tenant, no exception is thrown.
        await Store.UpdateRunCostAsync(runId, cost, TenantB);
        (await Store.GetRunAsync(runId))!.Cost.ShouldBeNull();

        await Store.UpdateRunCostAsync(runId, cost, TenantA);
        (await Store.GetRunAsync(runId))!.Cost!.InputCost.ShouldBe(1.5m);
    }

    [Fact]
    public async Task RecordToolInvocationAsync_does_not_write_with_the_wrong_expected_tenant()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));

        var invocation = new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = "get_order",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await Should.ThrowAsync<AgentPrismException>(async () =>
            await Store.RecordToolInvocationAsync(invocation with { TenantId = TenantB }));

        (await Store.ListToolInvocationsAsync(runId)).ShouldBeEmpty();

        await Store.RecordToolInvocationAsync(invocation with { TenantId = TenantA });

        (await Store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();
    }

    // --- Orphaned run reconciliation (Phase 54) ---

    [Fact]
    public async Task TouchHeartbeatAsync_silently_skips_a_nonexistent_or_non_Running_id()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId));
        await Store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        // Neither a nonexistent id nor a Completed row should throw -- this is
        // a maintenance signal, it must not interrupt the run.
        await Store.TouchHeartbeatAsync([runId, AgentPrismId.NewId()], DateTimeOffset.UtcNow);

        (await Store.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task ClaimOrphanedRunsAsync_marks_a_stale_Running_row_as_Failed()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await Store.StartRunAsync(TestData.Run(runId) with { StartedAt = startedAt });

        var claimed = await Store.ClaimOrphanedRunsAsync(
            staleBefore: DateTimeOffset.UtcNow.AddMinutes(-5),
            max: 10);

        var record = claimed.ShouldHaveSingleItem();
        record.Id.ShouldBe(runId);
        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull();
        record.Error!.Type.ShouldBe("orphaned");
        record.Error.Class.ShouldBe(RunErrorClass.Infrastructure);

        var stored = await Store.GetRunAsync(runId);
        stored!.Status.ShouldBe(RunStatus.Failed);
        stored.Error!.Type.ShouldBe("orphaned");

        // RunEventWriter no longer exists in that process; reconciliation must
        // write the event itself -- a run that closes without an event cannot
        // answer the "why did it end" question.
        var events = new List<RunEvent>();

        await foreach (var runEvent in Store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunFailed);
    }

    [Fact]
    public async Task ClaimOrphanedRunsAsync_does_not_touch_a_Running_row_within_the_threshold()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        await Store.StartRunAsync(TestData.Run(runId) with { StartedAt = DateTimeOffset.UtcNow });

        var claimed = await Store.ClaimOrphanedRunsAsync(
            staleBefore: DateTimeOffset.UtcNow.AddMinutes(-5),
            max: 10);

        claimed.ShouldNotContain(record => record.Id == runId);
        (await Store.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running);
    }

    [Fact]
    public async Task ClaimOrphanedRunsAsync_never_touches_a_Queued_row()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await Store.StartRunAsync(TestData.Run(runId) with { StartedAt = startedAt, Status = RunStatus.Queued });

        var claimed = await Store.ClaimOrphanedRunsAsync(
            staleBefore: DateTimeOffset.UtcNow.AddMinutes(-5),
            max: 10);

        claimed.ShouldNotContain(record => record.Id == runId);
        (await Store.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Queued);
    }

    [Fact]
    public async Task TouchHeartbeatAsync_refreshed_row_is_not_considered_orphaned()
    {
        AmbientTenant.TenantId = TenantA;

        var runId = AgentPrismId.NewId();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await Store.StartRunAsync(TestData.Run(runId) with { StartedAt = startedAt });

        // heartbeat_at OVERRIDES started_at: a run that signaled recently,
        // despite an old start time, must be considered alive.
        await Store.TouchHeartbeatAsync([runId], DateTimeOffset.UtcNow);

        var claimed = await Store.ClaimOrphanedRunsAsync(
            staleBefore: DateTimeOffset.UtcNow.AddMinutes(-5),
            max: 10);

        claimed.ShouldNotContain(record => record.Id == runId);
        (await Store.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running);
    }
}
