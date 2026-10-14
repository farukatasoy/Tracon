using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Who owns the run an answer starts (phase 208, independent audit 🔴1 and 🔴2): the run row
/// is opened BEFORE the claim, so a claim never names a run that does not exist, and a queued
/// answer whose job has not finished is followed, never started a second time beside the job.
/// </summary>
/// <remarks>
/// <para>
/// Each case drives the real endpoint. A claim with no run behind it locks a single-answer
/// request for good, and that only shows at the HTTP boundary: the repeat answer is what reads
/// the claim.
/// </para>
/// <para>
/// The race is made deterministic with <see cref="BarrierStore"/>: both answers read the claim
/// as free before either writes it, the shape a real double click takes when both requests
/// arrive within the same few milliseconds.
/// </para>
/// </remarks>
public sealed class WorkflowResponseOwnershipTests
{
    [Fact]
    public async Task A_throwing_attribution_getter_does_not_lock_a_streamed_request()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: static services =>
            services.AddSingleton<IRunAttributionContext, ThrowingAttribution>());
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        try
        {
            using var first = await RespondAsync(host, sourceId, new { requestId, approved = true });
            await first.Content.ReadAsStringAsync();
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            // The first call may fail (the test host has no exception handler, so a 500 arrives as
            // the exception); what matters is what the request looks like afterwards.
        }

        // Whatever the first call answered, the request is not locked: the same answer reaches a
        // run that does the work.
        var (runId, frames) = await AnswerUntilAppliedAsync(host, sourceId, requestId);

        frames.ShouldContain(static frame => frame.Event == "event" && frame.Data.Contains("\"WorkflowOutput\"", StringComparison.Ordinal));
        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
        await EveryRecordNamesARunAsync(host, sourceId);
    }

    [Fact]
    public async Task A_throwing_attribution_getter_does_not_lock_a_queued_request()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: static services =>
            services.AddSingleton<IRunAttributionContext, ThrowingAttribution>());
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        try
        {
            using var first = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
            await first.Content.ReadAsStringAsync();
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            // The first call may fail; what matters is what the request looks like afterwards.
        }

        using var repeat = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var runId = await QueuedWorkflowHarness.AcceptedRunIdAsync(repeat);

        // The 202 names a run that exists and that the worker finishes.
        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
        await EveryRecordNamesARunAsync(host, sourceId);
    }

    [Fact]
    public async Task The_loser_of_a_streamed_double_click_closes_its_own_run_as_already_answered()
    {
        var barrier = new BarrierStore(arrivals: 2);
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: services =>
            services.AddSingleton<IWorkflowResponseStore>(provider => barrier.Wrap(provider)));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var before = (await WorkflowRunsAsync(host)).Select(static run => run.Id).ToHashSet();

        barrier.Arm();

        var responses = await Task.WhenAll(
            RespondAsync(host, sourceId, new { requestId, approved = true }),
            RespondAsync(host, sourceId, new { requestId, approved = true }));

        var first = await FramesAsync(responses[0]);
        var second = await FramesAsync(responses[1]);

        RunIdOf(first).ShouldBe(RunIdOf(second));

        var winner = RunIdOf(first);

        (await host.WaitForTerminalRunAsync(winner)).Status.ShouldBe(RunStatus.Completed);

        // Both answers opened a run before they claimed; the loser closed its own.
        var opened = (await WorkflowRunsAsync(host)).Where(run => !before.Contains(run.Id)).ToList();

        opened.Count.ShouldBe(2);

        var loser = opened.Single(run => run.Id != winner);

        loser.Status.ShouldBe(RunStatus.Canceled);
        loser.Error.ShouldNotBeNull().Type.ShouldBe("request_already_answered");

        // Each audit entry names a run that exists: the winner's and the loser's own.
        var named = (await RespondEntriesAsync(host))
            .Select(static entry => JsonDocument.Parse(entry.After!).RootElement.GetProperty("responseRunId").GetGuid())
            .ToList();

        named.ShouldBe([winner, loser.Id], ignoreOrder: true);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task The_loser_of_a_queued_double_click_closes_its_queued_run_and_queues_nothing()
    {
        var barrier = new BarrierStore(arrivals: 2);
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(
            runWorker: false,
            configureServices: services => services.AddSingleton<IWorkflowResponseStore>(provider => barrier.Wrap(provider)));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var before = (await WorkflowRunsAsync(host)).Select(static run => run.Id).ToHashSet();

        barrier.Arm();

        var responses = await Task.WhenAll(
            QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true }, idempotencyKey: "loser-1"),
            QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true }, idempotencyKey: "loser-2"));

        var winner = await QueuedWorkflowHarness.AcceptedRunIdAsync(responses[0]);

        (await QueuedWorkflowHarness.AcceptedRunIdAsync(responses[1])).ShouldBe(winner);
        (await QueuedWorkflowHarness.JobsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(winner);

        var loser = (await WorkflowRunsAsync(host)).Single(run => !before.Contains(run.Id) && run.Id != winner);

        loser.Status.ShouldBe(RunStatus.Canceled);
        loser.Error.ShouldNotBeNull().Type.ShouldBe("request_already_answered");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_client_that_goes_away_between_the_run_and_the_claim_leaves_a_request_that_can_be_answered()
    {
        var gate = new ClaimGateStore();
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: services =>
            services.AddSingleton<IWorkflowResponseStore>(provider => gate.Wrap(provider)));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var before = (await WorkflowRunsAsync(host)).Select(static run => run.Id).ToHashSet();

        using (var abort = new CancellationTokenSource())
        {
            var first = RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: abort.Token);

            // The answer reached its claim; the caller goes away before the claim is written.
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await abort.CancelAsync();
            gate.Release.SetResult();

            await Should.ThrowAsync<OperationCanceledException>(async () => (await first).Dispose());
        }

        // Nothing is left running for a caller who is gone, and the request is not locked.
        await WaitUntil.ValueAsync(
            async () => (await WorkflowRunsAsync(host)).Where(run => !before.Contains(run.Id)).ToList(),
            static runs => runs.Count > 0 && runs.All(static run => run.Status is not (RunStatus.Running or RunStatus.Queued)),
            "the run the gone caller opened to be closed");

        var (runId, frames) = await AnswerUntilAppliedAsync(host, sourceId, requestId);

        frames.ShouldContain(static frame => frame.Data.Contains("\"WorkflowOutput\"", StringComparison.Ordinal));
        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_repeat_while_the_queued_answers_job_is_unfinished_follows_the_job_instead_of_starting_a_second_owner()
    {
        // Independent audit 🔴2: the queued answer's first attempt was stopped (a graceful
        // shutdown closed its run Canceled) and its job waits to be leased again; recovery will
        // continue that run. A repeat of the same answer must not start the work a second time
        // beside the job.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false, resumeInterruptedRuns: true);
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var head = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);

        await host.Services.GetRequiredService<IRunStore>().CompleteRunAsync(new RunCompletion
        {
            RunId = head,
            Status = RunStatus.Canceled,
            CompletedAt = DateTimeOffset.UtcNow,
            TenantId = QueuedWorkflowHarness.TenantOf(host),
        });

        var repeat = RespondAsync(host, sourceId, new { requestId, approved = true });

        await QueuedWorkflowHarness.FinishWithWorkerAsync(host, head);

        using var answered = await repeat;
        var frames = await FramesAsync(answered);

        frames[^1].Event.ShouldBe("done");
        (await AppliedRunsAsync(host)).Count.ShouldBe(1, "the answer was applied by more than one run");
    }

    [Fact]
    public async Task A_stream_whose_engine_fails_before_its_first_event_closes_the_run_it_opened()
    {
        // The run row is opened before the record; the engine upserts it when it starts. When the
        // engine fails first (here: the waiting run's checkpoints are gone), WorkflowEventStream
        // closes the row itself, so the record names a stopped run and the request is not locked.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var source = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(sourceId))!;

        await host.Services.GetRequiredService<IWorkflowCheckpointStore>()
            .DeleteAsync(source.TenantId!, source.SessionId!);

        using var answered = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var frames = await FramesAsync(answered);
        var runId = RunIdOf(frames);

        frames[^1].Event.ShouldBe("error");

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("checkpoint", Case.Insensitive);
        (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem().ResponseRunId.ShouldBe(runId);
    }

    [Fact]
    public async Task A_delivered_answer_whose_run_failed_before_any_checkpoint_starts_again()
    {
        // Open question 6-A: the workflow received the answer, then the run failed before it wrote
        // a checkpoint of its own. Nothing can be resumed, so the same answer starts the work again.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false);
        var (sourceId, requestId, head) = await StoppedQueuedAnswerAsync(host, RunStatus.Failed, delivered: true);

        using var again = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var restarted = RunIdOf(await FramesAsync(again));

        restarted.ShouldNotBe(head);
        (await host.WaitForTerminalRunAsync(restarted)).Status.ShouldBe(RunStatus.Completed);
        (await RecordsAsync(host, sourceId)).Single(record => record.ResponseRunId == head).Status.ShouldBe(WorkflowResponseStatus.Abandoned);
    }

    [Fact]
    public async Task A_delivered_answer_whose_run_wrote_a_checkpoint_is_followed_not_started_again()
    {
        // Open question 6-A, the other way: a checkpoint means the run got past the answer; the
        // operator resumes it from there. The same answer follows it and starts nothing.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false);
        var (sourceId, requestId, head) = await StoppedQueuedAnswerAsync(host, RunStatus.Failed, delivered: true);

        await WriteCheckpointAsync(host, head);

        var before = (await WorkflowRunsAsync(host)).Count;

        using var again = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var frames = await FramesAsync(again);

        RunIdOf(frames).ShouldBe(head);
        frames[^1].Event.ShouldBe("done");
        (await WorkflowRunsAsync(host)).Count.ShouldBe(before);
    }

    [Fact]
    public async Task An_accepted_answer_a_recovery_continuation_carried_past_a_checkpoint_is_followed()
    {
        // Independent audit 🟡2: a checkpoint-less recovery applies the answer again under the
        // continuation's id, and the delivery is recorded only for the first run, so the record
        // stays Accepted. A chain longer than one run therefore goes through the checkpoint rule.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false);
        var (sourceId, requestId, head) = await StoppedQueuedAnswerAsync(host, RunStatus.Failed, delivered: false);
        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenant = QueuedWorkflowHarness.TenantOf(host);
        var continuation = Guid.NewGuid();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = continuation,
            AgentName = SingleFlow,
            Kind = RunKind.Workflow,
            WorkflowName = SingleFlow,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
            SessionId = (await runs.GetRunAsync(sourceId))!.SessionId,
            ContinuedFromRunId = head,
        });
        await runs.AppendEventAsync(new RunEvent
        {
            RunId = continuation,
            Sequence = 0,
            Type = RunEventType.RunStarted,
            Timestamp = DateTimeOffset.UtcNow,
            TenantId = tenant,
        });
        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = continuation,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "test", Message = "failed after its first checkpoint" },
            TenantId = tenant,
        });
        await WriteCheckpointAsync(host, continuation);

        var before = (await WorkflowRunsAsync(host)).Count;

        using var again = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var frames = await FramesAsync(again);

        RunIdOf(frames).ShouldBe(continuation);
        (await WorkflowRunsAsync(host)).Count.ShouldBe(before);
        (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowResponseStatus.Accepted);
    }

    [Fact]
    public async Task Two_concurrent_redrives_of_a_stopped_answer_start_one_run()
    {
        var barrier = new BarrierStore(arrivals: 2);
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(
            runWorker: false,
            configureServices: services => services.AddSingleton<IWorkflowResponseStore>(provider => barrier.Wrap(provider)));
        var (sourceId, requestId, head) = await StoppedQueuedAnswerAsync(host, RunStatus.Canceled, delivered: false);
        var before = (await WorkflowRunsAsync(host)).Select(static run => run.Id).ToHashSet();

        barrier.Arm();

        var responses = await Task.WhenAll(
            RespondAsync(host, sourceId, new { requestId, approved = true }),
            RespondAsync(host, sourceId, new { requestId, approved = true }));

        var first = RunIdOf(await FramesAsync(responses[0]));
        var second = RunIdOf(await FramesAsync(responses[1]));

        first.ShouldBe(second);
        (await host.WaitForTerminalRunAsync(first)).Status.ShouldBe(RunStatus.Completed);

        // One run did the work; a race loser, if there was a race, closed its own run unstarted.
        var opened = (await WorkflowRunsAsync(host)).Where(run => !before.Contains(run.Id)).ToList();

        opened.Count(static run => !string.Equals(run.Error?.Type, "request_already_answered", StringComparison.Ordinal)).ShouldBe(1);
        (await AppliedRunsAsync(host)).ShouldBe([first]);

        var records = await RecordsAsync(host, sourceId);

        records.Single(record => record.ResponseRunId == head).Status.ShouldBe(WorkflowResponseStatus.Abandoned);
        records.Count(static record => record.Status != WorkflowResponseStatus.Abandoned).ShouldBe(1);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task An_answer_whose_run_was_erased_is_refused_instead_of_followed_into_nothing()
    {
        // A record is written only after its run row, so a record with no run means the run was
        // removed later: a data subject erasure that named only the answer's run (its foreign key
        // is on the source run). There is nothing to follow and starting again could repeat what
        // the erased run did, so both paths answer 409. Measured on a real SQLite store.
        using var database = new TempSqliteDatabase("faz208-gone");
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(
            runWorker: false,
            configureTracon: builder => builder.UseSqlite(database.ConnectionString));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var head = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);
        var tenant = QueuedWorkflowHarness.TenantOf(host);

        await host.Services.GetRequiredService<IDataSubjectStore>().EraseAsync(
            tenant,
            new DataSubjectScope { SessionIds = [], RunIds = [head], ConversationIds = [] },
            static (_, _) => default);

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(head)).ShouldBeNull();
        (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem().ResponseRunId.ShouldBe(head);

        using var streamed = await RespondAsync(host, sourceId, new { requestId, approved = true });

        streamed.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await streamed.Content.ReadAsStringAsync()).ShouldContain("no longer exists", Case.Sensitive);

        using var queuedAgain = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });

        queuedAgain.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_queued_answer_whose_job_was_never_written_is_finished_by_the_same_answer(bool queuedRepeat)
    {
        // Second-round audit: the queued answer's row and record were written, its job was not.
        // A process that stops between the record and the enqueue leaves exactly this; here the
        // queue refuses the job and the run store cannot close the row either. Run reconciliation
        // reads only Running rows and a Queued run with no job cannot be cancelled, so the repeat
        // of the same answer is the one thing that can still move the request: it writes the
        // missing job (maintainer decision 2026-10-14).
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false, configureTracon: static builder => StopBeforeTheJob(builder.Services));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var head = await QueuedAnswerWithNoJobAsync(host, sourceId, requestId);

        if (queuedRepeat)
        {
            using var repeat = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });

            (await QueuedWorkflowHarness.AcceptedRunIdAsync(repeat)).ShouldBe(head);
            (await QueuedWorkflowHarness.JobsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(head);
            (await QueuedWorkflowHarness.FinishWithWorkerAsync(host, head)).Status.ShouldBe(JobStatus.Completed);
        }
        else
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var repeat = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: timeout.Token);

            (await QueuedWorkflowHarness.JobsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(head);

            var reading = FramesAsync(repeat);

            (await QueuedWorkflowHarness.FinishWithWorkerAsync(host, head)).Status.ShouldBe(JobStatus.Completed);

            var frames = await reading;

            RunIdOf(frames).ShouldBe(head);
            frames[^1].Event.ShouldBe("done");
            frames.ShouldContain(static frame => frame.Data.Contains("\"WorkflowOutput\"", StringComparison.Ordinal));
        }

        (await host.WaitForTerminalRunAsync(head)).Status.ShouldBe(RunStatus.Completed);
        (await AppliedRunsAsync(host)).ShouldBe([head]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_repeats_that_both_find_the_job_missing_write_it_once(bool sqlite)
    {
        // The rewrite relies on the queue refusing a second job with the same id (the job id is
        // the run id); JobStoreContract measures that refusal on every store. Both repeats read
        // the job as missing before either writes it, so both write: one write wins, the other is
        // refused, reads the job back and answers with it, and leaves the row alone.
        using var database = sqlite ? new TempSqliteDatabase("faz208-missing-job") : null;
        var reads = new JobReadBarrier();
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(
            runWorker: false,
            configureTracon: builder =>
            {
                if (database is not null)
                {
                    builder.UseSqlite(database.ConnectionString);
                }

                StopBeforeTheJob(builder.Services);
                reads.Wrap(builder.Services);
            });
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var head = await QueuedAnswerWithNoJobAsync(host, sourceId, requestId);

        reads.Arm(head, arrivals: 2);

        var responses = await Task.WhenAll(
            QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true }, idempotencyKey: "repeat-1"),
            QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true }, idempotencyKey: "repeat-2"));

        reads.Released.ShouldBeTrue("the two repeats did not both read the job as missing");

        foreach (var response in responses)
        {
            (await QueuedWorkflowHarness.AcceptedRunIdAsync(response)).ShouldBe(head);
            response.Dispose();
        }

        (await QueuedWorkflowHarness.JobsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(head);
        (await QueuedWorkflowHarness.FinishWithWorkerAsync(host, head)).Status.ShouldBe(JobStatus.Completed);
        (await host.WaitForTerminalRunAsync(head)).Status.ShouldBe(RunStatus.Completed);
        (await AppliedRunsAsync(host)).ShouldBe([head]);
    }

    [Fact]
    public async Task A_follower_of_a_run_that_never_started_ends_with_an_error_not_done()
    {
        // The first answer's run row and claim are written; its caller goes away before the
        // engine starts, so its stream closes the row Canceled with no events. A repeat that began
        // following while the row was still Running must not report that as success.
        var gate = new ClaimGateStore(holdAfterWrite: true);
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: services =>
            services.AddSingleton<IWorkflowResponseStore>(provider => gate.Wrap(provider)));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var before = (await WorkflowRunsAsync(host)).Select(static run => run.Id).ToHashSet();

        using var abort = new CancellationTokenSource();
        var first = RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: abort.Token);

        await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var head = (await WorkflowRunsAsync(host)).Single(run => !before.Contains(run.Id)).Id;

        // The follower's headers arrive once it is past its decision: it follows the Running row.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var follower = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: timeout.Token);
        var reading = FramesAsync(follower);

        await abort.CancelAsync();
        gate.Release.SetResult();

        await Should.ThrowAsync<OperationCanceledException>(async () => (await first).Dispose());

        var frames = await reading;
        var run = await host.WaitForTerminalRunAsync(head);

        run.Status.ShouldBe(RunStatus.Canceled);
        (await StoredEventsAsync(host, head)).ShouldBeEmpty();
        RunIdOf(frames).ShouldBe(head);
        frames.ShouldNotContain(static frame => frame.Event == "done");
        frames[^1].Event.ShouldBe("error");
        frames[^1].Data.ShouldContain("never started", Case.Sensitive);
    }

    [Fact]
    public async Task A_follower_whose_run_was_erased_while_it_followed_ends_with_an_error_not_done()
    {
        // The queued answer's job waits (the worker is off), so a streamed repeat follows its
        // Queued row. A data subject erasure removes the row; the follower must say so.
        using var database = new TempSqliteDatabase("faz208-follow-erased");
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(
            runWorker: false,
            configureTracon: builder => builder.UseSqlite(database.ConnectionString));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var head = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var follower = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: timeout.Token);
        var reading = FramesAsync(follower);

        await host.Services.GetRequiredService<IDataSubjectStore>().EraseAsync(
            QueuedWorkflowHarness.TenantOf(host),
            new DataSubjectScope { SessionIds = [], RunIds = [head], ConversationIds = [] },
            static (_, _) => default);

        var frames = await reading;

        RunIdOf(frames).ShouldBe(head);
        frames.ShouldNotContain(static frame => frame.Event == "done");
        frames[^1].Event.ShouldBe("error");
        frames[^1].Data.ShouldContain("no longer exists", Case.Sensitive);
    }

    /// <summary>
    /// Makes the first queued answer leave what a process that stopped between its record and its
    /// enqueue leaves: the queue refuses the job, and the run store cannot close the Queued row.
    /// </summary>
    private static void StopBeforeTheJob(IServiceCollection services)
    {
        services.ThrowFrom<IJobStore>(
            nameof(IJobStore.EnqueueAsync),
            static () => new ProcessStoppedException(),
            times: 1,
            when: static args => args[0] is JobRecord { HandlerKey: JobHandlerKeys.WorkflowRun });
        services.ThrowFrom<IRunStore>(
            nameof(IRunStore.CompleteRunAsync),
            static () => new ProcessStoppedException(),
            times: 1,
            when: static args => args[0] is RunCompletion { Error.Type: nameof(ProcessStoppedException) });
    }

    /// <summary>
    /// Queues an answer on a host made with <see cref="StopBeforeTheJob"/> and checks the state it
    /// leaves: one Queued row, a record that names it, and no job.
    /// </summary>
    private static async Task<Guid> QueuedAnswerWithNoJobAsync(TraconTestHost host, Guid sourceId, string requestId)
    {
        try
        {
            using var first = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });

            first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        }
        catch (Exception exception) when (exception is not ShouldAssertException)
        {
            // The test host has no exception handler: the 500 arrives as the exception.
        }

        var head = (await WorkflowRunsAsync(host)).Where(static run => run.Status == RunStatus.Queued).ShouldHaveSingleItem().Id;

        (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem().ResponseRunId.ShouldBe(head);
        (await QueuedWorkflowHarness.JobsAsync(host)).ShouldBeEmpty();

        return head;
    }

    /// <summary>The events stored for a run.</summary>
    private static async Task<List<RunEvent>> StoredEventsAsync(TraconTestHost host, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(runId, 0))
        {
            events.Add(runEvent);
        }

        return events;
    }

    /// <summary>
    /// Queues an answer (the host's worker is off), then stops its run the way a crash or an
    /// operator does and finishes its job, so nothing owns the work any more.
    /// </summary>
    private static async Task<(Guid SourceId, string RequestId, Guid Head)> StoppedQueuedAnswerAsync(
        TraconTestHost host,
        RunStatus stopped,
        bool delivered)
    {
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var head = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);
        var tenant = QueuedWorkflowHarness.TenantOf(host);

        (await host.Services.GetRequiredService<IJobStore>().CancelAsync(tenant, head)).ShouldBeTrue();

        if (delivered)
        {
            await host.Services.GetRequiredService<IWorkflowResponseStore>().MarkDeliveredAsync(head);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        await runs.AppendEventAsync(new RunEvent
        {
            RunId = head,
            Sequence = 0,
            Type = RunEventType.RunStarted,
            Timestamp = DateTimeOffset.UtcNow,
            TenantId = tenant,
        });
        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = head,
            Status = stopped,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "test", Message = "stopped by the test" },
            TenantId = tenant,
        });

        return (sourceId, requestId, head);
    }

    /// <summary>Writes a checkpoint row for a run.</summary>
    private static async Task WriteCheckpointAsync(TraconTestHost host, Guid runId)
    {
        var run = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))!;

        await host.Services.GetRequiredService<IWorkflowCheckpointStore>().CreateAsync(new WorkflowCheckpointRecord
        {
            Id = Guid.NewGuid(),
            TenantId = run.TenantId!,
            SessionId = run.SessionId!,
            CheckpointId = $"checkpoint-{runId:N}",
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = JsonDocument.Parse("{}").RootElement.Clone(),
        });
    }

    /// <summary>The runs that applied an answer: each carries the back-link event.</summary>
    private static async Task<List<Guid>> AppliedRunsAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var applied = new List<Guid>();

        foreach (var run in await WorkflowRunsAsync(host))
        {
            await foreach (var runEvent in runs.ReadEventsAsync(run.Id, 0))
            {
                if (runEvent.Type == RunEventType.WorkflowResponseApplied)
                {
                    applied.Add(run.Id);
                    break;
                }
            }
        }

        return applied;
    }

    /// <summary>
    /// Answers until a run applies the answer: a repeat either follows a run in flight or starts the
    /// work again after a run that stopped. Bounded, so a lock fails the test instead of hanging it.
    /// </summary>
    private static async Task<(Guid RunId, List<SseFrame> Frames)> AnswerUntilAppliedAsync(TraconTestHost host, Guid sourceId, string requestId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: timeout.Token);
        var frames = await FramesAsync(response);

        return (RunIdOf(frames), frames);
    }

    /// <summary>Every answer record of the source names a run row that exists.</summary>
    private static async Task EveryRecordNamesARunAsync(TraconTestHost host, Guid sourceId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        foreach (var record in await RecordsAsync(host, sourceId))
        {
            (await runs.GetRunAsync(record.ResponseRunId)).ShouldNotBeNull($"the record of run {record.ResponseRunId} names no run");
        }
    }

    /// <summary>An attribution context whose identity pipeline throws.</summary>
    private sealed class ThrowingAttribution : IRunAttributionContext
    {
        public string? UserId => throw new InvalidOperationException("identity pipeline is down");

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>
    /// Holds the first claim lookups until a set number of answers arrived, so every one of them
    /// reads the request as unclaimed.
    /// </summary>
    private sealed class BarrierStore(int arrivals)
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private volatile bool _armed;

        public void Arm() => _armed = true;

        public IWorkflowResponseStore Wrap(IServiceProvider provider)
            => new Store(this, new InMemoryWorkflowResponseStore(provider.GetRequiredService<ITenantContext>()));

        private async ValueTask WaitAsync()
        {
            if (!_armed)
            {
                return;
            }

            if (Interlocked.Increment(ref _arrived) >= arrivals)
            {
                _armed = false;
                _all.TrySetResult();
            }

            await _all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        private sealed class Store(BarrierStore barrier, IWorkflowResponseStore inner) : IWorkflowResponseStore
        {
            public ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default)
                => inner.AddAsync(record, cancellationToken);

            public async ValueTask<WorkflowResponseRecord?> FindClaimAsync(string claimScope, string requestId, CancellationToken cancellationToken = default)
            {
                var found = await inner.FindClaimAsync(claimScope, requestId, cancellationToken);

                await barrier.WaitAsync();

                return found;
            }

            public ValueTask<bool> ReplaceAsync(Guid abandonedResponseRunId, WorkflowResponseRecord replacement, CancellationToken cancellationToken = default)
                => inner.ReplaceAsync(abandonedResponseRunId, replacement, cancellationToken);

            public ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
                => inner.MarkDeliveredAsync(responseRunId, cancellationToken);

            public ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(Guid sourceRunId, CancellationToken cancellationToken = default)
                => inner.ListBySourceRunAsync(sourceRunId, cancellationToken);

            public ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
                => inner.GetAsync(responseRunId, cancellationToken);
        }
    }

    /// <summary>
    /// Holds the first claim write until the test releases it: before the write, or, with
    /// <c>holdAfterWrite</c>, after it (the claim is stored, its caller has not moved on).
    /// </summary>
    private sealed class ClaimGateStore(bool holdAfterWrite = false)
    {
        private int _held;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool HoldAfterWrite => holdAfterWrite;

        public IWorkflowResponseStore Wrap(IServiceProvider provider)
            => new Store(this, new InMemoryWorkflowResponseStore(provider.GetRequiredService<ITenantContext>()));

        private sealed class Store(ClaimGateStore gate, IWorkflowResponseStore inner) : IWorkflowResponseStore
        {
            public async ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default)
            {
                var hold = Interlocked.Exchange(ref gate._held, 1) == 0;

                if (hold && !gate.HoldAfterWrite)
                {
                    await HoldAsync();
                }

                var claim = await inner.AddAsync(record, cancellationToken);

                if (hold && gate.HoldAfterWrite)
                {
                    await HoldAsync();
                }

                return claim;
            }

            private async Task HoldAsync()
            {
                gate.Reached.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }

            public ValueTask<WorkflowResponseRecord?> FindClaimAsync(string claimScope, string requestId, CancellationToken cancellationToken = default)
                => inner.FindClaimAsync(claimScope, requestId, cancellationToken);

            public ValueTask<bool> ReplaceAsync(Guid abandonedResponseRunId, WorkflowResponseRecord replacement, CancellationToken cancellationToken = default)
                => inner.ReplaceAsync(abandonedResponseRunId, replacement, cancellationToken);

            public ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
                => inner.MarkDeliveredAsync(responseRunId, cancellationToken);

            public ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(Guid sourceRunId, CancellationToken cancellationToken = default)
                => inner.ListBySourceRunAsync(sourceRunId, cancellationToken);

            public ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
                => inner.GetAsync(responseRunId, cancellationToken);
        }
    }

    /// <summary>A fault that stands for a process that stopped in the middle of a request.</summary>
    private sealed class ProcessStoppedException() : Exception("The process stopped.");

    /// <summary>
    /// Holds the job reads of one run until a set number of callers arrived, so every one of them
    /// reads the job as missing before any of them writes it.
    /// </summary>
    internal sealed class JobReadBarrier
    {
        private readonly TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Guid _jobId;
        private int _arrivals;
        private int _arrived;
        private volatile bool _armed;

        /// <summary>Gets whether the set number of callers arrived and were let through together.</summary>
        public bool Released => _all.Task.IsCompletedSuccessfully;

        public void Arm(Guid jobId, int arrivals)
        {
            _jobId = jobId;
            _arrivals = arrivals;
            _armed = true;
        }

        /// <summary>Replaces the last <see cref="IJobStore"/> registration with one whose reads wait here.</summary>
        public void Wrap(IServiceCollection services)
        {
            var descriptor = services.Last(static candidate => candidate.ServiceType == typeof(IJobStore));

            services.Add(ServiceDescriptor.Describe(
                typeof(IJobStore),
                provider => JobReadBarrierProxy.Create(FaultInjection.Resolve<IJobStore>(provider, descriptor), this),
                descriptor.Lifetime));
        }

        internal bool Holds(Guid jobId) => _armed && jobId == _jobId;

        internal async Task WaitAsync()
        {
            if (Interlocked.Increment(ref _arrived) >= _arrivals)
            {
                _armed = false;
                _all.TrySetResult();
            }

            await _all.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>The forwarding proxy behind <see cref="JobReadBarrier"/>.</summary>
#pragma warning disable CA1852 // DispatchProxy derives a runtime type from this class; it cannot be sealed.
    internal class JobReadBarrierProxy : DispatchProxy
#pragma warning restore CA1852
    {
        private IJobStore _inner = null!;
        private JobReadBarrier _barrier = null!;

        internal static IJobStore Create(IJobStore inner, JobReadBarrier barrier)
        {
            var proxy = Create<IJobStore, JobReadBarrierProxy>();
            var self = (JobReadBarrierProxy)(object)proxy;

            self._inner = inner;
            self._barrier = barrier;

            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (string.Equals(targetMethod.Name, nameof(IJobStore.GetAsync), StringComparison.Ordinal)
                && args is [string tenantId, Guid jobId, CancellationToken token]
                && _barrier.Holds(jobId))
            {
                return new ValueTask<JobRecord?>(GetAfterBarrierAsync(tenantId, jobId, token));
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }
        }

        private async Task<JobRecord?> GetAfterBarrierAsync(string tenantId, Guid jobId, CancellationToken token)
        {
            var job = await _inner.GetAsync(tenantId, jobId, token);

            await _barrier.WaitAsync();

            return job;
        }
    }
}
