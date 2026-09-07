using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Behavior tests for <c>GET /api/evals/runs/{id}/diff</c> (Phase 153).
/// </summary>
public sealed class EvalRunDiffTests
{
    [Fact]
    public async Task Diff_separates_the_six_buckets()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);

        var unchanged = AgentPrismId.NewId();
        var regressed = AgentPrismId.NewId();
        var repaired = AgentPrismId.NewId();
        var stillFailing = AgentPrismId.NewId();
        var removed = AgentPrismId.NewId();
        var added = AgentPrismId.NewId();

        var baseline = await CompletedRunAsync(
            store,
            suite.Id,
            (unchanged, true),
            (regressed, true),
            (repaired, false),
            (stillFailing, false),
            (removed, true));

        var candidate = await CompletedRunAsync(
            store,
            suite.Id,
            (unchanged, true),
            (regressed, false),
            (repaired, true),
            (stillFailing, false),
            (added, false));

        using var response = await host.Client.GetAsync(Diff(candidate, baseline));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("totalCases").GetInt32().ShouldBe(6);
        body.GetProperty("regressedCount").GetInt32().ShouldBe(1);
        body.GetProperty("fixedCount").GetInt32().ShouldBe(1);
        body.GetProperty("stillFailingCount").GetInt32().ShouldBe(1);
        body.GetProperty("unchangedCount").GetInt32().ShouldBe(1);
        body.GetProperty("addedCount").GetInt32().ShouldBe(1);
        body.GetProperty("removedCount").GetInt32().ShouldBe(1);

        body.GetProperty("baseline").GetProperty("id").GetGuid().ShouldBe(baseline);
        body.GetProperty("candidate").GetProperty("id").GetGuid().ShouldBe(candidate);

        // The bucket is written as a name, not a number - a numeric enum would
        // silently change meaning if the order ever moved.
        KindOf(body, regressed).ShouldBe("Regressed");
        KindOf(body, added).ShouldBe("Added");
        KindOf(body, removed).ShouldBe("Removed");
        KindOf(body, repaired).ShouldBe("Fixed");

        // Regressions come first so the on-call engineer reads them without paging.
        body.GetProperty("cases")[0].GetProperty("caseId").GetGuid().ShouldBe(regressed);
    }

    [Fact]
    public async Task Reversing_the_two_runs_turns_a_regression_into_a_fix()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);
        var caseId = AgentPrismId.NewId();

        var older = await CompletedRunAsync(store, suite.Id, (caseId, true));
        var newer = await CompletedRunAsync(store, suite.Id, (caseId, false));

        using (var forward = await host.Client.GetAsync(Diff(newer, older)))
        {
            KindOf(await AgentPrismTestHost.ReadJsonAsync(forward), caseId).ShouldBe("Regressed");
        }

        using var reversed = await host.Client.GetAsync(Diff(older, newer));
        KindOf(await AgentPrismTestHost.ReadJsonAsync(reversed), caseId).ShouldBe("Fixed");
    }

    [Fact]
    public async Task A_run_whose_details_retention_removed_answers_409_not_an_empty_diff()
    {
        // 🚨 The failure mode this endpoint exists to prevent: an empty diff
        // reads as "nothing changed" and turns a CI gate green over a
        // regression.
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);

        var trimmed = await TrimmedRunAsync(store, suite.Id, caseCount: 4);
        var candidate = await CompletedRunAsync(store, suite.Id, (AgentPrismId.NewId(), true));

        using var response = await host.Client.GetAsync(Diff(candidate, trimmed));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("baseline");
    }

    [Fact]
    public async Task The_candidate_losing_its_details_answers_409_too()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);

        var baseline = await CompletedRunAsync(store, suite.Id, (AgentPrismId.NewId(), true));
        var trimmed = await TrimmedRunAsync(store, suite.Id, caseCount: 4);

        using var response = await host.Client.GetAsync(Diff(trimmed, baseline));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()
            .ShouldNotBeNull().ShouldContain("candidate");
    }

    [Fact]
    public async Task Two_runs_of_different_suites_answer_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var first = await SuiteAsync(store, "suite-one");
        var second = await SuiteAsync(store, "suite-two");
        var caseId = AgentPrismId.NewId();

        var baseline = await CompletedRunAsync(store, first.Id, (caseId, true));
        var candidate = await CompletedRunAsync(store, second.Id, (caseId, false));

        using var response = await host.Client.GetAsync(Diff(candidate, baseline));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unfinished_run_answers_400_not_409()
    {
        // Two different faults with two different fixes: "wait for it" is not
        // "your history is gone".
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);

        var pending = await store.CreateRunAsync(NewRun(suite.Id));
        var candidate = await CompletedRunAsync(store, suite.Id, (AgentPrismId.NewId(), true));

        using var response = await host.Client.GetAsync(Diff(candidate, pending.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()
            .ShouldNotBeNull().ShouldContain("Pending");
    }

    [Fact]
    public async Task An_unknown_baseline_answers_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);
        var candidate = await CompletedRunAsync(store, suite.Id, (AgentPrismId.NewId(), true));

        using var response = await host.Client.GetAsync(Diff(candidate, AgentPrismId.NewId()));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_tenants_run_answers_404()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var mine = await SuiteAsync(store);
        var theirs = await SuiteAsync(store, "other-suite", tenantId: "other-tenant");

        var candidate = await CompletedRunAsync(store, mine.Id, (AgentPrismId.NewId(), true));
        var foreign = await CompletedRunAsync(store, theirs.Id, (AgentPrismId.NewId(), true), tenantId: "other-tenant");

        using var response = await host.Client.GetAsync(Diff(candidate, foreign));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Paging_moves_the_window_but_never_the_counters()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var store = host.Services.GetRequiredService<IEvalStore>();
        var suite = await SuiteAsync(store);

        var ids = Enumerable.Range(0, 12).Select(static _ => AgentPrismId.NewId()).ToArray();
        var rows = ids.Select(static id => (id, true)).ToArray();
        var baseline = await CompletedRunAsync(store, suite.Id, rows);
        var candidate = await CompletedRunAsync(store, suite.Id, rows);

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/evals/runs/{candidate}/diff?baseline={baseline}&skip=10&take=50", UriKind.Relative));

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("cases").GetArrayLength().ShouldBe(2);
        body.GetProperty("totalCases").GetInt32().ShouldBe(12);
        body.GetProperty("unchangedCount").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task A_failing_store_surfaces_the_fault_rather_than_an_empty_diff()
    {
        // A broken store must never be flattened into a 200 with no entries:
        // that reads as "nothing changed". The fault propagates - the test host
        // rethrows it, a real host turns it into a 500.
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IEvalStore>(new ThrowingEvalStore()));

        var fault = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.Client.GetAsync(Diff(AgentPrismId.NewId(), AgentPrismId.NewId())));

        fault.Message.ShouldBe("store is down");
    }

    private static Uri Diff(Guid candidate, Guid baseline)
        => new($"/agentprism/api/evals/runs/{candidate}/diff?baseline={baseline}", UriKind.Relative);

    private static string? KindOf(JsonElement body, Guid caseId)
        => body.GetProperty("cases").EnumerateArray()
            .Single(entry => entry.GetProperty("caseId").GetGuid() == caseId)
            .GetProperty("kind").GetString();

    private static async Task<EvalSuite> SuiteAsync(IEvalStore store, string name = "diff-suite", string tenantId = "default")
        => await store.SaveSuiteAsync(new EvalSuite
        {
            TenantId = tenantId,
            Name = name,
            AgentName = "customer-support-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty"}]""").RootElement,
        });

    private static EvalRun NewRun(Guid suiteId, string tenantId = "default") => new()
    {
        Id = AgentPrismId.NewId(),
        TenantId = tenantId,
        SuiteId = suiteId,
        Status = EvalRunStatus.Pending,
        Total = 1,
        StartedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<Guid> CompletedRunAsync(
        IEvalStore store,
        Guid suiteId,
        params (Guid CaseId, bool Passed)[] results)
        => await CompletedRunAsync(store, suiteId, results, "default");

    private static async Task<Guid> CompletedRunAsync(
        IEvalStore store,
        Guid suiteId,
        (Guid CaseId, bool Passed)[] results,
        string tenantId)
    {
        var run = await store.CreateRunAsync(NewRun(suiteId, tenantId));

        foreach (var (caseId, passed) in results)
        {
            await store.RecordCaseResultAsync(new EvalCaseResult
            {
                Id = AgentPrismId.NewId(),
                EvalRunId = run.Id,
                CaseId = caseId,
                RunId = AgentPrismId.NewId(),
                Passed = passed,
                FailureReason = passed ? null : "check failed",
            });
        }

        var passedCount = results.Count(static result => result.Passed);
        await store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = run.Id,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = results.Length,
            Passed = passedCount,
            Failed = results.Length - passedCount,
        });

        return run.Id;
    }

    private static async Task<Guid> CompletedRunAsync(
        IEvalStore store,
        Guid suiteId,
        (Guid CaseId, bool Passed) single,
        string tenantId)
        => await CompletedRunAsync(store, suiteId, [single], tenantId);

    private static async Task<Guid> TrimmedRunAsync(IEvalStore store, Guid suiteId, int caseCount)
    {
        // A run that kept its summary while retention removed its per-case rows.
        var run = await store.CreateRunAsync(NewRun(suiteId));
        await store.CompleteRunAsync(new EvalRunCompletion
        {
            EvalRunId = run.Id,
            Status = EvalRunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Total = caseCount,
            Passed = caseCount,
            Failed = 0,
        });

        return run.Id;
    }

    private sealed class ThrowingEvalStore : IEvalStore
    {
        public ValueTask<IReadOnlyList<EvalSuite>> ListSuitesAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalSuite?> GetSuiteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalSuite> SaveSuiteAsync(EvalSuite suite, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<bool> DeleteSuiteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<IReadOnlyList<EvalCase>> ListCasesAsync(Guid suiteId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<IReadOnlyList<EvalCase>> ReplaceCasesAsync(Guid suiteId, IReadOnlyList<EvalCase> cases, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalCaseAddResult> AddCaseAsync(Guid suiteId, EvalCaseDraft draft, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalRun> CreateRunAsync(EvalRun run, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask MarkRunRunningAsync(Guid evalRunId, int? agentVersion, string? modelId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask CompleteRunAsync(EvalRunCompletion completion, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalRun?> GetRunAsync(string tenantId, Guid evalRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalRun?> GetRunByJobIdAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<IReadOnlyList<EvalRun>> QueryRunsAsync(EvalRunQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask RecordCaseResultAsync(EvalCaseResult result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<IReadOnlyList<EvalCaseResult>> ListCaseResultsAsync(string tenantId, Guid evalRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");

        public ValueTask<EvalRunDiff?> DiffRunsAsync(EvalRunDiffQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is down");
    }
}
