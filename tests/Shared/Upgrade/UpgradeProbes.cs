using System.Security.Cryptography;
using System.Text.Json;

namespace Tracon.UpgradeGate;

/// <summary>What a probe works with.</summary>
/// <param name="Database">The upgraded database.</param>
/// <param name="Seed">The rows the database was filled with.</param>
/// <param name="SeededColumns">The columns each table had when the rows were written; a column the schema of that moment lacked holds no seed value.</param>
/// <param name="OrdinalKeys">Whether the keys a client chooses compared by their characters when the rows were written.</param>
/// <param name="Strict">
/// Whether the value of every column is asserted. It is when the database stopped at the last release or later, so that the
/// release's schema, which the seed is written for, is complete. A database that stopped earlier lacks columns the seed names,
/// and there the probes only prove that every store reads every row it holds.
/// </param>
internal sealed record ProbeScope(
    IUpgradeDatabase Database,
    UpgradeSeed Seed,
    IReadOnlyDictionary<string, IReadOnlySet<string>> SeededColumns,
    bool OrdinalKeys,
    bool Strict)
{
    /// <summary>Gets the shared store context.</summary>
    public SqlStoreContext Context => Database.StoreContext;

    /// <summary>Gets the tenant the stores run as, and the tenant most of the seed belongs to.</summary>
    public ITenantContext Tenant => Database.TenantContext;

    /// <summary>Gets the tenant id as text.</summary>
    public string TenantId => Seed.Tenant;

    /// <summary>Gets the identifier a seed name stands for.</summary>
    /// <param name="name">The name written in the seed.</param>
    /// <returns>The GUID.</returns>
    public static Guid Id(string name) => UpgradeSeed.Id(name);

    /// <summary>Whether the column existed when the rows were written, and so holds the seed's value.</summary>
    /// <param name="table">The table.</param>
    /// <param name="column">The column.</param>
    /// <returns><see langword="true"/> when it did.</returns>
    public bool Had(string table, string column) => SeededColumns.TryGetValue(table, out var columns) && columns.Contains(column);
}

/// <summary>One read of the upgraded database through the store that owns the table.</summary>
/// <param name="Name">What is read.</param>
/// <param name="Tables">The tables the read touches; the probe runs when the database held them before the upgrade.</param>
/// <param name="Mutates">Whether the probe writes, which puts it after every read.</param>
/// <param name="Run">The read and what it expects. The expectations are the values of the seed, written out here.</param>
/// <param name="AlsoReads">Tables the probe reads that it does not need rows of in order to run (a table a later migration creates).</param>
internal sealed record UpgradeProbe(
    string Name,
    IReadOnlyList<string> Tables,
    bool Mutates,
    Func<ProbeScope, ValueTask> Run,
    IReadOnlyList<string>? AlsoReads = null);

/// <summary>
/// The reads of the gate: every list and get a store offers over the tables of the seed, with what the seed says they return.
/// </summary>
/// <remarks>
/// <para>
/// The expectations are the values in <c>UpgradeSeed.json</c>, not what a store returned on some earlier run, so a store
/// that reads an old row differently from how the release wrote it is a finding rather than a new truth.
/// </para>
/// <para>
/// Each probe reads first, which proves the store finds and maps every row at every stage, and asserts the values only
/// when <see cref="ProbeScope.Strict"/>: an older schema lacks columns the seed names, and what a row says in a column
/// that did not exist is the default the migration gave it.
/// </para>
/// </remarks>
internal static class UpgradeProbes
{
    private static readonly DateTimeOffset Never = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets every probe.</summary>
    public static IReadOnlyList<UpgradeProbe> All { get; } =
    [
        Read("tenants", ["tenants"], async scope =>
        {
            var tenants = await new SqlTenantStore(scope.Context).ListAsync();

            tenants.Count.ShouldBe(2);

            if (!scope.Strict)
            {
                return;
            }

            tenants.Single(static tenant => string.Equals(tenant.Slug, "acme", StringComparison.Ordinal)).DisplayName.ShouldBe("Acme Corp");
        }),

        Read("agent definitions", ["agent_definitions", "agent_definition_versions"], async scope =>
        {
            var store = new SqlAgentDefinitionStore(scope.Context, scope.Tenant);

            var support = (await store.GetAsync("support-agent")).ShouldNotBeNull();

            (await store.ListAsync()).Select(static agent => agent.Name).Order(StringComparer.Ordinal).ShouldBe(["planner", "support-agent"]);
            (await store.ListVersionsAsync("support-agent")).Select(static agent => agent.Version).Order().ShouldBe([1, 2, 3]);
            (await store.GetVersionAsync("support-agent", 2)).ShouldNotBeNull();

            // The two keys older rows spell differently: a list of names, and a flag that moved under the tool loop.
            support.CallableAgents.Select(static agent => agent.Name).ShouldBe(["planner"]);
            support.Model.ToolLoop.ShouldNotBeNull().AllowConcurrentToolCalls.ShouldBeTrue();
            (await store.GetAsync("planner")).ShouldNotBeNull().Model.ToolLoop.ShouldNotBeNull().AllowConcurrentToolCalls.ShouldBeFalse();

            if (!scope.Strict)
            {
                return;
            }

            support.Version.ShouldBe(3);
            support.DisplayName.ShouldBe("Support Agent");
            support.Model.Provider.ShouldBe("OpenAI");
            support.Model.Model.ShouldBe("gpt-5");
            support.Model.Temperature.ShouldBe(0.2f);
            support.ToolNames.ShouldBe(["lookup_order", "send_email"]);
            support.SkillNames.ShouldBe(["refund-policy"]);
            support.Metadata["team"].GetString().ShouldBe("support");
        }),

        Read("runs", ["runs"], async scope =>
        {
            var runs = new SqlRunStore(scope.Context, scope.Tenant);

            var completed = (await runs.GetRunAsync(ProbeScope.Id("run-completed"))).ShouldNotBeNull();
            var failed = (await runs.GetRunAsync(ProbeScope.Id("run-failed"))).ShouldNotBeNull();
            var child = (await runs.GetRunAsync(ProbeScope.Id("run-child"))).ShouldNotBeNull();
            var workflow = (await runs.GetRunAsync(ProbeScope.Id("run-workflow"))).ShouldNotBeNull();

            // Paged: every run once, whatever the page size.
            var seen = new List<Guid>();

            for (var skip = 0; ; skip += 2)
            {
                var page = await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Skip = skip, Take = 2 });

                seen.AddRange(page.Select(static run => run.Id));

                if (page.Count < 2)
                {
                    break;
                }
            }

            seen.Order().ShouldBe(new[] { "run-completed", "run-failed", "run-child", "run-workflow" }.Select(ProbeScope.Id).Order());

            // Costs are read back from the text the release wrote, whichever column type the provider gave them; a schema
            // from before the cost columns holds none.
            if (scope.Had("runs", "input_cost"))
            {
                completed.Cost.ShouldNotBeNull().InputCost.ShouldBe(0.015m);
            }

            completed.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(1500);

            if (!scope.Strict)
            {
                return;
            }

            completed.AgentName.ShouldBe("support-agent");
            completed.Status.ShouldBe(RunStatus.Completed);
            completed.SessionId.ShouldBe("session-alpha");
            completed.UserId.ShouldBe("alice");
            completed.Labels.ShouldNotBeNull()["team"].ShouldBe("support");
            completed.AgentVersion.ShouldBe(3);
            completed.ExperimentId.ShouldBe(ProbeScope.Id("exp-1"));
            completed.Variant.ShouldBe("candidate");
            completed.ModelId.ShouldBe("gpt-5");
            completed.ModelProvider.ShouldBe("OpenAI");
            completed.EventCount.ShouldBe(4);

            var usage = completed.Usage;

            usage.InputTokens.ShouldBe(1200);
            usage.OutputTokens.ShouldBe(300);
            usage.CachedInputTokens.ShouldBe(200);
            usage.ReasoningTokens.ShouldBe(50);

            var cost = completed.Cost.ShouldNotBeNull();

            cost.OutputCost.ShouldBe(0.009m);
            cost.CachedInputCost.ShouldBe(0.0005m);
            cost.InputPricePerMillionTokens.ShouldBe(1.25m);
            cost.Currency.ShouldBe("USD");
            cost.Source.ShouldBe(PricingSource.Catalog);

            // The columns the unreleased migrations add hold nothing for a row the release wrote.
            if (scope.Had("runs", "cache_write_input_tokens"))
            {
                usage.CacheWriteInputTokens.ShouldBe(100);
                cost.CacheWriteCost.ShouldBe(0.0003m);
                cost.CacheWriteLongPricePerMillionTokens.ShouldBe(6m);
            }
            else
            {
                usage.CacheWriteInputTokens.ShouldBeNull();
                cost.CacheWriteCost.ShouldBeNull();
                cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();
            }

            completed.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.002m);

            failed.Status.ShouldBe(RunStatus.Failed);
            failed.Error.ShouldNotBeNull().Type.ShouldBe("ProviderException");
            failed.Error.Class.ShouldBe(RunErrorClass.ProviderUnavailable);
            failed.Error.Fingerprint.ShouldBe("a1b2c3d4");
            failed.ReplayOfRunId.ShouldBe(ProbeScope.Id("run-completed"));
            failed.Cost.ShouldNotBeNull().Source.ShouldBe(PricingSource.Configuration);

            if (scope.Had("runs", "cancel_requested_at"))
            {
                failed.CancelRequestedAt.ShouldNotBeNull();
            }
            else
            {
                failed.CancelRequestedAt.ShouldBeNull();
            }

            child.ParentRunId.ShouldBe(ProbeScope.Id("run-completed"));
            child.Depth.ShouldBe(1);
            child.ContinuedFromRunId.ShouldBe(ProbeScope.Id("run-failed"));
            child.Status.ShouldBe(RunStatus.Running);

            workflow.Kind.ShouldBe(RunKind.Workflow);
            workflow.WorkflowName.ShouldBe("review-flow");
            workflow.Status.ShouldBe(RunStatus.AwaitingInput);

            (await runs.QueryRunsAsync(new RunQuery { AgentName = "support-agent", Status = RunStatus.Failed })).Single().Id.ShouldBe(ProbeScope.Id("run-failed"));
            (await runs.QueryRunsAsync(new RunQuery { LabelKey = "team", LabelValue = "support" })).Single().Id.ShouldBe(ProbeScope.Id("run-completed"));
            (await runs.QueryRunsAsync(new RunQuery { SessionId = "Session-Beta" })).Single().Id.ShouldBe(ProbeScope.Id("run-workflow"));

            var statistics = await runs.GetStatisticsAsync(new RunStatisticsQuery());

            statistics.CompletedRuns.ShouldBe(1);
            statistics.FailedRuns.ShouldBe(1);
            statistics.TotalTokens.ShouldBe(1555);
            statistics.ByAgent.Select(static agent => agent.AgentName).Order(StringComparer.Ordinal).ShouldBe(["planner", "review-flow", "support-agent"]);
        }),

        Read("run events", ["run_events"], async scope =>
        {
            var runs = new SqlRunStore(scope.Context, scope.Tenant);
            var events = new List<RunEvent>();

            await foreach (var runEvent in runs.ReadEventsAsync(ProbeScope.Id("run-completed")))
            {
                events.Add(runEvent);
            }

            events.Select(static runEvent => runEvent.Sequence).ShouldBe([1, 2, 3, 4]);
            (await runs.GetLastEventSequenceAsync(ProbeScope.Id("run-completed"))).ShouldBe(4);

            if (!scope.Strict)
            {
                return;
            }

            events[1].Type.ShouldBe(RunEventType.ToolInvoking);
            events[1].ToolName.ShouldBe("lookup_order");
            events[1].Payload.ShouldNotBeNull().ShouldContain("A-100");
            events[2].Text.ShouldBe("Your order ships tomorrow.");
            events[3].CustomType.ShouldBe("support.audit");
        }),

        Read("tool invocations", ["tool_invocations"], async scope =>
        {
            var runs = new SqlRunStore(scope.Context, scope.Tenant);
            var invocations = await runs.ListToolInvocationsAsync(ProbeScope.Id("run-completed"));

            invocations.Count.ShouldBe(2);

            if (!scope.Strict)
            {
                return;
            }

            var lookup = invocations.Single(static invocation => string.Equals(invocation.ToolName, "lookup_order", StringComparison.Ordinal));

            lookup.Arguments.ShouldNotBeNull().ShouldContain("A-100");
            lookup.Result.ShouldNotBeNull().ShouldContain("shipped");
            lookup.Duration.ShouldBe(TimeSpan.FromMilliseconds(120));
            lookup.Usage.ShouldNotBeNull().Unit.ShouldBe("request");
            lookup.Usage.Model.ShouldBe("orders-v2");

            var email = invocations.Single(static invocation => string.Equals(invocation.ToolName, "send_email", StringComparison.Ordinal));

            email.TimedOut.ShouldBeTrue();
            email.Error.ShouldBe("timed out");
            email.LateCompletedAt.ShouldNotBeNull();
        }),

        Read("non-token usage", ["run_non_token_usage"], async scope =>
        {
            var usage = (await new SqlRunStore(scope.Context, scope.Tenant).ListNonTokenUsageAsync(ProbeScope.Id("run-completed"))).Single();

            if (!scope.Strict)
            {
                return;
            }

            usage.NodeName.ShouldBe("search");
            usage.Usage.Unit.ShouldBe("request");
            usage.Usage.Quantity.ShouldBe(3m);
            usage.Usage.Provider.ShouldBe("Bing");
            usage.Usage.Model.ShouldBe("bing-v7");
        }),

        Read("sessions", ["sessions"], async scope =>
        {
            var sessions = new SqlSessionStore(scope.Context, scope.Tenant);

            var alpha = (await sessions.GetAsync("session-alpha")).ShouldNotBeNull();

            (await sessions.GetAsync("Session-Beta")).ShouldNotBeNull().Id.ShouldBe("Session-Beta");
            (await sessions.GetAsync("session-acme")).ShouldBeNull();
            (await sessions.QueryAsync(new SessionQuery { Take = 10 })).Count.ShouldBe(2);
            alpha.StateSchemaVersion.ShouldBe(1);
            alpha.State.GetProperty("stateBag").GetProperty("turns").GetInt32().ShouldBe(4);

            if (!scope.Strict)
            {
                return;
            }

            alpha.AgentName.ShouldBe("support-agent");
            alpha.Version.ShouldBe(7);
            alpha.OwnerId.ShouldBe("alice");
            alpha.StateMafVersion.ShouldBe("1.0.0");
            (await sessions.QueryAsync(new SessionQuery { AgentName = "planner", Take = 10 })).Single().Id.ShouldBe("Session-Beta");
            (await sessions.QueryAsync(new SessionQuery { OwnerId = "alice", Take = 10 })).Single().Id.ShouldBe("session-alpha");
        }),

        Read("workflow definitions", ["workflows", "workflow_definition_versions"], async scope =>
        {
            var workflows = new SqlWorkflowDefinitionStore(scope.Context);

            var flow = (await workflows.GetAsync(scope.TenantId, "review-flow")).ShouldNotBeNull();

            (await workflows.ListAsync(scope.TenantId)).Single().Name.ShouldBe("review-flow");
            (await workflows.ListVersionsAsync(scope.TenantId, "review-flow")).Select(static version => version.Version).Order().ShouldBe([1, 2]);
            (await workflows.GetVersionAsync(scope.TenantId, "review-flow", 1)).ShouldNotBeNull();

            // The shape of the release had no hand-off settings.
            flow.Handoffs.ShouldBeEmpty();
            flow.MaxHandoffs.ShouldBeNull();

            if (!scope.Strict)
            {
                return;
            }

            flow.Version.ShouldBe(2);
            flow.Kind.ShouldBe(WorkflowKind.Sequential);
            flow.AgentNames.ShouldBe(["support-agent", "planner"]);
            flow.Nodes.Select(static node => node.Name).ShouldBe(["support-agent", "planner"]);
            flow.Nodes[1].AgentVersion.ShouldBe(1);
            flow.MaxIterations.ShouldBe(3);
            flow.RunTimeout.ShouldBe(TimeSpan.FromMinutes(5));
            (await workflows.GetVersionAsync(scope.TenantId, "review-flow", 1)).ShouldNotBeNull().Description.ShouldBe("Drafts, then reviews");
        }),

        Read("workflow checkpoints", ["workflow_checkpoints"], async scope =>
        {
            var checkpoints = new SqlWorkflowCheckpointStore(scope.Context);

            var state = (await checkpoints.ReadAsync(scope.TenantId, "Session-Beta", "Checkpoint-A")).ShouldNotBeNull();

            state.GetProperty("superStep").GetInt32().ShouldBe(1);

            var listed = await checkpoints.ListAsync(scope.TenantId, "Session-Beta");

            listed.Select(static checkpoint => checkpoint.CheckpointId).Order(StringComparer.Ordinal).ShouldBe(["Checkpoint-A", "checkpoint-b"]);
            (await checkpoints.ListByRunAsync(scope.TenantId, ProbeScope.Id("run-workflow"))).Count.ShouldBe(2);

            if (!scope.Strict)
            {
                return;
            }

            listed.Single(static checkpoint => string.Equals(checkpoint.CheckpointId, "checkpoint-b", StringComparison.Ordinal)).ParentCheckpointId.ShouldBe("Checkpoint-A");
        }),

        Read("workflow requests and responses", ["workflow_requests", "workflow_responses"], async scope =>
        {
            var requests = new SqlWorkflowRequestStore(scope.Context, scope.Tenant);
            var responses = new SqlWorkflowResponseStore(scope.Context, scope.Tenant);

            var all = await requests.ListByRunAsync(ProbeScope.Id("run-workflow"));

            all.Count.ShouldBe(2);

            var request = all.Single(static candidate => string.Equals(candidate.RequestId, "req-1", StringComparison.Ordinal));

            (await requests.ListAsync(new WorkflowRequestQuery { State = WorkflowRequestState.Pending })).Count.ShouldBe(1);

            var response = (await responses.GetAsync(ProbeScope.Id("run-child"))).ShouldNotBeNull();

            (await responses.ListBySourceRunAsync(ProbeScope.Id("run-workflow"))).Count.ShouldBe(1);

            if (!scope.Strict)
            {
                return;
            }

            request.RequestId.ShouldBe("req-1");
            request.PortId.ShouldBe("review");
            request.Status.ShouldBe(WorkflowRequestStatus.Open);
            request.SessionId.ShouldBe("Session-Beta");

            var answered = all.Single(static candidate => string.Equals(candidate.RequestId, "req-2", StringComparison.Ordinal));

            answered.Status.ShouldBe(WorkflowRequestStatus.Closed);
            answered.AnsweredAt.ShouldNotBeNull();
            answered.ClosedAt.ShouldNotBeNull();
            answered.LastResponseRunId.ShouldBe(ProbeScope.Id("run-child"));
            response.RequestId.ShouldBe("req-1");
            response.ClaimScope.ShouldBe("user:alice");
            response.RespondedBy.ShouldBe("alice");
            (await responses.FindClaimAsync("user:alice", "req-1")).ShouldNotBeNull();
        }),

        Read("run inputs", ["run_inputs"], async scope =>
        {
            var inputs = new SqlRunInputStore(scope.Context);

            var input = (await inputs.GetAsync(scope.TenantId, ProbeScope.Id("run-completed"))).ShouldNotBeNull();

            input.Messages.Single().Text.ShouldBe("Where is my order A-100?");
            (await inputs.GetAsync(scope.TenantId, ProbeScope.Id("run-failed"))).ShouldNotBeNull().Parameters.ShouldBeNull();

            if (!scope.Strict)
            {
                return;
            }

            input.Parameters.ShouldNotBeNull()["locale"].ShouldBe("en");
        }),

        Read("evals", ["eval_suites", "eval_cases", "eval_runs", "eval_case_results"], async scope =>
        {
            var evals = new SqlEvalStore(scope.Context);

            (await evals.GetSuiteAsync(scope.TenantId, "refund-suite")).ShouldNotBeNull().AgentName.ShouldBe("support-agent");
            (await evals.ListSuitesAsync(scope.TenantId)).Count.ShouldBe(1);

            var cases = await evals.ListCasesAsync(ProbeScope.Id("suite-1"));

            cases.Select(static evalCase => evalCase.Seq).ShouldBe([1, 2]);

            var run = (await evals.GetRunAsync(scope.TenantId, ProbeScope.Id("eval-run-1"))).ShouldNotBeNull();

            (await evals.QueryRunsAsync(new EvalRunQuery { TenantId = scope.TenantId, SuiteId = ProbeScope.Id("suite-1"), Take = 10 })).Count.ShouldBe(1);

            var results = await evals.ListCaseResultsAsync(scope.TenantId, ProbeScope.Id("eval-run-1"));

            results.Count.ShouldBe(2);

            if (!scope.Strict)
            {
                return;
            }

            cases[0].Query.ShouldBe("Can I get a refund?");
            cases[0].SourceKind.ShouldBe(EvalCaseSource.FailedRun);
            cases[0].SourceRunId.ShouldBe(ProbeScope.Id("run-failed"));
            cases[0].Parameters.ShouldNotBeNull()["locale"].ShouldBe("en");
            cases[0].ExpectedToolCalls.Single().Name.ShouldBe("lookup_order");
            cases[1].ExpectedToolCalls.ShouldBeEmpty();
            run.Status.ShouldBe(EvalRunStatus.Completed);
            run.Total.ShouldBe(2);
            run.AgentVersion.ShouldBe(3);
            (await evals.GetRunByJobIdAsync(scope.TenantId, ProbeScope.Id("job-1"))).ShouldNotBeNull().Id.ShouldBe(run.Id);
            results.Single(static result => result.Passed).Output.ShouldBe("Yes, within 30 days.");
            results.Single(static result => !result.Passed).FailureReason.ShouldBe("no output");
        }),

        Read("experiments", ["experiments"], async scope =>
        {
            var experiments = new SqlExperimentStore(scope.Context, scope.Tenant);

            var running = (await experiments.GetRunningAsync(scope.TenantId, "support-agent")).ShouldNotBeNull();

            (await experiments.GetAsync(scope.TenantId, "draft-experiment")).ShouldNotBeNull().Status.ShouldBe(ExperimentStatus.Draft);
            (await experiments.ListAsync(scope.TenantId)).Count.ShouldBe(3);

            if (!scope.Strict)
            {
                return;
            }

            var stopped = (await experiments.GetAsync(scope.TenantId, "stopped-experiment")).ShouldNotBeNull();

            stopped.Status.ShouldBe(ExperimentStatus.Stopped);
            stopped.EndedAt.ShouldNotBeNull();
            stopped.RollbackReason.ShouldBe("error rate above the limit");
            running.Name.ShouldBe("refund-wording");
            running.Variants.Select(static variant => (variant.Name, variant.Version, variant.Weight)).ShouldBe([("control", 2, 50), ("candidate", 3, 50)]);
            running.Canary.ShouldNotBeNull().CanaryVariant.ShouldBe("candidate");
            running.Canary.MinSampleSize.ShouldBe(10);
            (await experiments.ListRunningWithCanaryAsync()).Single().Name.ShouldBe("refund-wording");
        }),

        Read("schedules and jobs", ["job_schedules", "jobs", "job_items"], async scope =>
        {
            var schedules = new SqlJobScheduleStore(scope.Context);
            var jobs = new SqlJobStore(scope.Context);

            // The handler of a job used to be a number in a column of its own; the migration turned it into the key.
            var schedule = (await schedules.GetAsync(scope.TenantId, "nightly-report")).ShouldNotBeNull();

            schedule.HandlerKey.ShouldBe("tracon.agent-run");
            (await schedules.ListAsync(scope.TenantId)).Count.ShouldBe(1);

            (await jobs.GetAsync(scope.TenantId, ProbeScope.Id("job-1"))).ShouldNotBeNull().HandlerKey.ShouldBe("tracon.agent-batch");
            (await jobs.GetAsync(scope.TenantId, ProbeScope.Id("job-2"))).ShouldNotBeNull().HandlerKey.ShouldBe("tracon.workflow");
            (await jobs.QueryAsync(new JobQuery { TenantId = scope.TenantId, Take = 10 })).Count.ShouldBe(3);
            var items = await jobs.ListItemsAsync(ProbeScope.Id("job-1"));

            items.Select(static item => item.Seq).ShouldBe([1, 2]);
            (await jobs.GetQueueDepthAsync()).ShouldNotBeNull();

            if (!scope.Strict)
            {
                return;
            }

            items[1].Status.ShouldBe(JobItemStatus.Failed);
            items[1].Error.ShouldBe("boom");
            (await jobs.GetAsync(scope.TenantId, ProbeScope.Id("job-3"))).ShouldNotBeNull().ErrorMessage.ShouldBe("handler failed after three attempts");
            schedule.Cron.ShouldBe("0 2 * * *");
            schedule.TimeZone.ShouldBe("Europe/Istanbul");
            schedule.Lane.ShouldBe("default");
            (await schedules.ListDueAsync(Never)).Count.ShouldBe(1);

            var job = (await jobs.GetAsync(scope.TenantId, ProbeScope.Id("job-1"))).ShouldNotBeNull();

            job.Status.ShouldBe(JobStatus.Completed);
            job.ScheduleId.ShouldBe(ProbeScope.Id("sched-1"));
            job.MaxAttempts.ShouldBe(3);
            job.MaxDuration.ShouldBe(TimeSpan.FromMinutes(1));
            job.DoneItems.ShouldBe(2);

            var leased = (await jobs.GetAsync(scope.TenantId, ProbeScope.Id("job-2"))).ShouldNotBeNull();

            leased.Lane.ShouldBe("slow");
            leased.LeaseOwner.ShouldBe("node-1");
            (await jobs.QueryAsync(new JobQuery { TenantId = scope.TenantId, Status = JobStatus.Leased, Take = 10 })).Single().Id.ShouldBe(ProbeScope.Id("job-2"));
        }),

        Read("approval rules and pending approvals", ["tool_approval_rules", "pending_approvals"], async scope =>
        {
            var rules = await new SqlToolApprovalRuleStore(scope.Context).ListAsync(scope.TenantId);

            rules.Count.ShouldBe(2);

            var rule = rules.Single(static candidate => string.Equals(candidate.ToolName, "send_email", StringComparison.Ordinal));

            var approvals = new SqlPendingApprovalStore(scope.Context, scope.Tenant);
            var approval = (await approvals.GetAsync(ProbeScope.Id("approval-1"))).ShouldNotBeNull();

            (await approvals.ListPendingAsync()).Single().Id.ShouldBe(approval.Id);

            if (!scope.Strict)
            {
                return;
            }

            rules.Single(static candidate => string.Equals(candidate.ToolName, "lookup_order", StringComparison.Ordinal)).ArgumentsHash.ShouldBe("9f86d081884c7d659a2feaa0c55ad015");
            rule.AgentName.ShouldBe("support-agent");
            rule.ArgumentConditions.Single().Path.ShouldBe("to");
            rule.ArgumentConditions.Single().Value.GetString().ShouldBe("a@example.com");
            var decided = (await approvals.GetAsync(ProbeScope.Id("approval-2"))).ShouldNotBeNull();

            decided.Status.ShouldBe(ApprovalStatus.Approved);
            decided.DecidedBy.ShouldBe("bob");
            decided.DecidedAt.ShouldNotBeNull();
            approval.RequestId.ShouldBe("req-approval-1");
            approval.Status.ShouldBe(ApprovalStatus.Pending);
            approval.Presentation.ShouldNotBeNull().EntityName.ShouldBe("Support Agent");
            approval.Arguments.ShouldNotBeNull().ShouldContain("a@example.com");
        }),

        Read("mcp servers", ["mcp_servers"], async scope =>
        {
            var servers = new SqlMcpServerStore(scope.Context);

            var docs = (await servers.GetAsync(scope.TenantId, "Docs-Server")).ShouldNotBeNull();
            var oauth = (await servers.GetAsync(scope.TenantId, "oauth-server")).ShouldNotBeNull();

            (await servers.ListAsync(scope.TenantId)).Count.ShouldBe(2);

            // The column that held the key of the Authorization header went into the map of header keys when it was dropped.
            if (scope.Had("mcp_servers", "authorization_configuration_key"))
            {
                docs.HeaderConfigurationKeys["Authorization"].ShouldBe("Tracon:McpSecrets:Docs");
            }

            oauth.HeaderConfigurationKeys.ShouldBeEmpty();

            if (!scope.Strict)
            {
                return;
            }

            docs.Endpoint.ToString().ShouldBe("https://mcp.example.com/mcp");
            docs.Transport.ShouldBe(McpTransportMode.StreamableHttp);
            docs.HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:McpSecrets:DocsKey");
            oauth.OAuthEnabled.ShouldBeTrue();
            oauth.OAuthClientId.ShouldBe("client-1");
            oauth.OAuthScopes.ShouldBe("read write");
            oauth.RequiresApproval.ShouldBeTrue();
        }),

        Read("webhooks", ["webhook_subscriptions", "webhook_deliveries"], async scope =>
        {
            var webhooks = new SqlWebhookStore(scope.Context);

            var subscription = (await webhooks.GetSubscriptionAsync(scope.TenantId, "Run-Events")).ShouldNotBeNull();

            (await webhooks.ListSubscriptionsAsync(scope.TenantId)).Count.ShouldBe(1);
            (await webhooks.FindForEventAsync(scope.TenantId, "run.failed")).Single().Id.ShouldBe(subscription.Id);

            var delivery = (await webhooks.GetDeliveryAsync(ProbeScope.Id("delivery-1"))).ShouldNotBeNull();

            (await webhooks.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = scope.TenantId, Take = 10 })).Count.ShouldBe(2);

            if (!scope.Strict)
            {
                return;
            }

            subscription.Events.ShouldBe(["run.completed", "run.failed"]);
            subscription.SecretConfigurationKey.ShouldBe("Tracon:Webhooks:Secret");
            subscription.HeaderConfigurationKeys["X-Token"].ShouldBe("Tracon:Webhooks:Token");
            subscription.ConsecutiveFailures.ShouldBe(1);
            delivery.Status.ShouldBe(WebhookDeliveryStatus.Delivered);
            delivery.ResponseCode.ShouldBe(200);
            delivery.Payload.ShouldContain("completed");
            (await webhooks.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = scope.TenantId, Status = WebhookDeliveryStatus.Failed, Take = 10 }))
                .Single().Error.ShouldBe("server error");
        }),

        Read("skills", ["agent_skills", "agent_skill_resources", "agent_skill_scripts", "skill_script_grants"], async scope =>
        {
            var skills = new SqlAgentSkillStore(scope.Context);

            var skill = (await skills.GetAsync(scope.TenantId, "refund-policy")).ShouldNotBeNull();

            skill.Resources.Single().Name.ShouldBe("policy.md");
            skill.Scripts.Single().Name.ShouldBe("calc");
            (await skills.ListAsync(scope.TenantId)).Count.ShouldBe(1);

            var grants = new SqlSkillScriptGrantStore(scope.Context);

            var granted = await grants.ListAsync(scope.TenantId);

            granted.Count.ShouldBe(2);

            var grant = granted.Single(static candidate => string.Equals(candidate.ScriptName, "calc", StringComparison.Ordinal));

            if (!scope.Strict)
            {
                return;
            }

            skill.Instructions.ShouldBe("Refund within 30 days.");
            skill.Compatibility.ShouldBe("claude-code");
            granted.Single(static candidate => candidate.ScriptName is null).RevokedAt.ShouldNotBeNull();
            skill.AllowedTools.ShouldBe("lookup_order");
            skill.Version.ShouldBe(2);
            skill.Metadata["owner"].GetString().ShouldBe("support");
            grant.ContentHash.ShouldNotBeNull().Length.ShouldBe(64);
            (await grants.FindActiveAsync(scope.TenantId, "refund-policy", "calc", Never.AddYears(-3))).ShouldNotBeNull();
        }),

        Read("quotas", ["quotas", "quota_usage"], async scope =>
        {
            var quotas = new SqlQuotaStore(scope.Context);

            var definitions = await quotas.ListAsync(scope.TenantId);

            definitions.Count.ShouldBe(2);
            (await quotas.GetAsync(scope.TenantId, ProbeScope.Id("quota-1"))).ShouldNotBeNull();

            var usage = (await quotas.GetUsageAsync(new QuotaUsageQuery { TenantId = scope.TenantId, AgentName = "support-agent" })).Single();

            if (!scope.Strict)
            {
                return;
            }

            var perAgent = definitions.Single(static quota => string.Equals(quota.AgentName, "support-agent", StringComparison.Ordinal));

            perAgent.Period.ShouldBe(QuotaPeriod.Monthly);
            perAgent.MaxRuns.ShouldBe(1000);
            perAgent.MaxTokens.ShouldBe(5000000);
            perAgent.MaxCost.ShouldBe(50m);
            definitions.Single(static quota => quota.AgentName is null).Enabled.ShouldBeFalse();
            usage.Runs.ShouldBe(2);
            usage.Tokens.ShouldBe(1540);
            usage.Cost.ShouldBe(0.024m);
            usage.PeriodStart.ShouldBe(new DateOnly(2026, 3, 1));
        }),

        Read("voice sessions", ["voice_sessions"], async scope =>
        {
            var voice = (await new SqlVoiceSessionStore(scope.Context).QueryAsync(scope.TenantId, new VoiceSessionQuery { Take = 10 })).Single();

            voice.SessionId.ShouldBe("session-alpha");

            if (!scope.Strict)
            {
                return;
            }

            voice.Turns.ShouldBe(3);
            voice.InputSeconds.ShouldBe(42.5m);
            voice.LiveSeconds.ShouldBe(60m);
            voice.EndReason.ShouldBe(VoiceSessionEndReason.Client);
            voice.Cost.ShouldNotBeNull().DurationCost.ShouldBe(0.01m);
            voice.Cost.Currency.ShouldBe("USD");
        }),

        Read("audit log", ["audit_log"], async scope =>
        {
            var audit = new SqlAuditLog(scope.Context, scope.Tenant);

            var entries = await audit.QueryAsync(new AuditQuery { TenantId = scope.TenantId, Limit = 10 });

            entries.Count.ShouldBe(3);

            // The chain the release wrote verifies under the rules of today: plain SHA-256, no key.
            var verification = await audit.VerifyChainAsync(new AuditChainQuery { TenantId = scope.TenantId });

            verification.Status.ShouldBe(AuditChainStatus.Valid);

            // A row written before the chain existed has no hash, and nothing links it.
            if (scope.Had("audit_log", "hash"))
            {
                verification.EntriesChecked.ShouldBe(3);
            }

            if (!scope.Strict)
            {
                return;
            }

            entries.Single(static entry => string.Equals(entry.Action, "update", StringComparison.Ordinal)).Before.ShouldBe("{\"version\":1}");
            entries.ShouldAllBe(static entry => entry.KeyId == null);
        }),

        Read("api keys", ["api_keys"], async scope =>
        {
            var keys = new SqlApiKeyStore(scope.Context);

            var listed = await keys.ListAsync(scope.TenantId);

            listed.Count.ShouldBe(2);
            (await keys.FindByHashAsync(SHA256.HashData("seed-api-key-1"u8))).ShouldNotBeNull();

            if (!scope.Strict)
            {
                return;
            }

            listed.Single(static key => string.Equals(key.Name, "ci-key", StringComparison.Ordinal)).Scopes.ShouldBe([ApiKeyScope.RunsRead, ApiKeyScope.AgentsRead]);
            listed.Single(static key => string.Equals(key.Name, "old-key", StringComparison.Ordinal)).IsActive.ShouldBeFalse();
            (await keys.FindByHashAsync(SHA256.HashData("seed-api-key-1"u8))).ShouldNotBeNull().Name.ShouldBe("ci-key");
        }),

        Read("tenant bindings and policies", ["tenant_provider_bindings", "tenant_egress_policies", "tenant_tool_policies"], async scope =>
        {
            var bindings = new SqlTenantProviderBindingStore(scope.Context);

            var openai = (await bindings.GetAsync(scope.TenantId, "openai")).ShouldNotBeNull();

            (await bindings.ListAsync(scope.TenantId)).Count.ShouldBe(2);

            var egress = (await new SqlTenantEgressPolicyStore(scope.Context).GetAsync(scope.TenantId)).ShouldNotBeNull();
            var tools = (await new SqlTenantToolPolicyStore(scope.Context).GetAsync(scope.TenantId)).ShouldNotBeNull();

            if (!scope.Strict)
            {
                return;
            }

            openai.ApiKeyConfigurationName.ShouldBe("Tracon:Tenants:default:OpenAI:ApiKey");
            (await bindings.GetAsync(scope.TenantId, "anthropic")).ShouldNotBeNull().Endpoint.ShouldBe("https://api.anthropic.example.com");
            egress.AllowedProviders.ShouldBe(["openai", "anthropic"]);
            tools.AllowedTools.ShouldBe(["lookup_order", "send_email"]);
            tools.GrantedPermissions.ShouldBe(["orders.read"]);
        }),

        Read("inbound triggers", ["inbound_triggers"], async scope =>
        {
            var triggers = new SqlInboundTriggerStore(scope.Context);

            var trigger = (await triggers.GetAsync(scope.TenantId, "Github-Push")).ShouldNotBeNull();

            (await triggers.ListAsync(scope.TenantId)).Count.ShouldBe(1);

            if (!scope.Strict)
            {
                return;
            }

            trigger.TargetKind.ShouldBe(InboundTriggerTargetKind.Agent);
            trigger.PayloadMode.ShouldBe(InboundTriggerPayloadMode.Path);
            trigger.PayloadPath.ShouldBe("$.head_commit.message");
            trigger.SigningSecretConfigurationName.ShouldBe("Tracon:Triggers:GithubSecret");
        }),

        Read("attachments", ["attachments"], async scope =>
        {
            var attachments = new SqlAttachmentStore(scope.Context);

            var descriptor = (await attachments.GetAsync(scope.TenantId, ProbeScope.Id("attachment-1"))).ShouldNotBeNull();

            await using var stream = (await attachments.OpenReadAsync(scope.TenantId, descriptor.Id)).ShouldNotBeNull();

            var bytes = new byte[4];
            await stream.ReadExactlyAsync(bytes);
            bytes.ShouldBe("%PDF"u8.ToArray());

            (await attachments.ListAsync(new AttachmentQuery { TenantId = scope.TenantId, SessionId = "session-alpha", Take = 10 })).Count.ShouldBe(1);
            (await attachments.GetAsync(scope.TenantId, ProbeScope.Id("attachment-external"))).ShouldNotBeNull().FileName.ShouldBe("big.zip");

            if (!scope.Strict)
            {
                return;
            }

            descriptor.FileName.ShouldBe("invoice.pdf");
            descriptor.ByteSize.ShouldBe(4);
            descriptor.RunId.ShouldBe(ProbeScope.Id("run-completed"));
        }),

        Read("agent files", ["agent_files"], async scope =>
        {
            var files = new SqlAgentFileStore(scope.Context, scope.Tenant);

            (await files.ReadAsync(scope.TenantId, "/Notes", CancellationToken.None)).ShouldNotBeEmpty();
        }),

        Read("idempotency keys", ["idempotency_keys"], async scope =>
        {
            var keys = new SqlIdempotencyStore(scope.Context);

            // The key that completed answers with the stored response; the one that did not is still in progress.
            var completed = await keys.ReserveAsync(new IdempotencyRequest { TenantId = scope.TenantId, Key = "Key-One", Fingerprint = "fp-abc", CreatedAt = Never });
            completed.State.ShouldBe(IdempotencyState.Completed);

            // The second spelling is a row of its own only where the keys compare by their characters.
            if (scope.OrdinalKeys)
            {
                (await keys.ReserveAsync(new IdempotencyRequest { TenantId = scope.TenantId, Key = "key-one", Fingerprint = "fp-def", CreatedAt = Never.AddYears(-3) }))
                    .State.ShouldBe(IdempotencyState.InProgress);
            }

            if (!scope.Strict)
            {
                return;
            }

            completed.Response.ShouldNotBeNull().StatusCode.ShouldBe(200);

            if (scope.Had("idempotency_keys", "headers"))
            {
                completed.Response.Headers.ShouldNotBeNull()["Location"].ShouldBe("/runs/1");
            }
        }),

        Read("run scores", ["run_scores"], async scope =>
        {
            var scores = await new SqlRunScoreStore(scope.Context, scope.Tenant).ListAsync(scope.TenantId, ProbeScope.Id("run-completed"));

            scores.Count.ShouldBe(2);

            // The name of a judge's score is the part of its author after the prefix.
            scores.ShouldContain(static score => string.Equals(score.Name, "tone", StringComparison.Ordinal));

            if (!scope.Strict)
            {
                return;
            }

            var quality = scores.Single(static score => string.Equals(score.Name, "quality", StringComparison.Ordinal));

            quality.Kind.ShouldBe(RunScoreKind.Stars);
            quality.Value.ShouldBe(4);
            quality.MessageId.ShouldBe("msg-1");
            quality.Author.ShouldBe("alice");

            var tone = scores.Single(static score => string.Equals(score.Name, "tone", StringComparison.Ordinal));

            tone.Kind.ShouldBe(RunScoreKind.Categorical);
            tone.TextValue.ShouldBe("polite");
            tone.EvaluatorVersion.ShouldBe("tone@2");
        }),

        Read("traces", ["traces", "spans"], async scope =>
        {
            var trace = (await new SqlTraceStore(scope.Context, scope.Tenant).GetTraceByRunAsync(ProbeScope.Id("run-completed"))).ShouldNotBeNull();

            trace.Spans.Count.ShouldBe(2);

            if (!scope.Strict)
            {
                return;
            }

            trace.TraceId.ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
            trace.Spans.Single(static span => string.Equals(span.Name, "agent.run", StringComparison.Ordinal)).Attributes["tokens"].ShouldBe("1500");
            trace.Spans.Single(static span => string.Equals(span.Name, "tool.lookup_order", StringComparison.Ordinal)).ParentId.ShouldBe(ProbeScope.Id("span-1"));
        }),

        Read("retention", ["retention_policies", "retention_runs", "run_events"], async scope =>
        {
            var retention = new SqlRetentionPolicyStore(scope.Context);

            (await retention.GetPolicyAsync(scope.TenantId, "run_events")).ShouldNotBeNull();
            (await retention.ListPoliciesAsync(scope.TenantId)).Count.ShouldBe(2);

            var runs = await retention.ListRunsAsync(scope.TenantId, "run_events", 0, 10);

            runs.Count.ShouldBe(1);
            (await retention.ListRunsAsync(scope.TenantId, "sessions", 0, 10)).Count.ShouldBe(1);

            // The events of a run count once the run has completed: four events of the completed run, none of the run still waiting.
            (await new SqlRetentionStore(scope.Context).CountOlderThanAsync(RetentionTargets.RunEvents, scope.TenantId, Never)).ShouldBe(4);

            if (!scope.Strict)
            {
                return;
            }

            (await retention.GetPolicyAsync(scope.TenantId, "run_events"))!.MaxAgeDays.ShouldBe(90);
            (await retention.GetPolicyAsync(scope.TenantId, "run_events"))!.Archive.ShouldBeTrue();
            runs.Single().DeletedRows.ShouldBe(10);
            (await retention.GetPolicyAsync(scope.TenantId, "sessions"))!.MaxRows.ShouldBe(1000);
            (await retention.ListRunsAsync(scope.TenantId, "sessions", 0, 10)).Single().Error.ShouldBe("disk full");
        }),

        Read("data subject export", ["conversations", "conversation_items", "responses", "sessions", "runs"], async scope =>
        {
            var export = await new SqlDataSubjectStore(scope.Context).ExportAsync(
                scope.TenantId,
                new DataSubjectScope { SessionIds = ["session-alpha"], RunIds = [ProbeScope.Id("run-completed")], ConversationIds = [ProbeScope.Id("conv-1")] });

            using var document = JsonDocument.Parse(export.Json);

            export.Json.ShouldContain("Where is my order?");
            export.Json.ShouldContain("resp_seed_1");
        }),

        Write("singleton leases", ["singleton_leases"], async scope =>
        {
            var leases = new SqlSingletonLeaseStore(scope.Context);

            // The lease the release left is held until its expiry, by its owner.
            (await leases.TryAcquireAsync("retention", "node-2", TimeSpan.FromMinutes(1))).ShouldBeFalse();
            (await leases.RenewAsync("retention", "node-1", TimeSpan.FromMinutes(1))).ShouldBeTrue();
        }),

        Write("rate limit counters", ["rate_limit_counters"], async scope =>
        {
            var window = new DateTimeOffset(2026, 3, 1, 10, 53, 0, TimeSpan.Zero);

            (await new SqlRateLimitCounterStore(scope.Context).IncrementAsync("endpoint:Tracon:tenant:Default", window)).Count.ShouldBe(4);
        }),

        Write("conversation branch", ["conversations", "conversation_items"], async scope =>
        {
            var branch = (await new SqlConversationBranchStore(scope.Context).BranchAsync(scope.TenantId, ProbeScope.Id("conv-1"), 1)).ShouldNotBeNull();

            branch.BranchFromSequence.ShouldBe(1);
            branch.CopiedItemCount.ShouldBe(1);
        }),

        Write("agent version counter", ["agent_definitions"], alsoReads: ["agent_definition_version_counters"], run: async scope =>
        {
            var store = new SqlAgentDefinitionStore(scope.Context, scope.Tenant);

            var support = (await store.GetAsync("support-agent")).ShouldNotBeNull();

            // The next number comes from the counter the release kept (or the migration seeded from the versions), not from the rows.
            (await store.SaveAsync(support)).Version.ShouldBe(4);
            (await store.ListVersionsAsync("support-agent")).Count.ShouldBe(4);
        }),
    ];

    /// <summary>Gets the tables some probe reads.</summary>
    public static IReadOnlySet<string> ProbedTables { get; } =
        All.SelectMany(static probe => probe.Tables.Concat(probe.AlsoReads ?? [])).ToHashSet(StringComparer.Ordinal);

    /// <summary>Runs every probe whose tables the database held before the upgrade, reads before writes.</summary>
    /// <param name="database">The upgraded database.</param>
    /// <param name="seed">The seed it was filled with.</param>
    /// <param name="outcome">What the seed wrote.</param>
    /// <param name="strict">Whether the value of every column is asserted (see <see cref="ProbeScope"/>).</param>
    /// <returns>One line per failing probe; empty when every store reads every row.</returns>
    public static async ValueTask<List<string>> RunAsync(IUpgradeDatabase database, UpgradeSeed seed, SeedOutcome outcome, bool strict)
    {
        var seeded = outcome.Rows.Keys.ToHashSet(StringComparer.Ordinal);
        var scope = new ProbeScope(database, seed, outcome.Columns, outcome.OrdinalKeys, strict);
        var failures = new List<string>();

        foreach (var probe in All.OrderBy(static probe => probe.Mutates))
        {
            if (!probe.Tables.All(seeded.Contains))
            {
                continue;
            }

            try
            {
                await probe.Run(scope).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add($"  [{probe.Name}] {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
            }
        }

        return failures;
    }

    private static UpgradeProbe Read(string name, string[] tables, Func<ProbeScope, ValueTask> run) => new(name, tables, Mutates: false, run);

    private static UpgradeProbe Write(string name, string[] tables, Func<ProbeScope, ValueTask> run, string[]? alsoReads = null)
        => new(name, tables, Mutates: true, run, alsoReads);
}
