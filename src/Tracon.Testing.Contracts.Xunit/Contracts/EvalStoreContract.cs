
namespace Tracon.Testing.Contracts.Storage;

/// <summary>Behavior tests for the <see cref="IEvalStore"/> contract.</summary>
public abstract class EvalStoreContract : TenantIsolationContract<IEvalStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId, name), cancellationToken);
        await Store.CreateRunAsync(TestData.EvalRun(tenantId, suite.Id), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetSuiteAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListSuitesAsync(tenantId, cancellationToken)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteSuiteAsync(tenantId, (string)key);

    [Fact]
    public async Task SaveSuiteAsync_assigns_an_id_to_a_new_suite()
    {
        var saved = await Store.SaveSuiteAsync(TestData.EvalSuite());

        saved.Id.ShouldNotBe(Guid.Empty);
        saved.CreatedAt.ShouldNotBe(default);
        saved.UpdatedAt.ShouldNotBe(default);
    }

    /// <summary>
    /// A suite saved with no checks round-trips through every provider.
    /// </summary>
    /// <remarks>
    /// The eval side of the same class as the schedule payload: the unset
    /// value used to reach the column as the literal <c>null</c>, which SQL
    /// Server's <c>CHECK (ISJSON(checks) = 1)</c> rejects.
    /// </remarks>
    [Fact]
    public async Task SaveSuiteAsync_round_trips_a_suite_that_carries_no_checks()
    {
        var saved = await Store.SaveSuiteAsync(TestData.EvalSuite() with { Checks = default });

        saved.Checks.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        saved.Checks.GetArrayLength().ShouldBe(0);

        var fetched = await Store.GetSuiteAsync("default", saved.Name);
        fetched!.Checks.ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Array);
        fetched.Checks.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task SaveSuiteAsync_keeps_the_id_for_the_same_name_and_updates()
    {
        var first = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var second = await Store.SaveSuiteAsync(TestData.EvalSuite() with { Description = "updated" });

        second.Id.ShouldBe(first.Id);

        var fetched = await Store.GetSuiteAsync("default", "customer-support-team");
        fetched!.Description.ShouldBe("updated");
    }

    [Fact]
    public async Task GetSuiteAsync_returns_null_for_another_tenant()
    {
        await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId: "tenant-a"));

        (await Store.GetSuiteAsync("tenant-b", "customer-support-team")).ShouldBeNull();
    }

    [Fact]
    public async Task ListSuitesAsync_returns_only_that_tenant()
    {
        await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId: "tenant-a", name: "s1"));
        await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId: "tenant-b", name: "s2"));

        var list = await Store.ListSuitesAsync("tenant-a");

        list.ShouldHaveSingleItem();
        list[0].Name.ShouldBe("s1");
    }

    [Fact]
    public async Task DeleteSuiteAsync_also_deletes_its_cases_and_runs()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id)]);
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id));

        (await Store.DeleteSuiteAsync("default", "customer-support-team")).ShouldBeTrue();

        (await Store.ListCasesAsync(suite.Id)).ShouldBeEmpty();
        (await Store.GetRunAsync("default", run.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task ReplaceCasesAsync_assigns_sequence_numbers_and_replaces_previous_cases()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var saved = await Store.ReplaceCasesAsync(
            suite.Id,
            [TestData.EvalCase(suite.Id, "first"), TestData.EvalCase(suite.Id, "second")]);

        saved[0].Seq.ShouldBe(0);
        saved[1].Seq.ShouldBe(1);
        saved[0].SuiteId.ShouldBe(suite.Id);

        var replaced = await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id, "single")]);

        replaced.Count.ShouldBe(1);
        (await Store.ListCasesAsync(suite.Id)).Count.ShouldBe(1);
        (await Store.ListCasesAsync(suite.Id))[0].Query.ShouldBe("single");
    }

    [Fact]
    public async Task ReplaceCasesAsync_stores_expected_tools_and_context()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var input = TestData.EvalCase(suite.Id) with
        {
            ExpectedOutput = "expected",
            ExpectedTools = ["get_order_status", "get_shipping_status"],
            Context = "extra context",
        };

        await Store.ReplaceCasesAsync(suite.Id, [input]);
        var loaded = (await Store.ListCasesAsync(suite.Id))[0];

        loaded.ExpectedOutput.ShouldBe("expected");
        loaded.ExpectedTools.ShouldBe(["get_order_status", "get_shipping_status"]);
        loaded.Context.ShouldBe("extra context");
    }

    [Fact]
    public async Task ReplaceCasesAsync_stores_parameter_values_for_a_parameterized_agent()
    {
        // A parameterized agent cannot be evaluated without a value for each
        // placeholder its instructions reference; the case must round-trip
        // the exact map it was given.
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var input = TestData.EvalCase(suite.Id) with
        {
            Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customer"] = "Acme",
                ["tone"] = "formal",
            },
        };

        await Store.ReplaceCasesAsync(suite.Id, [input]);
        var loaded = (await Store.ListCasesAsync(suite.Id))[0];

        loaded.Parameters.ShouldNotBeNull();
        loaded.Parameters!.Count.ShouldBe(2);
        loaded.Parameters["customer"].ShouldBe("Acme");
        loaded.Parameters["tone"].ShouldBe("formal");
    }

    [Fact]
    public async Task ReplaceCasesAsync_leaves_parameters_null_for_a_case_that_declares_none()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id)]);
        var loaded = (await Store.ListCasesAsync(suite.Id))[0];

        loaded.Parameters.ShouldBeNull();
    }

    [Fact]
    public async Task ReplaceCasesAsync_keeps_an_identifier_it_was_given()
    {
        // A case holds its identifier for life: the run-to-run diff behind
        // EvalRunDiffBuilder is matched on it, so a replace that reassigned
        // identifiers would read an unchanged case as one removed and another
        // added, and a real regression in the same window would be classed
        // "Removed" — which the --max-regressions gate deliberately ignores.
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var saved = await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id, "first")]);
        var original = saved[0].Id;

        original.ShouldNotBe(Guid.Empty);

        var replaced = await Store.ReplaceCasesAsync(
            suite.Id,
            [saved[0] with { Query = "first, revised" }, TestData.EvalCase(suite.Id, "second")]);

        replaced[0].Id.ShouldBe(original);
        replaced[0].Query.ShouldBe("first, revised");
        replaced[1].Id.ShouldNotBe(original);

        var loaded = await Store.ListCasesAsync(suite.Id);
        loaded[0].Id.ShouldBe(original);
    }

    [Fact]
    public async Task ReplaceCasesAsync_keeps_the_promotion_fields_it_was_given()
    {
        // A replace rewrites every row of the suite. Writing the case back
        // without its origin silently un-promoted it, and with the origin gone
        // the store's own duplicate guard stopped recognising the run — the
        // same run could be promoted a second time.
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var promoted = await Store.AddCaseAsync(
            suite.Id,
            new EvalCaseDraft
            {
                Query = "promoted",
                SourceRunId = Guid.NewGuid(),
                SourceKind = EvalCaseSource.FailedRun,
            });

        await Store.ReplaceCasesAsync(suite.Id, [promoted.Case]);
        var loaded = (await Store.ListCasesAsync(suite.Id))[0];

        loaded.SourceRunId.ShouldBe(promoted.Case.SourceRunId);
        loaded.SourceKind.ShouldBe(EvalCaseSource.FailedRun);
        loaded.PromotedAt.ShouldNotBeNull();

        // The guard still sees it, so the run is not promotable twice.
        var again = await Store.AddCaseAsync(
            suite.Id,
            new EvalCaseDraft
            {
                Query = "promoted",
                SourceRunId = promoted.Case.SourceRunId,
                SourceKind = EvalCaseSource.FailedRun,
            });

        again.Created.ShouldBeFalse();
        again.Case.Id.ShouldBe(loaded.Id);
    }

    [Fact]
    public async Task AddCaseAsync_assigns_seq_atomically()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var first = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(TraconId.NewId(), "first"));
        var second = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(TraconId.NewId(), "second"));

        first.Created.ShouldBeTrue();
        second.Created.ShouldBeTrue();
        first.Case.Seq.ShouldBe(0);
        second.Case.Seq.ShouldBe(1);
    }

    [Fact]
    public async Task AddCaseAsync_stores_source_fields_and_expected_fields()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var runId = TraconId.NewId();

        var added = await Store.AddCaseAsync(
            suite.Id,
            new EvalCaseDraft
            {
                Query = "question",
                ExpectedOutput = "expected",
                ExpectedTools = ["get_order_status"],
                Context = "context",
                SourceRunId = runId,
                SourceKind = EvalCaseSource.ReferenceRun,
            });

        added.Created.ShouldBeTrue();
        added.Case.Query.ShouldBe("question");
        added.Case.ExpectedOutput.ShouldBe("expected");
        added.Case.ExpectedTools.ShouldBe(["get_order_status"]);
        added.Case.Context.ShouldBe("context");
        added.Case.SourceRunId.ShouldBe(runId);
        added.Case.SourceKind.ShouldBe(EvalCaseSource.ReferenceRun);
        added.Case.PromotedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AddCaseAsync_returns_the_existing_case_the_second_time_for_the_same_source_run_id()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var runId = TraconId.NewId();

        var first = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(runId));
        var second = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(runId, "different-question"));

        first.Created.ShouldBeTrue();
        second.Created.ShouldBeFalse();
        second.Case.Id.ShouldBe(first.Case.Id);
        second.Case.Query.ShouldBe(first.Case.Query);

        (await Store.ListCasesAsync(suite.Id)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AddCaseAsync_does_not_separate_sourceless_cases_from_hand_written_ones()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        await Store.ReplaceCasesAsync(suite.Id, [TestData.EvalCase(suite.Id, "manual")]);

        var added = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(TraconId.NewId(), "promoted"));

        added.Case.Seq.ShouldBe(1);

        var cases = await Store.ListCasesAsync(suite.Id);
        cases.Count.ShouldBe(2);
        cases[0].SourceRunId.ShouldBeNull();
        cases[1].SourceRunId.ShouldNotBeNull();
    }

    [Fact]
    public async Task AddCaseAsync_concurrent_promotions_produce_distinct_seq()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(TraconId.NewId())).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.ShouldAllBe(static result => result.Created);
        results.Select(static result => result.Case.Seq).Distinct().Count().ShouldBe(results.Length);
    }

    [Fact]
    public async Task Run_lifecycle_transitions_from_running_to_completed()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var jobId = Guid.NewGuid();
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id) with { JobId = jobId });

        run.Status.ShouldBe(EvalRunStatus.Pending);

        await Store.MarkRunRunningAsync(run.Id, agentVersion: 4, modelId: "gpt-test");
        var running = await Store.GetRunAsync("default", run.Id);
        running!.Status.ShouldBe(EvalRunStatus.Running);
        running.AgentVersion.ShouldBe(4);
        running.ModelId.ShouldBe("gpt-test");

        await Store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = run.Id,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = 3,
            Passed = 2,
            Failed = 1,
            InputTokens = 100,
            OutputTokens = 50,
        });

        var completed = await Store.GetRunAsync("default", run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Completed);
        completed.Passed.ShouldBe(2);
        completed.Failed.ShouldBe(1);
        completed.InputTokens.ShouldBe(100);
        completed.OutputTokens.ShouldBe(50);
        completed.CompletedAt.ShouldNotBeNull();

        var byJob = await Store.GetRunByJobIdAsync("default", jobId);
        byJob!.Id.ShouldBe(run.Id);
    }

    [Fact]
    public async Task QueryRunsAsync_filters_by_suite_and_puts_the_newest_first()
    {
        var suiteA = await Store.SaveSuiteAsync(TestData.EvalSuite(name: "team-a"));
        var suiteB = await Store.SaveSuiteAsync(TestData.EvalSuite(name: "team-b"));

        var first = await Store.CreateRunAsync(TestData.EvalRun("default", suiteA.Id) with { StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
        var second = await Store.CreateRunAsync(TestData.EvalRun("default", suiteA.Id) with { StartedAt = DateTimeOffset.UtcNow });
        await Store.CreateRunAsync(TestData.EvalRun("default", suiteB.Id));

        var runs = await Store.QueryRunsAsync(new EvalRunQuery { TenantId = "default", SuiteId = suiteA.Id });

        runs.Count.ShouldBe(2);
        runs[0].Id.ShouldBe(second.Id);
        runs[1].Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Case_result_is_recorded_and_filtered_by_tenant()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id));
        var runId = Guid.NewGuid();

        await Store.RecordCaseResultAsync(new EvalCaseResult
        {
            EvalRunId = run.Id,
            CaseId = Guid.NewGuid(),
            RunId = runId,
            Passed = false,
            Output = "output",
            Scores = TestData.State("""[{"name":"nonEmpty","passed":false}]"""),
            FailureReason = "too short",
        });

        var results = await Store.ListCaseResultsAsync("default", run.Id);

        results.ShouldHaveSingleItem();
        results[0].Passed.ShouldBeFalse();
        results[0].Output.ShouldBe("output");
        results[0].RunId.ShouldBe(runId);
        results[0].FailureReason.ShouldBe("too short");

        (await Store.ListCaseResultsAsync("other-tenant", run.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_reads_do_not_leak_across_tenants()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite("tenant-a", "team"));
        var jobId = TraconId.NewId();
        var run = await Store.CreateRunAsync(TestData.EvalRun("tenant-a", suite.Id) with { JobId = jobId });

        await Store.RecordCaseResultAsync(new EvalCaseResult
        {
            Id = TraconId.NewId(),
            EvalRunId = run.Id,
            CaseId = TraconId.NewId(),
            Passed = true,
        });

        (await Store.GetRunAsync("tenant-b", run.Id)).ShouldBeNull();
        (await Store.GetRunAsync("tenant-a", run.Id)).ShouldNotBeNull();

        (await Store.GetRunByJobIdAsync("tenant-b", jobId)).ShouldBeNull();
        (await Store.GetRunByJobIdAsync("tenant-a", jobId)).ShouldNotBeNull();

        (await Store.ListCaseResultsAsync("tenant-b", run.Id)).ShouldBeEmpty();
        (await Store.ListCaseResultsAsync("tenant-a", run.Id)).ShouldHaveSingleItem();

        (await Store.QueryRunsAsync(new EvalRunQuery { TenantId = "tenant-b" })).ShouldBeEmpty();
        (await Store.QueryRunsAsync(new EvalRunQuery { TenantId = "tenant-a" })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DiffRunsAsync_separates_the_six_buckets()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var unchanged = TraconId.NewId();
        var regressed = TraconId.NewId();
        var repaired = TraconId.NewId();
        var stillFailing = TraconId.NewId();
        var removed = TraconId.NewId();
        var added = TraconId.NewId();

        var baseline = await CompletedRunAsync(
            suite.Id,
            (unchanged, true),
            (regressed, true),
            (repaired, false),
            (stillFailing, false),
            (removed, true));

        var candidate = await CompletedRunAsync(
            suite.Id,
            (unchanged, true),
            (regressed, false),
            (repaired, true),
            (stillFailing, false),
            (added, false));

        var diff = await Store.DiffRunsAsync(new EvalRunDiffQuery
        {
            TenantId = "default",
            BaselineRunId = baseline,
            CandidateRunId = candidate,
        });

        diff.ShouldNotBeNull();
        diff!.Baseline.Id.ShouldBe(baseline);
        diff.Candidate.Id.ShouldBe(candidate);
        diff.TotalCases.ShouldBe(6);
        diff.UnchangedCount.ShouldBe(1);
        diff.RegressedCount.ShouldBe(1);
        diff.FixedCount.ShouldBe(1);
        diff.StillFailingCount.ShouldBe(1);
        diff.RemovedCount.ShouldBe(1);
        diff.AddedCount.ShouldBe(1);

        diff.Cases.Single(entry => entry.CaseId == regressed).Kind.ShouldBe(EvalCaseDiffKind.Regressed);
        diff.Cases.Single(entry => entry.CaseId == added).Kind.ShouldBe(EvalCaseDiffKind.Added);
        diff.Cases.Single(entry => entry.CaseId == removed).Kind.ShouldBe(EvalCaseDiffKind.Removed);
    }

    [Fact]
    public async Task DiffRunsAsync_throws_when_the_baseline_details_were_removed_by_retention()
    {
        // 🚨 An empty diff would read as "nothing changed" and turn a CI gate
        // green over a regression. The run keeps its summary; only the per-case
        // rows are a retention target.
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var trimmed = await CompletedRunAsync(suite.Id, caseCount: 3);
        var candidate = await CompletedRunAsync(suite.Id, (TraconId.NewId(), true));

        var error = await Should.ThrowAsync<EvalRunDiffUnavailableException>(async () =>
            await Store.DiffRunsAsync(new EvalRunDiffQuery
            {
                TenantId = "default",
                BaselineRunId = trimmed,
                CandidateRunId = candidate,
            }));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DetailsRemoved);
    }

    [Fact]
    public async Task DiffRunsAsync_throws_when_the_candidate_details_were_removed_by_retention()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var baseline = await CompletedRunAsync(suite.Id, (TraconId.NewId(), true));
        var trimmed = await CompletedRunAsync(suite.Id, caseCount: 3);

        var error = await Should.ThrowAsync<EvalRunDiffUnavailableException>(async () =>
            await Store.DiffRunsAsync(new EvalRunDiffQuery
            {
                TenantId = "default",
                BaselineRunId = baseline,
                CandidateRunId = trimmed,
            }));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DetailsRemoved);
    }

    [Fact]
    public async Task DiffRunsAsync_throws_when_retention_removed_only_SOME_of_the_results()
    {
        // Retention deletes in batches and the sweep can stop between them, so
        // "partly trimmed" is a real state. Diffing the survivors would report
        // the deleted cases as Removed and quietly shrink the regression count.
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var kept = TraconId.NewId();
        var partial = await CompletedRunAsync(suite.Id, summarisedCases: 5, (kept, true));
        var candidate = await CompletedRunAsync(suite.Id, (kept, true));

        var error = await Should.ThrowAsync<EvalRunDiffUnavailableException>(async () =>
            await Store.DiffRunsAsync(new EvalRunDiffQuery
            {
                TenantId = "default",
                BaselineRunId = partial,
                CandidateRunId = candidate,
            }));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DetailsRemoved);
    }

    [Fact]
    public async Task DiffRunsAsync_counts_a_case_recorded_twice_once()
    {
        // K-641: IJobHandler is at-least-once, so the same eval run can record
        // a case a second time. Alignment must not double count it.
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var caseId = TraconId.NewId();
        var baseline = await CompletedRunAsync(suite.Id, (caseId, true));
        var candidate = await CompletedRunAsync(suite.Id, (caseId, true), (caseId, true));

        var diff = await Store.DiffRunsAsync(new EvalRunDiffQuery
        {
            TenantId = "default",
            BaselineRunId = baseline,
            CandidateRunId = candidate,
        });

        diff!.TotalCases.ShouldBe(1);
        diff.Cases.ShouldHaveSingleItem().Kind.ShouldBe(EvalCaseDiffKind.Unchanged);
    }

    [Fact]
    public async Task DiffRunsAsync_returns_null_for_another_tenants_run()
    {
        var mine = await Store.SaveSuiteAsync(TestData.EvalSuite("tenant-a", "team"));
        var theirs = await Store.SaveSuiteAsync(TestData.EvalSuite("tenant-b", "team"));
        var caseId = TraconId.NewId();

        var baseline = await CompletedRunAsync(mine.Id, "tenant-a", (caseId, true));
        var foreign = await CompletedRunAsync(theirs.Id, "tenant-b", (caseId, true));

        (await Store.DiffRunsAsync(new EvalRunDiffQuery
        {
            TenantId = "tenant-a",
            BaselineRunId = foreign,
            CandidateRunId = baseline,
        })).ShouldBeNull();

        (await Store.DiffRunsAsync(new EvalRunDiffQuery
        {
            TenantId = "tenant-a",
            BaselineRunId = baseline,
            CandidateRunId = foreign,
        })).ShouldBeNull();
    }

    [Fact]
    public async Task DiffRunsAsync_returns_null_for_an_unknown_run()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var known = await CompletedRunAsync(suite.Id, (TraconId.NewId(), true));

        (await Store.DiffRunsAsync(new EvalRunDiffQuery
        {
            TenantId = "default",
            BaselineRunId = TraconId.NewId(),
            CandidateRunId = known,
        })).ShouldBeNull();
    }

    [Fact]
    public async Task DiffRunsAsync_refuses_an_unfinished_run()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var completed = await CompletedRunAsync(suite.Id, (TraconId.NewId(), true));
        var pending = await Store.CreateRunAsync(TestData.EvalRun("default", suite.Id));

        var error = await Should.ThrowAsync<EvalRunDiffUnavailableException>(async () =>
            await Store.DiffRunsAsync(new EvalRunDiffQuery
            {
                TenantId = "default",
                BaselineRunId = pending.Id,
                CandidateRunId = completed,
            }));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.RunNotCompleted);
    }

    [Fact]
    public async Task DiffRunsAsync_refuses_two_runs_of_different_suites()
    {
        var first = await Store.SaveSuiteAsync(TestData.EvalSuite("default", "suite-one"));
        var second = await Store.SaveSuiteAsync(TestData.EvalSuite("default", "suite-two"));
        var caseId = TraconId.NewId();

        var baseline = await CompletedRunAsync(first.Id, (caseId, true));
        var candidate = await CompletedRunAsync(second.Id, (caseId, true));

        var error = await Should.ThrowAsync<EvalRunDiffUnavailableException>(async () =>
            await Store.DiffRunsAsync(new EvalRunDiffQuery
            {
                TenantId = "default",
                BaselineRunId = baseline,
                CandidateRunId = candidate,
            }));

        error.Reason.ShouldBe(EvalRunDiffUnavailableReason.DifferentSuites);
    }

    private ValueTask<Guid> CompletedRunAsync(Guid suiteId, params (Guid CaseId, bool Passed)[] results)
        => CompletedRunAsync(suiteId, "default", results);

    /// <summary>A run whose summary counts more cases than the rows it kept.</summary>
    private async ValueTask<Guid> CompletedRunAsync(
        Guid suiteId,
        int summarisedCases,
        params (Guid CaseId, bool Passed)[] results)
    {
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suiteId));

        foreach (var (caseId, passed) in results)
        {
            await Store.RecordCaseResultAsync(new EvalCaseResult
            {
                Id = TraconId.NewId(),
                EvalRunId = run.Id,
                CaseId = caseId,
                Passed = passed,
            });
        }

        await CompleteAsync(run.Id, summarisedCases, summarisedCases, 0);
        return run.Id;
    }

    private async ValueTask<Guid> CompletedRunAsync(Guid suiteId, int caseCount)
    {
        // A run that measured cases but keeps none of their rows - what
        // retention leaves behind.
        var run = await Store.CreateRunAsync(TestData.EvalRun("default", suiteId));
        await CompleteAsync(run.Id, caseCount, caseCount, 0);
        return run.Id;
    }

    private async ValueTask<Guid> CompletedRunAsync(
        Guid suiteId,
        string tenantId,
        params (Guid CaseId, bool Passed)[] results)
    {
        var run = await Store.CreateRunAsync(TestData.EvalRun(tenantId, suiteId));

        foreach (var (caseId, passed) in results)
        {
            await Store.RecordCaseResultAsync(new EvalCaseResult
            {
                Id = TraconId.NewId(),
                EvalRunId = run.Id,
                CaseId = caseId,
                RunId = TraconId.NewId(),
                Passed = passed,
                FailureReason = passed ? null : "check failed",
            });
        }

        var distinct = results.Select(static result => result.CaseId).Distinct().Count();
        var passedCount = results.Where(static result => result.Passed).Select(static result => result.CaseId).Distinct().Count();
        await CompleteAsync(run.Id, distinct, passedCount, distinct - passedCount);

        return run.Id;
    }

    private ValueTask CompleteAsync(Guid runId, int total, int passed, int failed)
        => Store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = runId,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = total,
            Passed = passed,
            Failed = failed,
        });
}
