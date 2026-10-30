using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// A cancel reaches the running handler's token (failure modes 14, 17 and 18).
/// </summary>
/// <remarks>
/// <para>
/// Before that change, the cancel endpoint only wrote the store, and the token a handler
/// received was cancelled only when the worker shut down: a cancelled job ran on until
/// its handler happened to ask (<see cref="JobContext.IsCancelledAsync"/>), between items.
/// The worker now reads a running job's status every <c>PollInterval</c>.
/// </para>
/// <para>
/// The HTTP half - the endpoint, and another tenant's cancel - is
/// <c>JobCancelEndpointTests</c> in the functional tests.
/// </para>
/// </remarks>
public sealed class JobWorkerCancellationTests
{
    [Fact]
    public async Task A_cancel_reaches_the_running_handlers_token_and_the_job_stays_Cancelled()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            (await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldBeTrue();

            // The token falls within a poll interval or so; the bound is a failure bound.
            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.Cancelled);

            await WaitUntil.TrueAsync(() => handler.For("slow").Token.IsCancellationRequested, "the handler's token to be cancelled");

            var job = await WaitForAsync(store, id, JobStatus.Cancelled);

            job.ErrorMessage.ShouldBeNull("a cancelled job carries no error, the handler's exit included");

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// The DoD's "within one <c>PollInterval</c>", measured on the worker's own clock: the
    /// watch reads the job when one poll interval has passed and not before, so a cancel
    /// reaches the token at that tick. The test above bounds the same path only by a
    /// failure timeout.
    /// </summary>
    [Fact]
    public async Task A_cancel_reaches_the_handler_at_the_next_poll_interval_of_the_workers_clock_and_not_before()
    {
        var clock = new ManualClock();
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler, clock: clock);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            // The watch is armed one poll interval ahead, on the worker's clock.
            clock.Pending.ShouldContain(Poll);

            (await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldBeTrue();
            var reads = store.Reads;

            clock.Advance(Poll - TimeSpan.FromTicks(1));

            store.Reads.ShouldBe(reads, "nothing reads the job before a poll interval has passed");
            handler.For("slow").Token.IsCancellationRequested.ShouldBeFalse();

            clock.Advance(TimeSpan.FromTicks(1));

            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.Cancelled);
            store.Reads.ShouldBeGreaterThan(reads);

            (await WaitForAsync(store, id, JobStatus.Cancelled)).ErrorMessage.ShouldBeNull();

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Until_a_cancel_the_watch_reads_the_job_and_leaves_the_handler_alone()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler { DefaultBehavior = ProbeBehavior.IgnoreToken };
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            var before = store.Reads;

            // Several poll intervals pass with reads and no cancel.
            await WaitUntil.TrueAsync(() => store.Reads >= before + 3, "the watch to read the running job");

            handler.For("slow").Token.IsCancellationRequested.ShouldBeFalse();

            handler.For("slow").Release.TrySetResult();
            (await WaitForAsync(store, id, JobStatus.Completed)).Status.ShouldBe(JobStatus.Completed);

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_store_error_in_the_watch_neither_ends_the_watch_nor_touches_the_handler()
    {
        // The rule for a per-tick loop: one failed read must not end it.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            var before = store.Reads;
            store.FailNextReads(3);

            await WaitUntil.TrueAsync(() => store.Reads >= before + 4, "the watch to read past the failures");
            handler.For("slow").Token.IsCancellationRequested.ShouldBeFalse("a failed read is not a cancel");

            (await store.CancelAsync(Tenant, id, TestContext.Current.CancellationToken)).ShouldBeTrue();

            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            await WaitForAsync(store, id, JobStatus.Cancelled);

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task One_jobs_cancel_does_not_cancel_another_and_the_watch_ends_with_its_handler()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new TokenProbeHandler();
        handler.For("other").Behavior = ProbeBehavior.IgnoreToken;
        var (worker, host) = Worker(store, handler);

        using (host)
        using (worker)
        {
            var cancelled = await EnqueueAsync(store, "slow");
            var other = await EnqueueAsync(store, "other");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);
            await handler.For("other").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            (await store.CancelAsync(Tenant, cancelled, TestContext.Current.CancellationToken)).ShouldBeTrue();
            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            await WaitForAsync(store, cancelled, JobStatus.Cancelled);

            handler.For("other").Token.IsCancellationRequested.ShouldBeFalse("each job has its own sources");

            handler.For("other").Release.TrySetResult();
            await WaitForAsync(store, other, JobStatus.Completed);

            // No handler runs now: the reads must stop with their handlers. The
            // worker's own lease poll does not read a single job. The finalization's
            // one read after the status is written gets a few intervals first.
            await Task.Delay(Poll * 5, TestContext.Current.CancellationToken); // delay: negative
            var settled = store.Reads;

            await Task.Delay(Poll * 10, TestContext.Current.CancellationToken); // delay: negative

            store.Reads.ShouldBe(settled, "a watch outlived the handler it watched");

            await worker.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_job_whose_record_is_gone_is_treated_as_cancelled_and_nothing_is_written()
    {
        var inner = new InMemoryJobStore();
        var store = new ProbeJobStore(inner);
        var handler = new TokenProbeHandler();
        var (worker, host) = Worker(store, handler, static options => options.MaxAttempts = 3);

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "slow", maxAttempts: 3);

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await handler.For("slow").Started.Task.WaitAsync(TestContext.Current.CancellationToken);

            store.Gone = id;

            await handler.For("slow").Cancelled.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            handler.For("slow").Signals!.Reason.ShouldBe(JobStopReason.Cancelled);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            // The worker did not release it for another attempt: the row is untouched.
            var row = await inner.GetAsync(Tenant, id, TestContext.Current.CancellationToken);

            row!.Status.ShouldBe(JobStatus.Running);
            row.ErrorMessage.ShouldBeNull();
        }
    }
}
