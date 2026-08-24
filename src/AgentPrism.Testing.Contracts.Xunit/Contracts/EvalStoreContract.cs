
namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>Behavior tests for the <see cref="IEvalStore"/> contract.</summary>
public abstract class EvalStoreContract : TenantIsolationContract<IEvalStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite(tenantId, name));
        await Store.CreateRunAsync(TestData.EvalRun(tenantId, suite.Id));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetSuiteAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListSuitesAsync(tenantId)).Count;

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
    public async Task AddCaseAsync_assigns_seq_atomically()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());

        var first = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(AgentPrismId.NewId(), "first"));
        var second = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(AgentPrismId.NewId(), "second"));

        first.Created.ShouldBeTrue();
        second.Created.ShouldBeTrue();
        first.Case.Seq.ShouldBe(0);
        second.Case.Seq.ShouldBe(1);
    }

    [Fact]
    public async Task AddCaseAsync_stores_source_fields_and_expected_fields()
    {
        var suite = await Store.SaveSuiteAsync(TestData.EvalSuite());
        var runId = AgentPrismId.NewId();

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
        var runId = AgentPrismId.NewId();

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

        var added = await Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(AgentPrismId.NewId(), "promoted"));

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
            .Select(_ => Store.AddCaseAsync(suite.Id, TestData.EvalCaseDraft(AgentPrismId.NewId())).AsTask())
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
        var jobId = AgentPrismId.NewId();
        var run = await Store.CreateRunAsync(TestData.EvalRun("tenant-a", suite.Id) with { JobId = jobId });

        await Store.RecordCaseResultAsync(new EvalCaseResult
        {
            Id = AgentPrismId.NewId(),
            EvalRunId = run.Id,
            CaseId = AgentPrismId.NewId(),
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
}
