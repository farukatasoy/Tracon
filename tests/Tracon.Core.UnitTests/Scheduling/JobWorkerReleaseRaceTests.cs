using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// A job released for another attempt is leasable at once, and the worker can lease
/// it again before the attempt that released it has finished on this worker.
/// </summary>
/// <remarks>
/// Measured (2026-10-15, F-547): the second lease met the first attempt still in the
/// worker's running set, was logged as "already executing" and dropped. The store had
/// already counted the attempt and set a five-minute lease, so the job stayed
/// <see cref="JobStatus.Leased"/> and, once the lease ran out, failed as out of
/// attempts without its second attempt ever running. The deadline test showed it one
/// run in 25 on macOS and on every Windows CI run; this class holds the window open.
/// </remarks>
public sealed class JobWorkerReleaseRaceTests
{
    [Fact]
    public async Task A_job_leased_again_while_its_previous_attempt_is_still_finishing_runs_that_attempt()
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new FailOnceHandler();
        var (worker, host) = Worker(store, handler);

        // The release returns to the worker only after the worker leased the job again,
        // so the first attempt is still in the running set when the second lease arrives.
        var leasedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Leased = job =>
        {
            if (job.Attempt == 2)
            {
                leasedAgain.TrySetResult();
            }
        };
        store.AfterReleaseForRetry = _ => leasedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "flaky", maxAttempts: 2);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            job.Attempt.ShouldBe(2);
            handler.Calls.ShouldBe(2);
        }
    }

    [Fact]
    public async Task A_lease_that_arrives_after_the_job_finished_elsewhere_does_not_run_the_handler_again()
    {
        // The other side of the same guard: the previous attempt completed the job while
        // the new lease waited, so the new lease has nothing left to run.
        var store = new ProbeJobStore(new InMemoryJobStore());
        var handler = new FailOnceHandler();
        var (worker, host) = Worker(store, handler);

        var leasedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Leased = job =>
        {
            if (job.Attempt == 2)
            {
                leasedAgain.TrySetResult();
            }
        };
        store.AfterReleaseForRetry = async jobId =>
        {
            await leasedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Something else finished the job before the waiting lease could run it.
            await store.CompleteAsync(new JobCompletion
            {
                JobId = jobId,
                Status = JobStatus.Completed,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        };

        using (host)
        using (worker)
        {
            var id = await EnqueueAsync(store, "flaky", maxAttempts: 2);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            var job = await WaitForAsync(store, id, JobStatus.Completed, JobStatus.Failed);

            await worker.StopAsync(TestContext.Current.CancellationToken);

            job.Status.ShouldBe(JobStatus.Completed);
            handler.Calls.ShouldBe(1, "the job was already finished when the second lease could run");
        }
    }

    private sealed class FailOnceHandler : IJobHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("The first attempt fails (probe).")
                : ValueTask.CompletedTask;
    }
}
