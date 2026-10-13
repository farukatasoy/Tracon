using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// End-to-end tests for automatic interrupted-run continuation (Phase 87).
/// </summary>
/// <remarks>
/// A real process crash cannot be simulated in-process; a "crashed" run is
/// SEEDED directly (a <c>Running</c> row with a stale heartbeat, a recorded
/// input, and a recorded tool call) — the same technique
/// <c>RunReconciliationTests</c> uses for plain orphan reconciliation. The
/// scenario is otherwise real: the job queue, the worker, and the compiled
/// agent all run for real.
/// </remarks>
public sealed class RunContinuationTests
{
    private const string AgentName = "continuable";
    private const string ModelId = "cont-1";
    private const string SessionId = "session-under-test";

    [Fact]
    public async Task Continuation_replays_the_completed_call_and_runs_the_new_one_live()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var sourceRunId = await SeedCrashedRunAsync(host);

        await host.Services.GetRequiredService<IRunStore>().RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = sourceRunId,
            ToolName = "get_order_status",
            Arguments = "orderId=ORD-7",
            Result = "RECORDED: in transit",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);

        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.ContinuedFromRunId.ShouldBe(sourceRunId);
        continuation.Status.ShouldBe(RunStatus.Completed);

        // 🚨 The FIRST call (ORD-7) has a recorded result; a live re-run would
        // ALSO increment this counter -- it must not. Only the SECOND call
        // (ORD-8), which was never recorded because it happened after the
        // simulated crash, actually runs.
        ContinuationProbeTools.Calls.ShouldBe(1);
        ContinuationProbeTools.LastLiveOrderId.ShouldBe("ORD-8");

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(continuationRunId))
        {
            events.Add(runEvent);
        }

        // The recorded (replayed) result must reach the model UNCHANGED —
        // this is the phase's first (and most likely) failure mode: if the
        // mismatch policy were not reversed for continuation, this run would
        // have stopped with a mismatch at the FIRST call instead of reaching Completed.
        events.Any(e => e.Type == RunEventType.ToolInvoked &&
            string.Equals(e.ToolName, "get_order_status", StringComparison.Ordinal))
            .ShouldBeTrue();

        // Source run is untouched (K-014): still Failed, no lineage pointing forward.
        var source = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(sourceRunId);
        source.ShouldNotBeNull();
        source.Status.ShouldBe(RunStatus.Failed);
    }

    /// <summary>
    /// F-352 class scan: the continuation pinned the interrupted run's version
    /// but dropped its experiment arm, so the second half of the turn counted
    /// in no arm.
    /// </summary>
    [Fact]
    public async Task Continuation_keeps_the_interrupted_runs_experiment_arm()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var experimentId = Guid.NewGuid();
        var sourceRunId = await SeedCrashedRunAsync(host, arm: (1, experimentId, "control"));

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);
        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.Status.ShouldBe(RunStatus.Completed);
        continuation.AgentVersion.ShouldBe(1);
        continuation.ExperimentId.ShouldBe(experimentId);
        continuation.Variant.ShouldBe("control");
    }

    /// <summary>
    /// F-369 class scan: the continuation opened no attribution scope, so the
    /// continued turn's tools and sub-agents ran under no user and no labels,
    /// and its own row carried neither.
    /// </summary>
    [Fact]
    public async Task A_continuation_runs_under_the_interrupted_runs_user_and_labels()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var sourceRunId = await SeedCrashedRunAsync(
            host,
            userId: "ada",
            labels: new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "payments" });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);
        var continuation = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId)).ShouldNotBeNull();

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);

        var scope = ContinuationProbeTools.LastLiveScope.ShouldNotBeNull();
        scope.RunId.ShouldBe(continuationRunId);
        scope.UserId.ShouldBe("ada");
        scope.Labels.ShouldNotBeNull().ShouldContainKeyAndValue("team", "payments");

        continuation.UserId.ShouldBe("ada");
        continuation.Labels.ShouldNotBeNull().ShouldContainKeyAndValue("team", "payments");
    }

    /// <summary>
    /// F-367: the continuation job runs with one attempt; a store error on the
    /// session save failed the job while the continued run had completed.
    /// </summary>
    [Fact]
    public async Task A_session_save_that_fails_once_does_not_fail_a_continuation()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(
            o =>
            {
                o.Enabled = true;
                o.MaxAttempts = 1;
            },
            configureTracon: static builder => builder.Services.ThrowFrom<ISessionStore>(
                nameof(ISessionStore.TryCreateAsync),
                static () => new TimeoutException("Injected: the session store did not answer in time."),
                times: 1));

        var sourceRunId = await SeedCrashedRunAsync(host);
        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.Completed);

        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(tenantId, continuationRunId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the continuation job to finish");

        job!.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync(SessionId)).ShouldNotBeNull();
    }

    /// <summary>
    /// F-370 class scan: a continued turn that asks for approval closed
    /// <c>AwaitingApproval</c>, but only the queued-run handler wrote the
    /// mailbox row, so nobody could answer it.
    /// </summary>
    [Fact]
    public async Task A_continuation_that_asks_for_approval_lists_it_and_the_decision_resumes_it()
    {
        var provider = new FakeModelProvider("cont-model")
            .CallsTool("cancel_order", new { orderId = "ORD-7" })
            .EchoesLastToolResult();

        await using var host = await StartHostAsync(
            o =>
            {
                o.Enabled = true;
                o.MaxAttempts = 1;
            },
            provider,
            static builder => builder.AddTool(
                (Func<string, string>)(orderId => $"{orderId} canceled."),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: static options => options.RequiresApproval = true));

        var sourceRunId = await SeedCrashedRunAsync(
            host,
            new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Give a short answer.",
                Model = new ModelBinding { Provider = "cont-model", Model = ModelId },
                ToolNames = ["cancel_order"],
            });

        var runs = host.Services.GetRequiredService<IRunStore>();

        var continuation = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId }))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is { Status: RunStatus.AwaitingApproval or RunStatus.Completed or RunStatus.Failed },
            $"a continuation of run '{sourceRunId}' to settle");

        continuation!.Status.ShouldBe(RunStatus.AwaitingApproval, continuation.Error?.Message);

        using var pending = await host.Client.GetAsync(new Uri("/tracon/api/approvals/pending", UriKind.Relative));
        var approval = (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem();

        approval.GetProperty("runId").GetGuid().ShouldBe(continuation.Id);
        approval.GetProperty("toolName").GetString().ShouldBe("cancel_order");

        using var decided = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approval.GetProperty("id").GetGuid()}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        decided.StatusCode.ShouldBe(HttpStatusCode.OK);

        var resumed = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId }))
                .FirstOrDefault(record => record.Id != sourceRunId && record.Id != continuation.Id),
            static record => record is { Status: RunStatus.AwaitingApproval or RunStatus.Completed or RunStatus.Failed },
            "the run resuming the continuation to settle");

        resumed!.Status.ShouldBe(RunStatus.Completed, resumed.Error?.Message);
    }

    [Fact]
    public async Task A_parameterized_agent_is_not_continued_with_values_its_run_never_had()
    {
        // The recorded input carries no parameter values; continuing would run
        // the turn with the defaults, not the source run's values.
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var sourceRunId = await SeedCrashedRunAsync(
            host,
            new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Help {{customer}} with the order.",
                Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" }],
                Model = new ModelBinding { Provider = "cont-model", Model = ModelId },
                ToolNames = ["get_order_status"],
            });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);
        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.Status.ShouldBe(RunStatus.Failed);
        continuation.Error.ShouldNotBeNull().Message.ShouldContain("declares parameters", Case.Sensitive);
        ContinuationProbeTools.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_parameterized_agent_is_continued_with_the_values_its_run_recorded()
    {
        // F-294: the interrupted run recorded the values it was bound with, and
        // the turn continues under the SAME instructions - not the defaults.
        ContinuationProbeTools.Reset();

        var provider = new FakeModelProvider("cont-model")
            .CallsTool("get_order_status", new { orderId = "ORD-7" })
            .CallsTool("get_order_status", new { orderId = "ORD-8" })
            .EchoesLastToolResult();

        await using var host = await StartHostAsync(
            o =>
            {
                o.Enabled = true;
                o.MaxAttempts = 1;
            },
            provider);

        var sourceRunId = await SeedCrashedRunAsync(
            host,
            new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Help {{customer}} with the order.",
                Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" }],
                Model = new ModelBinding { Provider = "cont-model", Model = ModelId },
                ToolNames = ["get_order_status"],
            },
            recordedParameters: new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Ada" });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);
        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);
        provider.Requests.ShouldNotBeEmpty();
        provider.Requests.ShouldAllBe(static request => string.Equals(request.Options!.Instructions, "Help Ada with the order.", StringComparison.Ordinal));

        // The continuation recorded the values it was bound with, so the chain goes on.
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var record = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(tenant, continuationRunId);

        record.ShouldNotBeNull();
        record.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Ada");
    }

    [Fact]
    public async Task Disabled_by_default_orphaned_run_stays_Failed_with_no_continuation()
    {
        await using var host = await StartHostAsync(static o => o.Enabled = false);

        var sourceRunId = await SeedCrashedRunAsync(host);

        await WaitForReconciliationPassAsync(host, sourceRunId);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var source = await runs.GetRunAsync(sourceRunId);

        source.ShouldNotBeNull();
        source.Status.ShouldBe(RunStatus.Failed);

        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_destructive_tool_call_blocks_continuation_and_records_why()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var sourceRunId = await SeedCrashedRunAsync(host);

        await host.Services.GetRequiredService<IRunStore>().RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = sourceRunId,
            ToolName = "delete_order",
            Arguments = "orderId=ORD-7",
            Result = "deleted",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var runs = host.Services.GetRequiredService<IRunStore>();

        var blocked = await WaitUntil.ValueAsync<RunEvent?>(
            async () =>
            {
                await foreach (var runEvent in runs.ReadEventsAsync(sourceRunId))
                {
                    if (runEvent.Type == RunEventType.RunContinuationBlocked)
                    {
                        return runEvent;
                    }
                }

                return null;
            },
            static runEvent => runEvent is not null,
            "a RunContinuationBlocked event on the source run");

        blocked.ShouldNotBeNull().Text.ShouldNotBeNull().ShouldContain("delete_order");

        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.Count.ShouldBe(1);
        ContinuationProbeTools.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_tool_declaring_SafeToRepeat_allows_continuation_despite_a_destructive_effect()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var sourceRunId = await SeedCrashedRunAsync(host);

        await host.Services.GetRequiredService<IRunStore>().RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = sourceRunId,
            ToolName = "idempotent_delete_order",
            Arguments = "orderId=ORD-7",
            Result = "deleted",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);
        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.ContinuedFromRunId.ShouldBe(sourceRunId);
    }

    [Fact]
    public async Task A_sessionless_run_is_never_continued()
    {
        await using var host = await StartHostAsync(o => o.Enabled = true);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),

            // No SessionId: nothing to continue.
        });

        await WaitForReconciliationPassAsync(host, runId);

        (await runs.GetRunAsync(runId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);

        var jobs = host.Services.GetRequiredService<IJobStore>();
        var jobsForRun = await jobs.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation });
        jobsForRun.ShouldBeEmpty();
    }

    [Fact]
    public async Task MaxAttempts_stops_a_second_continuation_in_the_same_chain()
    {
        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();

        // The chain's original run: already settled, not orphaned now.
        var ancestorId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = ancestorId,
            AgentName = AgentName,
            Status = RunStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
        });

        // A run that is ITSELF the chain's first continuation, now ALSO
        // crashed. With MaxAttempts = 1, this must NOT be continued again.
        var sourceRunId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = sourceRunId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
            ContinuedFromRunId = ancestorId,
        });

        await WaitForReconciliationPassAsync(host, sourceRunId);

        (await runs.GetRunAsync(sourceRunId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);

        // Exactly the two seeded rows -- no second continuation was opened.
        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.Count.ShouldBe(2);
    }

    /// <summary>
    /// F-344: continuation re-sends an agent turn's recorded input to its agent. A
    /// workflow root has no such turn - its row records parameter values and no
    /// message - so the reconciler opened a continuation that could only fail, under
    /// the workflow's name, in the workflow's session. A workflow run resumes from its
    /// checkpoints instead, and the refusal says so.
    /// </summary>
    [Fact]
    public async Task An_orphaned_workflow_root_is_not_continued_as_an_agent_turn_and_names_resume()
    {
        await using var host = await StartHostAsync(o => o.Enabled = true);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var rootId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
        });

        // What WorkflowRunner records for a workflow root: the values, no message.
        await host.Services.GetRequiredService<IRunInputStore>().SaveAsync(new RunInputRecord
        {
            RunId = rootId,
            TenantId = tenants.TenantId,
            Messages = [],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await WaitForReconciliationPassAsync(host, rootId);

        (await runs.GetRunAsync(rootId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);

        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.ShouldHaveSingleItem().Id.ShouldBe(rootId);

        (await host.Services.GetRequiredService<IJobStore>()
                .QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation }))
            .ShouldBeEmpty();

        var blocked = await ReadBlockedEventAsync(runs, rootId);

        blocked.ShouldNotBeNull("the refusal must be recorded on the workflow run")
            .Text.ShouldNotBeNull().ShouldContain($"POST /api/workflows/runs/{rootId}/resume", Case.Sensitive);
    }

    /// <summary>
    /// F-321 keeps one owner. With workflow recovery on, reconciliation still closes an
    /// orphaned workflow root and still does not continue it as an agent turn: the job
    /// that started the run resumes it from its checkpoint. Two owners would resume the
    /// same run twice. The refusal now names the job's recovery as well.
    /// </summary>
    [Fact]
    public async Task With_workflow_recovery_on_an_orphaned_workflow_root_is_still_not_continued_by_reconciliation()
    {
        await using var host = await StartHostAsync(
            o => o.Enabled = true,
            configureTracon: static builder => builder.UseWorkflows(static options => options.ResumeInterruptedRuns = true));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var rootId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
        });

        await WaitForReconciliationPassAsync(host, rootId);

        (await runs.GetRunAsync(rootId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);
        (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false }))
            .ShouldHaveSingleItem().Id.ShouldBe(rootId);
        (await host.Services.GetRequiredService<IJobStore>()
                .QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation }))
            .ShouldBeEmpty();

        (await ReadBlockedEventAsync(runs, rootId)).ShouldNotBeNull()
            .Text.ShouldNotBeNull().ShouldContain("ResumeInterruptedRuns", Case.Sensitive);
    }

    /// <summary>F-321: a workflow step (a sub-run) is not continued on its own with recovery on either.</summary>
    [Fact]
    public async Task With_workflow_recovery_on_an_orphaned_workflow_step_is_still_not_continued_on_its_own()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(
            o => o.Enabled = true,
            configureTracon: static builder => builder.UseWorkflows(static options => options.ResumeInterruptedRuns = true));

        var stepId = await SeedCrashedSubRunAsync(host, parentKind: RunKind.Workflow);

        await WaitForReconciliationPassAsync(host, stepId);

        await AssertSubRunNotContinuedAsync(host, stepId);
    }

    /// <summary>
    /// F-344 class scan: a step of a workflow is an agent run (<see cref="RunKind.Agent"/>)
    /// below the workflow root, and it carries the root's session id. Continued on its
    /// own, it re-ran the step - tools and all - in the workflow's session, with nobody
    /// waiting for its answer.
    /// </summary>
    [Fact]
    public async Task An_orphaned_workflow_step_is_not_continued_on_its_own()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var stepId = await SeedCrashedSubRunAsync(host, parentKind: RunKind.Workflow);

        await WaitForReconciliationPassAsync(host, stepId);

        await AssertSubRunNotContinuedAsync(host, stepId);
    }

    /// <summary>
    /// F-344 class scan: the same rule for a callable sub-agent's run below an agent.
    /// Its session id is the caller's, so a continuation ran the sub-agent on the
    /// caller's conversation and saved it there.
    /// </summary>
    [Fact]
    public async Task An_orphaned_sub_agent_run_is_not_continued_on_its_own()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var childId = await SeedCrashedSubRunAsync(host, parentKind: RunKind.Agent);

        await WaitForReconciliationPassAsync(host, childId);

        await AssertSubRunNotContinuedAsync(host, childId);
    }

    /// <summary>
    /// F-344: a continuation job that was already queued (by a version before the fix,
    /// or by hand) for a workflow run is refused by the handler too, with the same reason.
    /// </summary>
    [Fact]
    public async Task A_queued_continuation_of_a_workflow_run_is_refused_by_the_handler()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(static o => o.Enabled = false);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var rootId = TraconId.NewId();
        var continuationId = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = AgentName,
            Kind = RunKind.Workflow,
            WorkflowName = AgentName,
            Status = RunStatus.Failed,
            StartedAt = now.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
        });

        // An agent with the workflow's name exists and the input carries a message: the
        // handler must refuse on the kind, not on a missing definition or an empty input.
        await host.Services.GetRequiredService<IAgentDefinitionStore>()
            .SaveAsync(host.Services.GetServices<IStartupSeed>().Single().Definition);
        await host.Services.GetRequiredService<IRunInputStore>().SaveAsync(new RunInputRecord
        {
            RunId = rootId,
            TenantId = tenants.TenantId,
            Messages = [new ChatMessage(ChatRole.User, "where is ORD-7")],
            CreatedAt = now,
        });

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = continuationId,
            AgentName = AgentName,
            Status = RunStatus.Queued,
            StartedAt = now,
            TenantId = tenants.TenantId,
            SessionId = SessionId,
            ContinuedFromRunId = rootId,
        });

        using var payload = System.Text.Json.JsonDocument.Parse(
            $$"""{"sourceRunId":"{{rootId}}","continuationRunId":"{{continuationId}}"}""");

        await host.Services.GetRequiredService<IJobStore>().EnqueueAsync(
            new JobRecord
            {
                Id = continuationId,
                TenantId = tenants.TenantId,
                HandlerKey = JobHandlerKeys.RunContinuation,
                TargetName = AgentName,
                Status = JobStatus.Pending,
                Payload = payload.RootElement.Clone(),
                MaxAttempts = 1,
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        var continuation = await WaitUntil.ValueAsync(
            async () => await runs.GetRunAsync(continuationId),
            static record => record is { Status: RunStatus.Completed or RunStatus.Failed },
            "the queued continuation to settle");

        continuation.ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);
        continuation.Error.ShouldNotBeNull().Message.ShouldContain($"POST /api/workflows/runs/{rootId}/resume", Case.Sensitive);
        ContinuationProbeTools.Calls.ShouldBe(0);
    }

    /// <summary>
    /// F-344 class scan: the reconciler has no ambient tenant. The refusal event read the
    /// run's events through the ambient tenant to find the next sequence, read none for
    /// another tenant's run, collided with the claim's own RunFailed event at sequence 0
    /// and was dropped - the run never said why it was not continued.
    /// </summary>
    [Fact]
    public async Task The_refusal_is_recorded_on_a_workflow_run_of_another_tenant()
    {
        const string Tenant = "acme";

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var rootId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = rootId,
            AgentName = "podcast",
            Kind = RunKind.Workflow,
            WorkflowName = "podcast",
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = Tenant,
            SessionId = SessionId,
        });

        var blocked = await WaitUntil.ValueAsync(
            async () =>
            {
                using (AmbientTenantScope.Begin(Tenant))
                {
                    return await ReadBlockedEventAsync(runs, rootId);
                }
            },
            static runEvent => runEvent is not null,
            "a RunContinuationBlocked event on the other tenant's workflow run");

        blocked.ShouldNotBeNull().Text.ShouldNotBeNull().ShouldContain("/resume", Case.Sensitive);
    }

    /// <summary>
    /// F-344 class scan: the chain walk behind <see cref="TraconRunContinuationOptions.MaxAttempts"/>
    /// read each ancestor through the ambient tenant too. For another tenant it found
    /// none past the first link, so a chain already continued twice was continued a third time.
    /// </summary>
    [Fact]
    public async Task MaxAttempts_counts_every_link_of_another_tenants_chain()
    {
        ContinuationProbeTools.Reset();

        const string Tenant = "acme";

        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 2;
        });

        var runs = host.Services.GetRequiredService<IRunStore>();
        var firstId = TraconId.NewId();
        var secondId = TraconId.NewId();
        var sourceId = TraconId.NewId();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);

        // The original turn and its first continuation are settled; the second
        // continuation is the orphan. With MaxAttempts = 2 it is not continued again.
        await runs.StartRunAsync(Link(firstId, RunStatus.Failed, continuedFrom: null));
        await runs.StartRunAsync(Link(secondId, RunStatus.Failed, continuedFrom: firstId));
        await runs.StartRunAsync(Link(sourceId, RunStatus.Running, continuedFrom: secondId));

        await WaitForReconciliationPassAsync(host, sourceId, Tenant);

        using (AmbientTenantScope.Begin(Tenant))
        {
            var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });

            all.ShouldNotContain(record => record.ContinuedFromRunId == sourceId);
            all.Count.ShouldBe(3);
        }

        RunStartInfo Link(Guid runId, RunStatus status, Guid? continuedFrom)
            => new()
            {
                RunId = runId,
                AgentName = AgentName,
                Status = status,
                StartedAt = startedAt,
                TenantId = Tenant,
                SessionId = SessionId,
                ContinuedFromRunId = continuedFrom,
            };
    }

    private static async Task<Guid> SeedCrashedSubRunAsync(TraconTestHost host, RunKind parentKind)
    {
        foreach (var seed in host.Services.GetServices<IStartupSeed>())
        {
            await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(seed.Definition);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var parentId = TraconId.NewId();
        var childId = TraconId.NewId();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        // The parent is settled: only the sub-run is the orphan under test.
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = parentId,
            AgentName = parentKind == RunKind.Workflow ? "podcast" : "orchestrator",
            Kind = parentKind,
            WorkflowName = parentKind == RunKind.Workflow ? "podcast" : null,
            Status = RunStatus.Failed,
            StartedAt = startedAt,
            TenantId = tenants.TenantId,
            SessionId = SessionId,
        });

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = childId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = startedAt,
            TenantId = tenants.TenantId,
            SessionId = SessionId,
            ModelId = ModelId,
            ParentRunId = parentId,
            RootRunId = parentId,
            Depth = 1,
        });

        await host.Services.GetRequiredService<IRunInputStore>().SaveAsync(new RunInputRecord
        {
            RunId = childId,
            TenantId = tenants.TenantId,
            Messages = [new ChatMessage(ChatRole.User, "where is ORD-7, and check ORD-8 too")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return childId;
    }

    private static async Task AssertSubRunNotContinuedAsync(TraconTestHost host, Guid subRunId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        (await runs.GetRunAsync(subRunId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);

        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.ShouldNotContain(record => record.ContinuedFromRunId == subRunId);

        (await host.Services.GetRequiredService<IJobStore>()
                .QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation }))
            .ShouldBeEmpty();

        ContinuationProbeTools.Calls.ShouldBe(0);

        (await ReadBlockedEventAsync(runs, subRunId)).ShouldNotBeNull("the refusal must be recorded on the sub-run");
    }

    private static async Task<RunEvent?> ReadBlockedEventAsync(IRunStore runs, Guid runId)
    {
        await foreach (var runEvent in runs.ReadEventsAsync(runId))
        {
            if (runEvent.Type == RunEventType.RunContinuationBlocked)
            {
                return runEvent;
            }
        }

        return null;
    }

    private static async Task<TraconTestHost> StartHostAsync(
        Action<TraconRunContinuationOptions> configureContinuation,
        FakeModelProvider? provider = null,
        Action<ITraconBuilder>? configureTracon = null)
        => await TraconTestHost.StartAsync(
            builder =>
            {
                ConfigureAgent(builder, provider);
                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                services.Configure<RunReconciliationOptions>(static options =>
                {
                    options.Enabled = true;
                    options.ScanInterval = TimeSpan.FromMilliseconds(30);
                    options.OrphanThreshold = TimeSpan.FromMinutes(5);
                    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                });
                services.Configure(configureContinuation);
            });

    private static void ConfigureAgent(ITraconBuilder builder, FakeModelProvider? provider = null)
    {
        builder
            .AddModelProvider(provider ?? new FakeModelProvider("cont-model")
                .CallsTool("get_order_status", new { orderId = "ORD-7" })
                .CallsTool("get_order_status", new { orderId = "ORD-8" })
                .EchoesLastToolResult())
            .AddToolsFrom(typeof(ContinuationProbeTools));

        var definitions = builder.Services;

        definitions.AddSingleton<IStartupSeed>(new StartupSeed(new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = new ModelBinding { Provider = "cont-model", Model = ModelId },
            ToolNames = ["get_order_status", "delete_order", "idempotent_delete_order"],
        }));
    }

    private static async ValueTask<Guid> SeedCrashedRunAsync(
        TraconTestHost host,
        AgentDefinition? definition = null,
        (int Version, Guid ExperimentId, string Variant)? arm = null,
        IReadOnlyDictionary<string, string>? recordedParameters = null,
        string? userId = null,
        IReadOnlyDictionary<string, string>? labels = null)
    {
        // A given definition replaces the seed: it has to be the ONLY version
        // before the crashed run exists, or the reconciler (scanning every
        // 30 ms) may continue the run against the seed first.
        var definitions = definition is null
            ? host.Services.GetServices<IStartupSeed>().Select(static seed => seed.Definition).ToList()
            : [definition];

        foreach (var seed in definitions)
        {
            await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(seed);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
            ModelId = ModelId,
            AgentVersion = arm?.Version,
            ExperimentId = arm?.ExperimentId,
            Variant = arm?.Variant,
            UserId = userId,
            Labels = labels,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenants.TenantId,
            Messages = [new ChatMessage(ChatRole.User, "where is ORD-7, and check ORD-8 too")],
            Parameters = recordedParameters,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }

    private static async Task<Guid> WaitForContinuationAsync(TraconTestHost host, Guid sourceRunId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        var continuation = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false }))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is { Status: RunStatus.Completed or RunStatus.Failed },
            $"a continuation of run '{sourceRunId}' to settle");

        return continuation!.Id;
    }

    /// <summary>
    /// Waits until the reconciler has closed <paramref name="runId"/> AND
    /// finished the pass that decided whether to continue it.
    /// </summary>
    /// <remarks>
    /// Phase 184: these tests used to sleep 500 ms and then assert both halves.
    /// Under load the scanner might not have run at all - "the run is Failed"
    /// then failed, and "no continuation was opened" passed without proof. A
    /// pass first closes a run and then decides its continuation, and passes
    /// run one at a time. A second, sessionless orphan seeded only after the
    /// first run is closed is claimed by a LATER pass, so once it is closed
    /// too, the decision about the first run is complete. Being sessionless,
    /// it can never be continued itself, and the session filters below never
    /// see it.
    /// </remarks>
    private static async Task WaitForReconciliationPassAsync(TraconTestHost host, Guid runId, string? tenant = null)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();

        await WaitUntil.TrueAsync(
            async () =>
            {
                using (AmbientTenantScope.Begin(tenant ?? tenants.TenantId))
                {
                    return (await runs.GetRunAsync(runId))?.Status == RunStatus.Failed;
                }
            },
            $"the reconciler to close run {runId}");

        var sentinelId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = sentinelId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
        });

        await WaitUntil.TrueAsync(
            async () => (await runs.GetRunAsync(sentinelId))?.Status == RunStatus.Failed,
            "a later reconciliation pass to close the sentinel run");
    }

    /// <summary>Marker interface carrying definitions to be saved during startup.</summary>
    private interface IStartupSeed
    {
        AgentDefinition Definition { get; }
    }

    private sealed record StartupSeed(AgentDefinition Definition) : IStartupSeed;
}

/// <summary>
/// The tools for the continuation tests. Measures whether the body ACTUALLY ran.
/// </summary>
internal static class ContinuationProbeTools
{
    private static int _calls;
    private static string? _lastLiveOrderId;
    private static AgentRunScope? _lastLiveScope;

    /// <summary>How many times a tool body ran (as opposed to being replayed).</summary>
    public static int Calls => Volatile.Read(ref _calls);

    /// <summary>The order id of the most recent LIVE call.</summary>
    public static string? LastLiveOrderId => Volatile.Read(ref _lastLiveOrderId);

    /// <summary>The run scope the most recent LIVE call ran under.</summary>
    public static AgentRunScope? LastLiveScope => Volatile.Read(ref _lastLiveScope);

    /// <summary>Resets the counters.</summary>
    public static void Reset()
    {
        Volatile.Write(ref _calls, 0);
        Volatile.Write(ref _lastLiveOrderId, null);
        Volatile.Write(ref _lastLiveScope, null);
    }

    /// <summary>Returns an order's status.</summary>
    [TraconTool("get_order_status", "Returns an order's shipping status.")]
    public static string GetOrderStatus(string orderId)
    {
        Interlocked.Increment(ref _calls);
        Volatile.Write(ref _lastLiveOrderId, orderId);
        Volatile.Write(ref _lastLiveScope, TraconRunContext.Current);

        return $"{orderId}: RAN LIVE";
    }

    /// <summary>Deletes an order. Irreversible.</summary>
    [TraconTool("delete_order", "Deletes an order.", Effect = ToolEffect.Destructive)]
    public static string DeleteOrder(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId} deleted.";
    }

    /// <summary>
    /// Deletes an order. Irreversible, but carries its own idempotency key,
    /// so the tool's author declares it safe to repeat.
    /// </summary>
    [TraconTool("idempotent_delete_order", "Deletes an order (idempotent).", Effect = ToolEffect.Destructive, SafeToRepeat = true)]
    public static string IdempotentDeleteOrder(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId} deleted (idempotent).";
    }
}
