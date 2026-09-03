using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for the per-judge retry checkpoint (Phase 118) against the real
/// <see cref="IJobStore"/> queue and the manual scoring endpoint.
/// </summary>
public sealed class OnlineEvalRetryTests
{
    [Fact]
    public async Task Successful_judges_are_not_re_invoked_when_a_queued_job_retries()
    {
        var goodJudge = new CountingRunJudge("good", 70);
        var flakyJudge = new FlakyRunJudge("flaky", failuresBeforeSuccess: 1, score: 55);

        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: services =>
            {
                // The worker's own poll/backoff loop is not exercised here: this
                // test drives the queue directly so the retry backoff (30s+)
                // does not make the test slow or flaky.
                services.UseScheduling(static options => options.RunWorker = false);
                services.AddSingleton<IRunJudge>(goodJudge);
                services.AddSingleton<IRunJudge>(flakyJudge);
            });

        var jobStore = host.Services.GetRequiredService<IJobStore>();
        var jobHandler = host.Services.GetRequiredService<OnlineEvalJobHandler>();
        var tenantContext = host.Services.GetRequiredService<ITenantContext>();
        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        var runId = await SeedScorableRunAsync(host);
        var now = DateTimeOffset.UtcNow;

        var job = await jobStore.EnqueueAsync(
            new JobRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = tenantContext.TenantId,
                HandlerKey = JobHandlerKeys.OnlineEval,
                TargetName = "test-agent",
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new[] { runId.ToString() }),
                ScheduledFor = now,
                CreatedAt = now,
            },
            [runId.ToString()]);

        await RunOneAttemptAsync(jobStore, jobHandler, job.Id);

        var afterFirstAttempt = await jobStore.GetAsync(tenantContext.TenantId, job.Id);
        afterFirstAttempt.ShouldNotBeNull();
        afterFirstAttempt!.Status.ShouldBe(JobStatus.Pending);
        afterFirstAttempt.Attempt.ShouldBe(1);

        await RunOneAttemptAsync(jobStore, jobHandler, job.Id);

        var afterSecondAttempt = await jobStore.GetAsync(tenantContext.TenantId, job.Id);
        afterSecondAttempt.ShouldNotBeNull();
        afterSecondAttempt!.Status.ShouldBe(JobStatus.Completed);
        afterSecondAttempt.Attempt.ShouldBe(2);

        // The whole point of the checkpoint: the judge that already succeeded
        // on attempt 1 is NOT called again on attempt 2.
        goodJudge.CallCount.ShouldBe(1);
        flakyJudge.CallCount.ShouldBe(2);

        (await scores.ListAsync(tenantContext.TenantId, runId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Manual_scoring_still_reruns_a_judge_that_already_scored_the_run()
    {
        var judge = new CountingRunJudge("good", 70);

        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IRunJudge>(judge));

        var runId = await SeedScorableRunAsync(host);

        using var first = await host.Client.PostAsync(JudgeUri(runId), content: null);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var second = await host.Client.PostAsync(JudgeUri(runId), content: null);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Manual re-judgment is a deliberate "score this again" request from a
        // caller; it must never be affected by the queued-retry checkpoint.
        judge.CallCount.ShouldBe(2);

        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        var tenantContext = host.Services.GetRequiredService<ITenantContext>();
        (await scores.ListAsync(tenantContext.TenantId, runId)).ShouldHaveSingleItem();
    }

    private static async Task RunOneAttemptAsync(IJobStore jobStore, OnlineEvalJobHandler handler, Guid jobId)
    {
        var owner = $"test-worker-{Guid.NewGuid():N}";
        var leased = await jobStore.LeaseAsync(owner, TimeSpan.FromMinutes(5), lanes: null);
        leased.ShouldNotBeNull();
        leased!.Id.ShouldBe(jobId);

        await jobStore.MarkRunningAsync(leased.Id, owner);
        var items = await jobStore.ListItemsAsync(leased.Id);

        var context = new JobContext
        {
            Job = leased,
            Items = items,
            ReportItemAsync = (result, cancellationToken) => jobStore.ReportItemAsync(result, cancellationToken),
            IsCancelledAsync = _ => new ValueTask<bool>(false),
        };

        try
        {
            await handler.ExecuteAsync(context);

            await jobStore.CompleteAsync(new JobCompletion
            {
                JobId = leased.Id,
                Status = JobStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }
        catch (JobRetryException exception)
        {
            // The real backoff delay is skipped here on purpose (see the
            // comment at the call site); only the re-lease/attempt-increment
            // behavior under test needs to be real.
            await jobStore.ReleaseForRetryAsync(leased.Id, exception.Message, retryAfter: TimeSpan.Zero);
        }
    }

    private static Uri JudgeUri(Guid runId) => new($"/agentprism/api/runs/{runId}/judge", UriKind.Relative);

    private static async Task<Guid> SeedScorableRunAsync(AgentPrismTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = now,
            TenantId = "default",
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = "default",
            Messages = [new ChatMessage(ChatRole.User, "question")],
            CreatedAt = now,
        });

        await runs.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.MessageCompleted,
            Timestamp = now,
            Text = "answer",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = now,
        });

        return runId;
    }

    private sealed class CountingRunJudge(string name, int score) : IRunJudge
    {
        private int _callCount;

        public string Name => name;

        public int CallCount => _callCount;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return new ValueTask<RunJudgment>(new RunJudgment { Score = score });
        }
    }

    /// <summary>A judge that throws on its first N calls, then succeeds every time after.</summary>
    private sealed class FlakyRunJudge(string name, int failuresBeforeSuccess, int score) : IRunJudge
    {
        private int _callCount;

        public string Name => name;

        public int CallCount => _callCount;

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _callCount);

            return call <= failuresBeforeSuccess
                ? throw new InvalidOperationException("simulated transient failure")
                : new ValueTask<RunJudgment>(new RunJudgment { Score = score });
        }
    }
}
