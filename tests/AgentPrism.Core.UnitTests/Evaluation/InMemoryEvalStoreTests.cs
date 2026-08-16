namespace AgentPrism.Core.UnitTests.Evaluation;

public sealed class InMemoryEvalStoreTests
{
    [Fact]
    public async Task Suite_is_saved_and_read_back()
    {
        var store = new InMemoryEvalStore();

        var saved = await store.SaveSuiteAsync(Suite("tenant", "customer-support"));

        saved.Id.ShouldNotBe(Guid.Empty);
        saved.CreatedAt.ShouldNotBe(default);

        var loaded = await store.GetSuiteAsync("tenant", "customer-support");
        loaded.ShouldNotBeNull();
        loaded!.Id.ShouldBe(saved.Id);
    }

    [Fact]
    public async Task Another_tenants_suite_is_not_visible()
    {
        var store = new InMemoryEvalStore();
        await store.SaveSuiteAsync(Suite("tenant-a", "suite"));

        var loaded = await store.GetSuiteAsync("tenant-b", "suite");

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_suite_also_deletes_its_cases_and_runs()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("tenant", "suite"));

        await store.ReplaceCasesAsync(suite.Id, [Case("question-1")]);
        var run = await store.CreateRunAsync(Run("tenant", suite.Id));

        var deleted = await store.DeleteSuiteAsync("tenant", "suite");

        deleted.ShouldBeTrue();
        (await store.ListCasesAsync(suite.Id)).ShouldBeEmpty();
        (await store.GetRunAsync("tenant", run.Id)).ShouldBeNull();
    }

    [Fact]
    public async Task ReplaceCasesAsync_reassigns_sequence_numbers()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("tenant", "suite"));

        var saved = await store.ReplaceCasesAsync(suite.Id, [Case("first"), Case("second")]);

        saved[0].Seq.ShouldBe(0);
        saved[1].Seq.ShouldBe(1);
        saved[0].SuiteId.ShouldBe(suite.Id);

        // The second call fully replaces the previous ones.
        var replaced = await store.ReplaceCasesAsync(suite.Id, [Case("single")]);
        replaced.Count.ShouldBe(1);
        (await store.ListCasesAsync(suite.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Run_lifecycle_transitions_through_running_to_completed()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("tenant", "suite"));
        var run = await store.CreateRunAsync(Run("tenant", suite.Id));

        run.Status.ShouldBe(EvalRunStatus.Pending);

        await store.MarkRunRunningAsync(run.Id, agentVersion: 3, modelId: "gpt-x");
        var running = await store.GetRunAsync("tenant", run.Id);
        running!.Status.ShouldBe(EvalRunStatus.Running);
        running.AgentVersion.ShouldBe(3);
        running.ModelId.ShouldBe("gpt-x");

        await store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = run.Id,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = 2,
            Passed = 1,
            Failed = 1,
        });

        var completed = await store.GetRunAsync("tenant", run.Id);
        completed!.Status.ShouldBe(EvalRunStatus.Completed);
        completed.Passed.ShouldBe(1);
        completed.Failed.ShouldBe(1);
        completed.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetRunByJobIdAsync_finds_the_correct_run()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("tenant", "suite"));
        var jobId = Guid.NewGuid();
        var run = await store.CreateRunAsync(Run("tenant", suite.Id) with { JobId = jobId });

        var found = await store.GetRunByJobIdAsync("tenant", jobId);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(run.Id);
    }

    [Fact]
    public async Task Case_result_is_saved_and_filtered_by_tenant()
    {
        var store = new InMemoryEvalStore();
        var suite = await store.SaveSuiteAsync(Suite("tenant", "suite"));
        var run = await store.CreateRunAsync(Run("tenant", suite.Id));

        await store.RecordCaseResultAsync(new EvalCaseResult
        {
            EvalRunId = run.Id,
            CaseId = Guid.NewGuid(),
            Passed = true,
            Output = "answer",
        });

        var results = await store.ListCaseResultsAsync("tenant", run.Id);
        results.Count.ShouldBe(1);
        results[0].Output.ShouldBe("answer");

        (await store.ListCaseResultsAsync("other-tenant", run.Id)).ShouldBeEmpty();
    }

    private static EvalSuite Suite(string tenantId, string name) => new()
    {
        TenantId = tenantId,
        Name = name,
        AgentName = "customer-support-agent",
    };

    private static EvalCase Case(string query) => new()
    {
        SuiteId = Guid.Empty,
        Seq = 0,
        Query = query,
    };

    private static EvalRun Run(string tenantId, Guid suiteId) => new()
    {
        Id = Guid.Empty,
        TenantId = tenantId,
        SuiteId = suiteId,
        Status = EvalRunStatus.Pending,
        Total = 1,
        StartedAt = DateTimeOffset.UtcNow,
    };
}
