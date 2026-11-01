using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// The lease, attempt and cancel contract <see cref="IJobHandler"/> and
/// <see cref="TraconSchedulingOptions.MaxAttempts"/> promise, held by
/// <see cref="JobWorkerBackgroundService"/>.
/// </summary>
/// <remarks>
/// All three broke on the worker, not in a store. Renewal stopped for
/// good on its first failure, a job whose lease ran out was run again with no
/// attempt limit, and a cancelled job whose handler threw was retried or failed.
/// </remarks>
public sealed class JobWorkerLeaseContractTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task Lease_renewal_continues_after_a_failed_renewal()
    {
        var jobs = new FailFirstRenewalJobStore();
        var handler = new BlockingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMilliseconds(300));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Started.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        // The first renewal throws. A loop that stops there never calls again,
        // and the lease runs out under a handler that is still working.
        await WaitUntil.TrueAsync(() => jobs.RenewCalls >= 3, "the lease to be renewed after the failed renewal");

        handler.Release();
        await worker.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_job_whose_lease_ran_out_is_not_run_past_its_attempt_limit()
    {
        var clock = new ManualTimeProvider();
        var jobs = new InMemoryJobStore(clock);
        var handler = new CountingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, clock.GetUtcNow());

        // A worker leased the job (attempt 1) and died without renewing it.
        (await jobs.LeaseAsync(new JobLeaseRequest { Owner = "dead-worker", LeaseDuration = TimeSpan.FromMinutes(1) }, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        clock.Advance(TimeSpan.FromMinutes(2));

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5), clock);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken).AsTask(),
            static job => job?.Status is JobStatus.Failed or JobStatus.Completed,
            "the re-leased job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(0, "a job limited to one attempt ran a second time");
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(2);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldStartWith(JobErrorCodes.AttemptsExhausted);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public async Task A_cancelled_job_whose_handler_throws_stays_cancelled(int maxAttempts, bool throwsAfterTheWatchSawTheCancel)
    {
        var jobs = new ObservingJobStore();
        var handler = new CancelThenThrowHandler(jobs, throwsAfterTheWatchSawTheCancel);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5), metrics: metrics);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // The attempt's job metric is the one signal both orders share. A
        // handler that throws before the watch reads the cancel goes through the
        // failure path, which reads the status back; when the watch reads it first,
        // the worker writes and reads nothing more. Waiting for that read hung the
        // second order for 30 s, and a busy machine made it the order that ran.
        await WaitUntil.TrueAsync(
            () => collector.LongTags(TraconDiagnostics.JobCounterName).Count > 0,
            "the worker to record the attempt's outcome");

        var record = await jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken);

        record!.Status.ShouldBe(JobStatus.Cancelled);
        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldHaveSingleItem()[TraconDiagnostics.Tags.JobStatus]
            .ShouldBe(nameof(JobStatus.Cancelled));
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_job_cancelled_between_lease_and_start_is_not_run()
    {
        var jobs = new ObservingJobStore { CancelBeforeMarkRunning = true };
        var handler = new CountingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // Either the handler runs, or the worker reads the job back and stops.
        await Task.WhenAny(handler.Called, jobs.ReadAfterMarkRunning)
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken);

        var record = await jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(0);
        record!.Status.ShouldBe(JobStatus.Cancelled);
    }

    /// <summary>
    /// The store calls that write a successful handler's outcome sat in
    /// the handler's own <c>try</c>. One store error there was taken for a
    /// handler failure: under one attempt the job became Failed while its work
    /// had finished; under three the handler ran a second time.
    /// </summary>
    [Theory]
    [InlineData(StoreCall.ListItems, 1)]
    [InlineData(StoreCall.ListItems, 3)]
    [InlineData(StoreCall.Complete, 1)]
    [InlineData(StoreCall.Complete, 3)]
    public async Task A_store_error_after_the_handler_succeeded_neither_fails_nor_reruns_the_job(StoreCall failing, int maxAttempts)
    {
        var jobs = new ThrowOnceJobStore(failing);
        var handler = new CountingHandler(jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue("the store error was never raised, so the case proves nothing");
        handler.Calls.ShouldBe(1, "a handler that succeeded ran again because the store failed once");
        record!.Status.ShouldBe(JobStatus.Completed);
        record.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// A handler whose work is done but whose last write kept failing
    /// throws <see cref="NonRetryableJobException"/>. Releasing that job ran
    /// the work again (a second model call for a queued run).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_handler_whose_finished_work_could_not_be_recorded_fails_at_once_and_does_not_run_again(int maxAttempts)
    {
        var jobs = new InMemoryJobStore();
        var handler = new NonRetryableHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(1, "a handler whose work was done ran again");
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(1);
        record.ErrorMessage.ShouldBe(JobErrorCodes.Format(QueuedTurn.SessionSaveFailedCode, "abcd1234"));
    }

    [Fact]
    public async Task A_store_error_while_releasing_a_failed_attempt_still_releases_the_job()
    {
        var jobs = new ThrowOnceJobStore(StoreCall.ReleaseForRetry);
        var handler = new FailingHandler(failures: 1, jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        // A release that escaped left the job Running under a five-minute lease.
        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the released job to run again and finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue();
        handler.Calls.ShouldBe(2);
        record!.Status.ShouldBe(JobStatus.Completed);
        record.Attempt.ShouldBe(2);
    }

    [Fact]
    public async Task A_store_error_while_failing_the_last_attempt_still_fails_the_job()
    {
        var jobs = new ThrowOnceJobStore(StoreCall.Complete);
        var handler = new FailingHandler(failures: int.MaxValue, jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the failed job to be closed");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue();
        handler.Calls.ShouldBe(1);
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(1);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldNotStartWith(JobErrorCodes.AttemptsExhausted);
    }

    /// <summary>
    /// The completion retry stops where this worker can no longer prove its
    /// lease: past that point another worker may own the job, and a store
    /// write carries no owner check. The job then takes the lease-expiry path.
    /// </summary>
    [Fact]
    public async Task A_completion_the_store_keeps_refusing_is_left_to_lease_expiry()
    {
        var clock = new ManualTimeProvider();
        var jobs = new RefusingCompletionJobStore(clock);
        var handler = new CountingHandler(jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, clock.GetUtcNow());

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5), clock);

        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(() => jobs.RefusedCompletions >= 2, "the worker to retry the refused completion");

        // The lease runs out on the store's clock and on the worker's.
        clock.Advance(TimeSpan.FromMinutes(6));

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the expired job to be re-leased and closed");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.Calls.ShouldBe(1);
        record!.Status.ShouldBe(JobStatus.Failed);
        record.Attempt.ShouldBe(2);
        record.ErrorMessage.ShouldNotBeNull();
        record.ErrorMessage.ShouldStartWith(JobErrorCodes.AttemptsExhausted);
    }

    [Fact]
    public async Task Stopping_the_worker_ends_the_completion_retries()
    {
        var jobs = new RefusingCompletionJobStore();
        var handler = new CountingHandler(jobs.Arm);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, leaseDuration: TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(() => jobs.RefusedCompletions >= 2, "the worker to retry the refused completion");

        using var stopBound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stopBound.CancelAfter(WaitUntil.DefaultTimeout);

        await worker.StopAsync(stopBound.Token);

        (worker.ExecuteTask?.IsCompleted ?? false).ShouldBeTrue("a retry loop that ignores shutdown holds the host open");

        var record = await jobs.PeekAsync(jobId);

        handler.Calls.ShouldBe(1);
        record!.Status.ShouldBe(JobStatus.Running);
    }

    /// <summary>
    /// A worker whose renewal the store refuses has lost the job: it tells the handler, writes
    /// nothing, counts nothing and publishes nothing. The worker that took the job over does all four.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handler_is_cancelled_when_a_renewal_is_refused(bool handlerReturnsNormally)
    {
        var jobs = new LeaseLossJobStore { Renewal = RenewalAnswer.Refuse };
        var handler = new StopsWhenCancelledHandler(handlerReturnsNormally);
        var publisher = new RecordingPublisher();
        var logger = new CollectingLogger();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMilliseconds(300), metrics: metrics, publisher: publisher, logger: logger);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Cancelled.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.ReasonWhenCancelled.ShouldBe(JobStopReason.LeaseLost);
        handler.Calls.ShouldBe(1, "the lost attempt must not be run again by its own worker");

        // One warning says why; the handler's cancellation is not reported as a failed job.
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("no longer holds the lease", StringComparison.Ordinal));
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Error);
        jobs.CompleteCalls.ShouldBe(0, "a worker that lost its lease would close the attempt of the worker that holds it");
        jobs.ReleaseCalls.ShouldBe(0, "a worker that lost its lease would requeue the attempt of the worker that holds it");
        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldBeEmpty("the worker that holds the job counts it");
        publisher.Published.ShouldBeEmpty("the worker that holds the job publishes its outcome");

        // Nothing was written: the store still shows the attempt this worker started.
        var record = (await jobs.PeekAsync(jobId)).ShouldNotBeNull();

        record.Status.ShouldBe(JobStatus.Running);
        record.Attempt.ShouldBe(1);
    }

    /// <summary>
    /// Renewals that fail past the point the lease can be proven leave the worker unable to say
    /// whether another worker holds the job. It stops the handler rather than guess.
    /// </summary>
    [Fact]
    public async Task Handler_is_cancelled_when_the_lease_cannot_be_proven()
    {
        var jobs = new LeaseLossJobStore { Renewal = RenewalAnswer.Throw };
        var handler = new StopsWhenCancelledHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMilliseconds(300));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Cancelled.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.ReasonWhenCancelled.ShouldBe(JobStopReason.LeaseLost);
        jobs.RenewCalls.ShouldBeGreaterThanOrEqualTo(1, "the worker tried before it gave the lease up");
        jobs.CompleteCalls.ShouldBe(0);
        jobs.ReleaseCalls.ShouldBe(0);
        (await jobs.PeekAsync(jobId))!.Status.ShouldBe(JobStatus.Running);
    }

    /// <summary>One missed renewal is inside the margin a third of the lease leaves; it is not a lost lease.</summary>
    [Fact]
    public async Task A_transient_store_error_in_renewal_does_not_lose_the_lease()
    {
        // 🚨 The clock is never advanced. The renewal timer still ticks in real time (a TimeProvider that does not
        // override CreateTimer uses the system timer), but the time a lease may go unproven is read from this clock.
        // On the real clock the margin was one renewal interval, 300 ms: a tick that a loaded machine delayed by that
        // much made the worker drop a healthy lease, the renewals stopped at one, and the wait below ran out its 30 s.
        var clock = new ManualTimeProvider();
        var jobs = new LeaseLossJobStore(clock) { Renewal = RenewalAnswer.ThrowOnce };
        var handler = new StopsWhenCancelledHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        var jobId = await EnqueueAsync(jobs, maxAttempts: 1, clock.GetUtcNow());

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMilliseconds(900), clock);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Started.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        // The first renewal throws, the second succeeds: three calls mean the lease survived the miss.
        await WaitUntil.TrueAsync(() => jobs.RenewCalls >= 3, "the lease to be renewed after the missed renewal");

        handler.WasCancelled.ShouldBeFalse("a single missed renewal stopped a healthy handler");

        handler.Release();

        var record = await WaitUntil.ValueAsync(
            () => jobs.PeekAsync(jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to finish");

        await worker.StopAsync(TestContext.Current.CancellationToken);

        record!.Status.ShouldBe(JobStatus.Completed);
    }

    /// <summary>
    /// A cancelled job refuses a renewal too (cancelling clears the lease). That is the cancel watch's case:
    /// telling the handler its lease was lost would leave a cancelled job's checkpoints behind, and the job
    /// would not be counted as cancelled.
    /// </summary>
    [Fact]
    public async Task A_renewal_refused_because_the_job_was_cancelled_is_not_a_lost_lease()
    {
        var jobs = new LeaseLossJobStore();
        var handler = new StopsWhenCancelledHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        // The renewal (every 100 ms) reaches the cancel long before the cancel watch (every 1.5 s) does.
        using var worker = CreateWorker(
            jobs,
            host,
            TimeSpan.FromMilliseconds(300),
            metrics: metrics,
            pollInterval: TimeSpan.FromMilliseconds(1500));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Started.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        (await jobs.CancelInnerAsync(jobId)).ShouldBeTrue();

        await WaitUntil.TrueAsync(() => jobs.RefusedRenewals >= 1, "the store to refuse the cancelled job's renewal");
        await handler.Cancelled.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.ReasonWhenCancelled.ShouldBe(JobStopReason.Cancelled);
        (await jobs.PeekAsync(jobId))!.Status.ShouldBe(JobStatus.Cancelled);
        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldHaveSingleItem()[TraconDiagnostics.Tags.JobStatus]
            .ShouldBe(nameof(JobStatus.Cancelled));
    }

    /// <summary>
    /// The renewal said the lease was fine and the write lost the race (the lease moved in between). The refusal is an answer:
    /// no retry, no metric, no outcome.
    /// </summary>
    [Fact]
    public async Task A_lost_lease_publishes_no_outcome_and_counts_no_job()
    {
        var jobs = new LeaseLossJobStore { RefuseWrites = true };
        var handler = new CountingHandler();
        var publisher = new RecordingPublisher();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMinutes(5), metrics: metrics, publisher: publisher);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => jobs.CompleteCalls >= 1, "the worker to try to complete the job");
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.CompleteCalls.ShouldBe(1, "a refusal is an answer: the same call would be refused again");
        handler.Calls.ShouldBe(1);
        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldBeEmpty();
        publisher.Published.ShouldBeEmpty();
        (await jobs.PeekAsync(jobId))!.Status.ShouldBe(JobStatus.Running);
    }

    /// <summary>A failed attempt whose release is refused is not released again and does not run again.</summary>
    [Fact]
    public async Task A_release_the_store_refuses_is_not_tried_again()
    {
        var jobs = new LeaseLossJobStore { RefuseWrites = true };
        var handler = new FailingHandler(failures: int.MaxValue);
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMinutes(5));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => jobs.ReleaseCalls >= 1, "the worker to try to release the job");
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.ReleaseCalls.ShouldBe(1);
        handler.Calls.ShouldBe(1);
    }

    /// <summary>
    /// The store committed the completion and only its answer was lost. The next try finds the job closed and the
    /// store answers <see langword="false"/> (the lease is gone), which must not read as another worker's job:
    /// the outcome is counted and published once.
    /// </summary>
    [Fact]
    public async Task A_completion_whose_answer_was_lost_is_still_counted_and_published()
    {
        var jobs = new LostAnswerJobStore();
        var handler = new CountingHandler(jobs.Arm);
        var publisher = new RecordingPublisher();
        var logger = new CollectingLogger();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        var jobId = await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMinutes(5), metrics: metrics, publisher: publisher, logger: logger);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => publisher.Published.Count > 0, "the worker to publish the completed job");
        await worker.StopAsync(TestContext.Current.CancellationToken);

        jobs.Threw.ShouldBeTrue("the lost answer was never raised, so the case proves nothing");
        (await jobs.PeekAsync(jobId))!.Status.ShouldBe(JobStatus.Completed);
        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldHaveSingleItem()[TraconDiagnostics.Tags.JobStatus]
            .ShouldBe(nameof(JobStatus.Completed));
        publisher.Published.ShouldBe(["job.completed"]);
        logger.Entries.ShouldNotContain(entry => entry.Message.Contains("no longer holds the lease", StringComparison.Ordinal));
    }

    /// <summary>
    /// A refused renewal whose status read fails cannot be told apart from a cancelled job, so the lease is given up
    /// only when it can no longer be proven. The handler is stopped then, as it is for renewals that fail.
    /// </summary>
    [Fact]
    public async Task A_refused_renewal_whose_status_read_fails_is_a_lost_lease_once_the_lease_cannot_be_proven()
    {
        var jobs = new LeaseLossJobStore { Renewal = RenewalAnswer.Refuse, GetFails = true };
        var handler = new StopsWhenCancelledHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMilliseconds(300));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await handler.Cancelled.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        handler.ReasonWhenCancelled.ShouldBe(JobStopReason.LeaseLost);
        jobs.RefusedRenewals.ShouldBeGreaterThanOrEqualTo(1, "the worker asked before it gave the lease up");
        jobs.CompleteCalls.ShouldBe(0);
        jobs.ReleaseCalls.ShouldBe(0);
    }

    /// <summary>A refused completion whose status read fails counts and publishes nothing, and says why.</summary>
    [Fact]
    public async Task A_refused_write_whose_status_read_fails_is_not_counted()
    {
        var jobs = new LeaseLossJobStore { RefuseWrites = true, GetFails = true };
        var handler = new CountingHandler();
        var publisher = new RecordingPublisher();
        var logger = new CollectingLogger();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));
        using var meters = new TestMeterFactory();
        using var collector = new MetricCollector(meters.Meter);
        using var metrics = new TraconMetrics(meters, TenantTaggedMetricsOptions.Create());

        await EnqueueAsync(jobs, maxAttempts: 3, DateTimeOffset.UtcNow);

        using var worker = CreateWorker(jobs, host, TimeSpan.FromMinutes(5), metrics: metrics, publisher: publisher, logger: logger);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => jobs.CompleteCalls >= 1, "the worker to try to complete the job");
        await AttemptEndedAsync(jobs);
        await worker.StopAsync(TestContext.Current.CancellationToken);

        collector.LongTags(TraconDiagnostics.JobCounterName).ShouldBeEmpty();
        publisher.Published.ShouldBeEmpty();
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("no longer holds the lease", StringComparison.Ordinal));
    }

    /// <summary>
    /// The attempt is over once the worker asks for another job: a worker with one slot leases only while
    /// the slot is free. Stopping the worker earlier would let the shutdown decide the outcome.
    /// </summary>
    private static async Task AttemptEndedAsync(LeaseLossJobStore jobs)
    {
        var calls = jobs.LeaseCalls;

        await WaitUntil.TrueAsync(() => jobs.LeaseCalls > calls, "the worker to finish the attempt and ask for another job");
    }

    /// <summary>The store operation a <see cref="ThrowOnceJobStore"/> fails.</summary>
    public enum StoreCall
    {
        /// <summary><see cref="IJobStore.ListItemsAsync"/>.</summary>
        ListItems,

        /// <summary><see cref="IJobStore.CompleteAsync"/>.</summary>
        Complete,

        /// <summary><see cref="IJobStore.ReleaseForRetryAsync"/>.</summary>
        ReleaseForRetry,
    }

    private static JobWorkerBackgroundService CreateWorker(
        IJobStore jobs,
        TestJobHandlerHost host,
        TimeSpan leaseDuration,
        TimeProvider? clock = null,
        TraconMetrics? metrics = null,
        IWebhookPublisher? publisher = null,
        TimeSpan? pollInterval = null,
        ILogger<JobWorkerBackgroundService>? logger = null)
        => new(
            jobs,
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions
            {
                MaxConcurrentJobs = 1,
                PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(5),
                LeaseDuration = leaseDuration,
                MaxAttempts = 3,
            }),
            new SchemaReadyGate([]),
            new NotDraining(),
            timeProvider: clock,
            metrics: metrics,
            webhookPublisher: publisher,
            logger: logger ?? NullLogger<JobWorkerBackgroundService>.Instance);

    private static async Task<Guid> EnqueueAsync(IJobStore jobs, int maxAttempts, DateTimeOffset now)
    {
        var id = Guid.NewGuid();

        await jobs.EnqueueAsync(
            new JobRecord
            {
                Id = id,
                TenantId = TenantId,
                HandlerKey = JobHandlerKeys.AgentBatch,
                TargetName = "lease-contract",
                Status = JobStatus.Pending,
                MaxAttempts = maxAttempts,
                ScheduledFor = now,
                CreatedAt = now,
            },
            [],
            TestContext.Current.CancellationToken);

        return id;
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    private sealed class BlockingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.ConfigureAwait(false);
        }
    }

    /// <summary>Counts its calls and succeeds; <paramref name="onCall"/> runs inside each call.</summary>
    private sealed class CountingHandler(Action? onCall = null) : IJobHandler
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task Called => _called.Task;

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            onCall?.Invoke();
            _called.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Throws on its first <paramref name="failures"/> calls, then succeeds.</summary>
    private sealed class FailingHandler(int failures, Action? onCall = null) : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            onCall?.Invoke();

            return call <= failures
                ? throw new InvalidOperationException("The handler failed on purpose.")
                : ValueTask.CompletedTask;
        }
    }

    /// <summary>Did its work, then could not record it: what a queued turn throws when its session never saves.</summary>
    private sealed class NonRetryableHandler : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            throw new NonRetryableJobException(
                QueuedTurn.SessionSaveFailedCode,
                "abcd1234",
                new TimeoutException("The session store did not answer in time."));
        }
    }

    /// <summary>Cancels its own job, as an operator would mid-run, then fails on its way out.</summary>
    /// <param name="jobs">The store the handler cancels its job in.</param>
    /// <param name="waitForTheWatch">
    /// Throws only after the worker's cancel watch has read the cancel and stopped the
    /// handler's token. Without the wait the order is a race between the throw and the
    /// watch's next tick, and the worker writes nothing after the throw when the watch wins.
    /// </param>
    private sealed class CancelThenThrowHandler(ObservingJobStore jobs, bool waitForTheWatch) : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            (await jobs.CancelAsync(context.Job.TenantId, context.Job.Id, cancellationToken)).ShouldBeTrue();
            (await context.IsCancelledAsync(cancellationToken)).ShouldBeTrue();

            if (waitForTheWatch)
            {
                await Should.ThrowAsync<OperationCanceledException>(
                    () => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)); // delay: simulated
            }

            throw new InvalidOperationException("The handler stopped because its job was cancelled.");
        }
    }

    private abstract class DelegatingJobStore(TimeProvider? clock = null) : IJobStore
    {
        protected InMemoryJobStore Inner { get; } = new(clock);

        /// <summary>Reads a job past any fault the derived store injects.</summary>
        public ValueTask<JobRecord?> PeekAsync(Guid jobId)
            => Inner.GetAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => Inner.EnqueueAsync(job, items, cancellationToken);

        public virtual ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
            => Inner.LeaseAsync(request, cancellationToken);

        public virtual ValueTask<bool> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Inner.RenewLeaseAsync(lease, leaseDuration, cancellationToken);

        public virtual ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default)
            => Inner.MarkRunningAsync(lease, cancellationToken);

        public virtual ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
            => Inner.CompleteAsync(lease, completion, cancellationToken);

        public virtual ValueTask<bool> ReleaseForRetryAsync(JobLease lease, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => Inner.ReleaseForRetryAsync(lease, errorMessage, retryAfter, cancellationToken);

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => Inner.CancelAsync(tenantId, jobId, cancellationToken);

        public virtual ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => Inner.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
            => Inner.QueryAsync(query, cancellationToken);

        public virtual ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
            => Inner.ListItemsAsync(jobId, cancellationToken);

        public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
            => Inner.ReportItemAsync(item, cancellationToken);

        public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => Inner.GetQueueDepthAsync(cancellationToken);
    }

    /// <summary>Throws on the first renewal, the way a store that blips once does.</summary>
    private sealed class FailFirstRenewalJobStore : DelegatingJobStore
    {
        private int _renewCalls;

        public int RenewCalls => Volatile.Read(ref _renewCalls);

        public override ValueTask<bool> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _renewCalls) == 1
                ? throw new TimeoutException("The store did not answer in time.")
                : base.RenewLeaseAsync(lease, leaseDuration, cancellationToken);
    }

    /// <summary>
    /// Fails the first call of one operation made after <see cref="Arm"/>, the
    /// way a store that blips once does. Every other call reaches the store.
    /// </summary>
    private sealed class ThrowOnceJobStore(StoreCall failing) : DelegatingJobStore
    {
        private int _armed;
        private int _thrown;

        public bool Threw => Volatile.Read(ref _thrown) == 1;

        /// <summary>Called by the handler, so only the worker's calls after it can fail.</summary>
        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.ListItems);
            return base.ListItemsAsync(jobId, cancellationToken);
        }

        public override ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.Complete);
            return base.CompleteAsync(lease, completion, cancellationToken);
        }

        public override ValueTask<bool> ReleaseForRetryAsync(JobLease lease, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
        {
            ThrowOnce(StoreCall.ReleaseForRetry);
            return base.ReleaseForRetryAsync(lease, errorMessage, retryAfter, cancellationToken);
        }

        private void ThrowOnce(StoreCall call)
        {
            if (call == failing
                && Volatile.Read(ref _armed) == 1
                && Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
            {
                throw new TimeoutException("The store did not answer in time.");
            }
        }
    }

    /// <summary>
    /// Refuses every <see cref="JobStatus.Completed"/> completion made after
    /// <see cref="Arm"/>; a <see cref="JobStatus.Failed"/> one goes through.
    /// </summary>
    private sealed class RefusingCompletionJobStore(TimeProvider? clock = null) : DelegatingJobStore(clock)
    {
        private int _armed;
        private int _refused;

        public int RefusedCompletions => Volatile.Read(ref _refused);

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
        {
            if (completion.Status == JobStatus.Completed && Volatile.Read(ref _armed) == 1)
            {
                Interlocked.Increment(ref _refused);
                throw new TimeoutException("The store did not answer in time.");
            }

            return base.CompleteAsync(lease, completion, cancellationToken);
        }
    }

    /// <summary>How <see cref="LeaseLossJobStore"/> answers a renewal.</summary>
    private enum RenewalAnswer
    {
        /// <summary>Reaches the store.</summary>
        Pass,

        /// <summary>Answers <see langword="false"/>: the lease is not this worker's.</summary>
        Refuse,

        /// <summary>Throws every time, the way a store that is down does.</summary>
        Throw,

        /// <summary>Throws the first call, then reaches the store.</summary>
        ThrowOnce,
    }

    /// <summary>Counts the worker's lease-bound calls and answers its renewals, and optionally its writes, as a store that lost the lease would.</summary>
    private sealed class LeaseLossJobStore(TimeProvider? clock = null) : DelegatingJobStore(clock)
    {
        private int _renewCalls;
        private int _refusedRenewals;
        private int _completeCalls;
        private int _releaseCalls;
        private int _leaseCalls;

        public RenewalAnswer Renewal { get; init; } = RenewalAnswer.Pass;

        /// <summary>Gets whether completion and release answer <see langword="false"/> without writing.</summary>
        public bool RefuseWrites { get; init; }

        /// <summary>Gets whether every status read throws, the way a store that is down does.</summary>
        public bool GetFails { get; init; }

        public int RenewCalls => Volatile.Read(ref _renewCalls);

        public int RefusedRenewals => Volatile.Read(ref _refusedRenewals);

        public int CompleteCalls => Volatile.Read(ref _completeCalls);

        public int ReleaseCalls => Volatile.Read(ref _releaseCalls);

        public int LeaseCalls => Volatile.Read(ref _leaseCalls);

        public override ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => GetFails
                ? throw new TimeoutException("The store did not answer in time.")
                : base.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<bool> CancelInnerAsync(Guid jobId)
            => Inner.CancelAsync(TenantId, jobId, TestContext.Current.CancellationToken);

        public override ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _leaseCalls);

            return base.LeaseAsync(request, cancellationToken);
        }

        public override async ValueTask<bool> RenewLeaseAsync(JobLease lease, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _renewCalls);

            if (Renewal == RenewalAnswer.Throw || (Renewal == RenewalAnswer.ThrowOnce && call == 1))
            {
                throw new TimeoutException("The store did not answer in time.");
            }

            var renewed = Renewal != RenewalAnswer.Refuse
                && await base.RenewLeaseAsync(lease, leaseDuration, cancellationToken);

            if (!renewed)
            {
                Interlocked.Increment(ref _refusedRenewals);
            }

            return renewed;
        }

        public override ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _completeCalls);

            return RefuseWrites ? new ValueTask<bool>(false) : base.CompleteAsync(lease, completion, cancellationToken);
        }

        public override ValueTask<bool> ReleaseForRetryAsync(JobLease lease, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _releaseCalls);

            return RefuseWrites ? new ValueTask<bool>(false) : base.ReleaseForRetryAsync(lease, errorMessage, retryAfter, cancellationToken);
        }
    }

    /// <summary>Waits until its token is cancelled (or it is released) and records why the worker cancelled it.</summary>
    /// <param name="returnsNormally">Returns when cancelled, as a handler that swallows the cancellation does; throws otherwise.</param>
    private sealed class StopsWhenCancelledHandler(bool returnsNormally = false) : IJobHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        private int _cancellations;

        public int Calls => Volatile.Read(ref _calls);

        public Task Started => _started.Task;

        public Task Cancelled => _cancelled.Task;

        public bool WasCancelled => _cancelled.Task.IsCompleted;

        public JobStopReason ReasonWhenCancelled { get; private set; }

        public void Release() => _release.TrySetResult();

        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            _started.TrySetResult();

            var stopped = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated

            if (await Task.WhenAny(stopped, _release.Task) == _release.Task)
            {
                // Released by the test: the handler finishes its work.
                return;
            }

            // The reason of the FIRST cancellation. An expired lease lets the worker take the job again, and
            // that second call sees the worker stopping; it must not overwrite what the first one saw.
            if (Interlocked.CompareExchange(ref _cancellations, 1, 0) == 0)
            {
                ReasonWhenCancelled = context.Signals!.Reason;
                _cancelled.TrySetResult();
            }

            if (!returnsNormally)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    private sealed class CollectingLogger : ILogger<JobWorkerBackgroundService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class RecordingPublisher : IWebhookPublisher
    {
        private readonly List<string> _published = [];

        public IReadOnlyList<string> Published
        {
            get
            {
                lock (_published)
                {
                    return [.. _published];
                }
            }
        }

        public ValueTask<int> PublishAsync(string tenantId, string eventType, WebhookEventPayload payload, CancellationToken cancellationToken = default)
        {
            lock (_published)
            {
                _published.Add(eventType);
            }

            return new ValueTask<int>(1);
        }
    }

    /// <summary>
    /// Applies the first completion made after <see cref="Arm"/> and then throws: the write committed and only its
    /// answer was lost, the way a dropped connection loses it.
    /// </summary>
    private sealed class LostAnswerJobStore : DelegatingJobStore
    {
        private int _armed;
        private int _thrown;

        public bool Threw => Volatile.Read(ref _thrown) == 1;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default)
        {
            var applied = await base.CompleteAsync(lease, completion, cancellationToken);

            if (Volatile.Read(ref _armed) == 1 && Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
            {
                throw new TimeoutException("The store did not answer in time.");
            }

            return applied;
        }
    }

    /// <summary>
    /// Records the worker's store calls around the moments the cases care about.
    /// Each signal fires AFTER the call it reports has reached the inner store.
    /// </summary>
    private sealed class ObservingJobStore : DelegatingJobStore
    {
        private readonly TaskCompletionSource _readAfterMarkRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _markedRunning;

        /// <summary>Cancels the job just before the worker marks it running.</summary>
        public bool CancelBeforeMarkRunning { get; init; }

        /// <summary>The worker's first read after marking the job running failed.</summary>
        public Task ReadAfterMarkRunning => _readAfterMarkRunning.Task;

        public override async ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default)
        {
            if (CancelBeforeMarkRunning)
            {
                await Inner.CancelAsync(TenantId, lease.JobId, cancellationToken);
            }

            var marked = await base.MarkRunningAsync(lease, cancellationToken);

            _markedRunning = true;

            return marked;
        }

        public override async ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        {
            var record = await base.GetAsync(tenantId, jobId, cancellationToken);

            if (_markedRunning)
            {
                _readAfterMarkRunning.TrySetResult();
            }

            return record;
        }
    }
}
