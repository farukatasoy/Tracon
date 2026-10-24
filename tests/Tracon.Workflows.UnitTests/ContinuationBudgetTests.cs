using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The ceiling of a run tree (<c>Tracon:AgentGraph</c>) is the logical run's: a workflow that waits for a person,
/// is resumed by an operator or is picked up after an interruption starts its next run from what the chain
/// spent, not from zero (F-531).
/// </summary>
/// <remarks>
/// <para>
/// A workflow with a human approval loop used to get the whole ceiling again with every answer: each answer is a
/// new run row with a new budget, so the ceiling stopped nothing the loop did. A run that failed for its budget
/// and was resumed started the node that had been refused. These cases drive the real runner over the
/// in-memory stores, with real function nodes (the budget gate stands in front of each), and read the run rows
/// the way an operator would.
/// </para>
/// <para>
/// The scenario is money, not tokens: a function node spends no tokens, and the gate on a node reads the cost
/// and the time. The token and run-count ceilings of an agent step are the agent step's own gate, which
/// <c>ChildAgentInvoker</c> keeps; they read the same seeded budget.
/// </para>
/// </remarks>
public sealed class ContinuationBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    // --- A person's answer ---

    [Fact]
    public async Task An_answer_does_not_get_the_whole_ceiling_again()
    {
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 1.2m, publishAmount: 0.3m);

        host.TraconOptions.AgentGraph.MaxTotalCost = 1m;

        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);

        // The step that started under the ceiling ran past it, as one that is running always may.
        (await host.RunStore.GetRunAsync(waiting)).ShouldNotBeNull().NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(1.2m);

        var answer = await AnswerAsync(runner, waiting);
        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Failed);

        var error = record.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Message.ShouldContain("Could not start function node 'publish'", Case.Sensitive);
        error.Message.ShouldContain("cost budget is exhausted (1.200000/1.000000)", Case.Sensitive);
        flow.PublishCalls.ShouldBe(0, "the answer's node started on a budget of zero");
    }

    [Fact]
    public async Task An_answer_starts_from_what_the_run_it_answers_spent_and_may_use_the_rest()
    {
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0.8m, publishAmount: 0.3m);

        host.TraconOptions.AgentGraph.MaxTotalCost = 1m;

        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);
        var answer = await AnswerAsync(runner, waiting);

        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Completed, record.Error?.Message);
        flow.PublishCalls.ShouldBe(1);
        flow.CostAtPublish.ShouldBe(0.8m, "the node started on what the chain had spent");
        record.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.3m, "the answer's own row holds its own share");
    }

    [Fact]
    public async Task A_new_run_starts_from_zero()
    {
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0m, publishAmount: 0m);

        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);

        await AnswerAsync(runner, waiting);

        flow.CostAtPublish.ShouldBe(0m);
        flow.TokensAtPublish.ShouldBe(0);
    }

    [Fact]
    public async Task An_answer_does_not_record_the_run_it_answers_as_the_run_it_continues()
    {
        // K-130: a run that was answered stays AwaitingInput and may be answered again. The link back lives
        // in the answer's own history, and a row that named it would put every answer into the recovery chain.
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0.1m, publishAmount: 0.1m);
        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);
        var answer = await AnswerAsync(runner, waiting);

        (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull().ContinuedFromRunId.ShouldBeNull();
    }

    [Fact]
    public async Task A_second_answer_to_the_same_request_carries_the_run_it_answers_and_not_the_first_answer()
    {
        // Two answers to one waiting run are two branches (K-130). The second one's ceiling is the logical
        // run's up to the question, not the sum of two branches.
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0.6m, publishAmount: 0.3m);

        host.TraconOptions.AgentGraph.MaxTotalCost = 1m;

        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);

        await AnswerAsync(runner, waiting);
        await AnswerAsync(runner, waiting);

        flow.CostAtPublish.ShouldBe(0.6m);
        flow.PublishCalls.ShouldBe(2);
    }

    // --- An operator's resume ---

    [Fact]
    public async Task A_resume_of_a_run_that_failed_for_its_budget_is_refused_at_the_same_node_and_goes_on_once_the_ceiling_is_raised()
    {
        var host = new WorkflowTestHost();
        var rendered = 0;

        host.TraconOptions.Pricing.Currency = "USD";
        host.TraconOptions.AgentGraph.MaxTotalCost = 1m;
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "spend",
            async (messages, context, _) =>
            {
                await context.ReportUsageAsync(BudgetedApprovalWorkflow.Spent(1.2m));

                return messages;
            },
            static _ => { });
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "render",
            (messages, _, _) =>
            {
                Interlocked.Increment(ref rendered);

                return new ValueTask<List<ChatMessage>>(messages);
            },
            static _ => { });

        await host.SaveAsync(Sequential("spend", "render"));

        var runner = host.CreateRunner();
        var failed = await RunAsync(runner, "chain");

        (await host.RunStore.GetRunAsync(failed)).ShouldNotBeNull().Error.ShouldNotBeNull().Type
            .ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        rendered.ShouldBe(0);

        // The operator resumes it: the same node is refused for the same reason, as a new run that
        // records what it continues.
        var resumed = await ResumeAsync(runner, failed);
        var refused = (await host.RunStore.GetRunAsync(resumed)).ShouldNotBeNull();

        refused.Status.ShouldBe(RunStatus.Failed);
        refused.Error.ShouldNotBeNull().Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        refused.Error.Message.ShouldContain("Could not start function node 'render'", Case.Sensitive);
        refused.Error.Message.ShouldContain("cost budget is exhausted (1.200000/1.000000)", Case.Sensitive);
        refused.ContinuedFromRunId.ShouldBe(failed);
        rendered.ShouldBe(0, "the node that was refused started on a budget of zero");

        // The ceiling is a setting: raised, the chain goes on from what it spent.
        host.TraconOptions.AgentGraph.MaxTotalCost = 5m;

        var goneOn = (await host.RunStore.GetRunAsync(await ResumeAsync(runner, failed))).ShouldNotBeNull();

        goneOn.Status.ShouldBe(RunStatus.Completed, goneOn.Error?.Message);
        rendered.ShouldBe(1);
    }

    [Fact]
    public async Task A_chain_of_resumes_counts_every_link_once()
    {
        var host = new WorkflowTestHost();

        host.TraconOptions.Pricing.Currency = "USD";
        host.TraconOptions.AgentGraph.MaxTotalCost = 2m;

        var seen = new List<decimal>();

        // The first node's checkpoint is the one written before the second runs: resuming from it runs `spend` again.
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "prepare",
            (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            static _ => { });
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "spend",
            async (messages, context, _) =>
            {
                seen.Add(TraconRunContext.Current!.Budget!.ConsumedCost);
                await context.ReportUsageAsync(BudgetedApprovalWorkflow.Spent(0.5m));

                return messages;
            },
            static _ => { });

        await host.SaveAsync(Sequential("prepare", "spend"));

        var runner = host.CreateRunner();
        var first = await RunAsync(runner, "chain");

        // Each resume starts from the checkpoint written before the node, so the node runs again; each run spends 0.5.
        // The third resumes the second, from the same checkpoint: the operator names where to go on, and what it
        // continues is the run it resumed.
        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, first))[0].CheckpointId;
        var second = await ResumeAsync(runner, first, checkpoint: checkpoint);
        var third = await ResumeAsync(runner, second, checkpoint: checkpoint);

        seen.ShouldBe([0m, 0.5m, 1.0m]);

        (await host.RunStore.GetRunAsync(third)).ShouldNotBeNull().ContinuedFromRunId.ShouldBe(second);
        (await host.RunStore.GetRunAsync(second)).ShouldNotBeNull().ContinuedFromRunId.ShouldBe(first);
    }

    [Fact]
    public async Task A_resume_an_operator_started_is_the_work_in_flight_at_the_end_of_the_chain()
    {
        // The link makes the resume the newest link of the chain the recovery and the response gate read: "the newest
        // link is the work in flight" (WorkflowContinuationChain) holds for a resume an operator started too.
        var host = new WorkflowTestHost();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "prepare",
            (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            static _ => { });
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "render",
            (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            static _ => { });
        await host.SaveAsync(Sequential("prepare", "render"));

        var runner = host.CreateRunner();
        var head = await RunAsync(runner, "chain");

        var before = await WorkflowContinuationChain.ReadAsync(host.RunStore, head, host.TenantContext.TenantId, CancellationToken.None);

        before.Select(static run => run.Id).ShouldBe([head]);

        var checkpoint = (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, head))[0].CheckpointId;
        var resumed = await ResumeAsync(runner, head, checkpoint: checkpoint);
        var after = await WorkflowContinuationChain.ReadAsync(host.RunStore, head, host.TenantContext.TenantId, CancellationToken.None);

        after.Select(static run => run.Id).ShouldBe([head, resumed]);
    }

    [Fact]
    public async Task A_queued_resume_whose_row_names_nothing_records_its_source()
    {
        var host = new WorkflowTestHost();

        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "spend",
            (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            static _ => { });
        await host.SaveAsync(Sequential("spend"));

        var runner = host.CreateRunner();
        var source = await RunAsync(runner, "chain");

        // The queued path opens the row first, with no link (the HTTP endpoint cannot name one).
        var queued = Guid.NewGuid();

        await OpenQueuedAsync(host, queued, linkedTo: null);
        await ResumeAsync(runner, source, newRunId: queued);

        (await host.RunStore.GetRunAsync(queued)).ShouldNotBeNull().ContinuedFromRunId.ShouldBe(source);
    }

    // --- An interruption's recovery ---

    [Fact]
    public async Task A_recovery_counts_the_interrupted_run_the_continuation_is_linked_to_and_keeps_its_link()
    {
        // The checkpoint a recovery resumes from can belong to an earlier run of the chain than the run it
        // continues. H has a checkpoint and spent 0.7; H' was interrupted after it and spent 0.6; the
        // continuation H'' resumes from H's checkpoint and is linked to H'. The chain it carries is H' and H.
        var host = new WorkflowTestHost();
        var rendered = 0;
        var interruptedOnce = false;

        host.TraconOptions.Pricing.Currency = "USD";
        host.TraconOptions.AgentGraph.MaxTotalCost = 1m;
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "spend",
            async (messages, context, _) =>
            {
                await context.ReportUsageAsync(BudgetedApprovalWorkflow.Spent(0.7m));

                return messages;
            },
            static _ => { });
        host.AddFunctionWithOptions<List<ChatMessage>, List<ChatMessage>>(
            "render",
            (messages, _, _) =>
            {
                Interlocked.Increment(ref rendered);

                // The first time, the process stops here.
                if (!interruptedOnce)
                {
                    interruptedOnce = true;

                    throw new IOException("the host stopped");
                }

                return new ValueTask<List<ChatMessage>>(messages);
            },
            static _ => { });

        await host.SaveAsync(Sequential("spend", "render"));

        var runner = host.CreateRunner();
        var head = await RunAsync(runner, "chain");

        rendered.ShouldBe(1, "the node ran once, before the interruption");

        var interrupted = Guid.NewGuid();
        var continuation = Guid.NewGuid();

        await OpenFailedAsync(host, interrupted, linkedTo: head, amount: 0.6m);
        await OpenQueuedAsync(host, continuation, linkedTo: interrupted);

        await ResumeAsync(
            runner,
            head,
            continuation,
            (await host.CheckpointStore.ListByRunAsync(host.TenantContext.TenantId, head))[0].CheckpointId);

        var record = (await host.RunStore.GetRunAsync(continuation)).ShouldNotBeNull();

        record.ContinuedFromRunId.ShouldBe(interrupted, "the engine never overwrites the link the recovery wrote");
        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull().Message.ShouldContain("cost budget is exhausted (1.300000/1.000000)", Case.Sensitive);
        rendered.ShouldBe(1, "the node was refused on the chain's spend, 0.7 of the head and 0.6 of the interrupted run");
    }

    [Fact]
    public async Task A_recovery_that_runs_the_work_again_from_the_start_carries_what_the_interrupted_run_spent()
    {
        // No checkpoint in the chain: the queued operation runs again under the continuation's id. Its row
        // names the interrupted run, and that is all the budget has to go on.
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0.1m, publishAmount: 0.1m);

        host.TraconOptions.AgentGraph.MaxTotalCost = 1m;

        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var interrupted = Guid.NewGuid();
        var continuation = Guid.NewGuid();

        await OpenFailedAsync(host, interrupted, linkedTo: null, amount: 0.8m);
        await OpenQueuedAsync(host, continuation, linkedTo: interrupted);

        var spent = new List<decimal>();

        flow.OnFirst = () =>
        {
            spent.Add(TraconRunContext.Current!.Budget!.ConsumedCost);

            return ValueTask.CompletedTask;
        };

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = BudgetedApprovalWorkflow.Name,
            Message = "go",
            RunId = continuation,
        }))
        {
            // Drained.
        }

        // 0.8 from the interrupted run, 0.1 the first step reported itself before this read.
        spent.ShouldHaveSingleItem().ShouldBe(0.9m);
    }

    [Fact]
    public async Task A_continuation_does_not_lose_time_to_the_outage_that_came_before_the_recovery()
    {
        // MaxDuration is one hour. The interrupted run executed for ten minutes, then the host was down for two
        // hours, then the recovery closed its row and opened the continuation. The row says it ran 2 h 10 min.
        var clock = new AdjustableClock(Start + TimeSpan.FromMinutes(130));
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0m, publishAmount: 0m);
        var deadlines = new List<DateTimeOffset?>();

        flow.OnFirst = () =>
        {
            deadlines.Add(TraconRunContext.Current!.Budget!.Deadline);

            return ValueTask.CompletedTask;
        };

        host.TraconOptions.AgentGraph.MaxDuration = TimeSpan.FromHours(1);

        var runner = host.CreateRunnerWith(timeProvider: clock, services: flow.Services, codeWorkflows: flow.Registration);
        var interrupted = Guid.NewGuid();
        var continuation = Guid.NewGuid();

        await OpenInterruptedAsync(host, interrupted, ranMinutes: 10, closedAfterMinutes: 130);
        await OpenQueuedAsync(host, continuation, linkedTo: interrupted);

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = BudgetedApprovalWorkflow.Name,
            Message = "go",
            RunId = continuation,
        }))
        {
            // Drained.
        }

        (await host.RunStore.GetRunAsync(continuation)).ShouldNotBeNull().Status.ShouldBe(RunStatus.AwaitingInput);
        deadlines.ShouldHaveSingleItem().ShouldBe(clock.GetUtcNow() + TimeSpan.FromMinutes(50), "one hour, less the ten minutes the run executed");
    }

    // --- Time ---

    [Fact]
    public async Task The_time_a_workflow_waited_for_a_person_does_not_count_against_the_time_limit()
    {
        var clock = new AdjustableClock(Start);
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0m, publishAmount: 0m);

        host.TraconOptions.AgentGraph.MaxDuration = TimeSpan.FromMinutes(30);

        var runner = host.CreateRunnerWith(timeProvider: clock, services: flow.Services, codeWorkflows: flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);

        // Ten minutes of work, then three days for the person.
        await SetTimesAsync(host, waiting, Start, Start + TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromDays(3));

        var answerStartsAt = clock.GetUtcNow();
        var answer = await AnswerAsync(runner, waiting);

        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Completed, record.Error?.Message);
        flow.DeadlineAtPublish.ShouldBe(answerStartsAt + TimeSpan.FromMinutes(20), "30 minutes, less the 10 the chain was executing");
    }

    [Fact]
    public async Task A_chain_that_used_all_of_its_time_refuses_its_first_node()
    {
        var clock = new AdjustableClock(Start);
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0m, publishAmount: 0m);

        host.TraconOptions.AgentGraph.MaxDuration = TimeSpan.FromMinutes(30);

        var runner = host.CreateRunnerWith(timeProvider: clock, services: flow.Services, codeWorkflows: flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);

        await SetTimesAsync(host, waiting, Start, Start + TimeSpan.FromMinutes(40));
        clock.Advance(TimeSpan.FromHours(1));

        var answer = await AnswerAsync(runner, waiting);
        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Failed);

        var error = record.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Message.ShouldContain("time budget is exhausted (00:40:00/00:30:00)", Case.Sensitive);
        flow.PublishCalls.ShouldBe(0);
    }

    // --- What cannot be read ---

    [Fact]
    public async Task A_chain_that_cannot_be_read_fails_the_run_before_any_node_and_never_starts_it_on_a_budget_of_zero()
    {
        var host = new WorkflowTestHost();
        var flow = new BudgetedApprovalWorkflow(host, firstAmount: 0.4m, publishAmount: 0.1m);
        var runner = host.CreateRunner(configure: null, flow.Services, flow.Registration);
        var waiting = await RunToTheRequestAsync(runner);

        // The run that waited continues an earlier run (a recovery), and the store cannot read that one.
        var earlier = Guid.NewGuid();

        await OpenFailedAsync(host, earlier, linkedTo: null, amount: 0.5m);
        await RelinkAsync(host, waiting, earlier);

        var faulty = new FaultyReadRunStore(host.RunStore, static _ => new IOException("the run store is down"), id => id == earlier);
        var failing = host.CreateRunnerWith(runStore: faulty, services: flow.Services, codeWorkflows: flow.Registration);

        var answer = await AnswerAsync(failing, waiting);
        var record = (await host.RunStore.GetRunAsync(answer)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull().Message.ShouldContain("does not start with a budget of zero", Case.Sensitive);
        flow.PublishCalls.ShouldBe(0, "the run started on a budget it did not read");
        faulty.Refused.ShouldBeGreaterThan(0);
    }

    // --- Helpers ---

    private static WorkflowDefinition Sequential(params string[] functions)
        => new()
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes = [.. functions.Select(static name => new WorkflowNodeReference { Name = name, Kind = WorkflowNodeKind.Function })],
        };

    /// <summary>Runs a code workflow to the point it waits and returns the run.</summary>
    private static async Task<Guid> RunToTheRequestAsync(WorkflowRunner runner)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = BudgetedApprovalWorkflow.Name,
            Message = "publish the report",
        }))
        {
            events.Add(runEvent);
        }

        events[^1].Type.ShouldBe(RunEventType.RunAwaitingInput, events[^1].Text);

        return events[0].RunId;
    }

    /// <summary>Answers the request a run waits on and returns the run that carries the answer.</summary>
    private static async Task<Guid> AnswerAsync(WorkflowRunner runner, Guid waiting)
    {
        var pending = (await runner.ListPendingRequestsAsync(waiting)).Single();
        var first = Guid.Empty;

        await foreach (var runEvent in runner.RespondStreamingAsync(new WorkflowRespondRequest
        {
            RunId = waiting,
            RequestId = pending.RequestId,
            Approved = true,
        }))
        {
            if (first == Guid.Empty)
            {
                first = runEvent.RunId;
            }
        }

        return first;
    }

    /// <summary>Runs a stored workflow to its end and returns the run.</summary>
    private static async Task<Guid> RunAsync(WorkflowRunner runner, string workflow)
    {
        var first = Guid.Empty;

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = workflow, Message = "go" }))
        {
            if (first == Guid.Empty)
            {
                first = runEvent.RunId;
            }
        }

        return first;
    }

    /// <summary>Resumes a run and returns the run that carries the resume.</summary>
    /// <param name="runner">The runner.</param>
    /// <param name="source">The run to resume.</param>
    /// <param name="newRunId">The id the resume runs under, for a queued or recovered one.</param>
    /// <param name="checkpoint">The checkpoint to go on from; the latest of <paramref name="source"/> when omitted.</param>
    private static async Task<Guid> ResumeAsync(
        WorkflowRunner runner,
        Guid source,
        Guid? newRunId = null,
        string? checkpoint = null)
    {
        var first = Guid.Empty;

        await foreach (var runEvent in runner.ResumeStreamingAsync(new WorkflowResumeRequest
        {
            RunId = source,
            CheckpointId = checkpoint,
            NewRunId = newRunId,
        }))
        {
            if (first == Guid.Empty)
            {
                first = runEvent.RunId;
            }
        }

        return first;
    }

    private static async Task OpenQueuedAsync(WorkflowTestHost host, Guid run, Guid? linkedTo)
        => await host.RunStore.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = "chain",
            Kind = RunKind.Workflow,
            WorkflowName = "chain",
            Status = RunStatus.Queued,
            StartedAt = Start,
            TenantId = host.TenantContext.TenantId,
            SessionId = "session",
            ContinuedFromRunId = linkedTo,
        });

    /// <summary>Writes a closed run that spent <paramref name="amount"/>, as an interrupted run closed by its recovery is.</summary>
    private static async Task OpenFailedAsync(WorkflowTestHost host, Guid run, Guid? linkedTo, decimal amount)
    {
        await host.RunStore.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = "chain",
            Kind = RunKind.Workflow,
            WorkflowName = "chain",
            Status = RunStatus.Running,
            StartedAt = Start,
            TenantId = host.TenantContext.TenantId,
            SessionId = "session",
            ContinuedFromRunId = linkedTo,
        });

        await host.RunStore.CompleteRunAsync(new RunCompletion
        {
            RunId = run,
            Status = RunStatus.Failed,
            CompletedAt = Start + TimeSpan.FromMinutes(1),
            NonTokenCost = new RunNonTokenCost { Amount = amount, Currency = "USD" },
            TenantId = host.TenantContext.TenantId,
        });
    }

    /// <summary>
    /// Sets when a closed run started and ended. The writer stamps the end with the system clock, and a test that drives
    /// the runner with its own clock needs both ends on that clock.
    /// </summary>
    private static async Task SetTimesAsync(WorkflowTestHost host, Guid run, DateTimeOffset startedAt, DateTimeOffset completedAt)
    {
        var record = (await host.RunStore.GetRunAsync(run)).ShouldNotBeNull();

        await host.RunStore.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = record.AgentName,
            Kind = record.Kind,
            WorkflowName = record.WorkflowName,
            Status = record.Status,
            StartedAt = startedAt,
            TenantId = record.TenantId,
            SessionId = record.SessionId,
            ContinuedFromRunId = record.ContinuedFromRunId,
        });

        await host.RunStore.CompleteRunAsync(new RunCompletion
        {
            RunId = run,
            Status = record.Status,
            CompletedAt = completedAt,
            NonTokenCost = record.NonTokenCost,
            TenantId = record.TenantId,
        });
    }

    /// <summary>
    /// Writes a run the way the recovery leaves one: it executed for <paramref name="ranMinutes"/>, its process died, and
    /// the recovery closed the row <paramref name="closedAfterMinutes"/> after the start with a closing event of its own.
    /// </summary>
    private static async Task OpenInterruptedAsync(WorkflowTestHost host, Guid run, int ranMinutes, int closedAfterMinutes)
    {
        await host.RunStore.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = "chain",
            Kind = RunKind.Workflow,
            WorkflowName = "chain",
            Status = RunStatus.Running,
            StartedAt = Start,
            TenantId = host.TenantContext.TenantId,
            SessionId = "session",
        });

        await host.RunStore.AppendEventAsync(new RunEvent
        {
            RunId = run,
            Sequence = 0,
            Type = RunEventType.RunStarted,
            Timestamp = Start,
            TenantId = host.TenantContext.TenantId,
        });

        await host.RunStore.AppendEventAsync(new RunEvent
        {
            RunId = run,
            Sequence = 1,
            Type = RunEventType.ExecutorInvoked,
            Timestamp = Start + TimeSpan.FromMinutes(ranMinutes),
            TenantId = host.TenantContext.TenantId,
        });

        await host.RunStore.AppendEventAsync(new RunEvent
        {
            RunId = run,
            Sequence = 2,
            Type = RunEventType.RunFailed,
            Timestamp = Start + TimeSpan.FromMinutes(closedAfterMinutes),
            TenantId = host.TenantContext.TenantId,
        });

        await host.RunStore.CompleteRunAsync(new RunCompletion
        {
            RunId = run,
            Status = RunStatus.Failed,
            CompletedAt = Start + TimeSpan.FromMinutes(closedAfterMinutes),
            EventCount = 3,
            Error = new RunError { Type = WorkflowRecovery.InterruptedErrorType, Message = "The process running this workflow run stopped before the run ended." },
            TenantId = host.TenantContext.TenantId,
        });
    }

    /// <summary>Makes the run name another as the one it continues, keeping everything else the row holds.</summary>
    private static async Task RelinkAsync(WorkflowTestHost host, Guid run, Guid continues)
    {
        var record = (await host.RunStore.GetRunAsync(run)).ShouldNotBeNull();

        await host.RunStore.StartRunAsync(new RunStartInfo
        {
            RunId = run,
            AgentName = record.AgentName,
            Kind = record.Kind,
            WorkflowName = record.WorkflowName,
            Status = record.Status,
            StartedAt = record.StartedAt,
            TenantId = record.TenantId,
            SessionId = record.SessionId,
            ContinuedFromRunId = continues,
        });

        // The write above resets what the upsert does not carry: close it again the way the runner did.
        await host.RunStore.CompleteRunAsync(new RunCompletion
        {
            RunId = run,
            Status = record.Status,
            CompletedAt = record.CompletedAt ?? record.StartedAt,
            NonTokenCost = record.NonTokenCost,
            TenantId = record.TenantId,
        });
    }
}
