using static Tracon.Core.UnitTests.Scheduling.JobAttemptProbes;

namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// A host that stops while a leased job still waits for its slot's previous attempt must not
/// leave a faulted task behind.
/// </summary>
/// <remarks>
/// <para>
/// A job released for another attempt is leased again at once; the new attempt takes a slot
/// and then waits for the previous attempt to finish on this worker. While it waits it is in
/// no list the worker's shutdown looks at, so a stop that arrives in that window ended the
/// worker's loop, disposed the slot pool, and only then let the waiting attempt run: its
/// <c>Release()</c> met a disposed semaphore and the exception sat in a task nobody awaited.
/// It showed once in a full run of the unit test project as exit code 7.
/// </para>
/// <para>
/// Which of two continuations of the finishing attempt runs first decides it, so the test
/// repeats the window many times at once and reads the whole outcome after the collector
/// has run: the shared guard only reports at the end of the run, where a hit cannot be tied
/// to its test.
/// </para>
/// </remarks>
public sealed class JobWorkerStopDropsNoTaskTests
{
    private const int Rounds = 24;
    private const int Parallel = 8;

    [Fact]
    public async Task A_stop_while_a_second_attempt_waits_for_the_first_drops_no_task_and_ends_the_worker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var remaining = Rounds;

        var lanes = Enumerable.Range(0, Parallel).Select(async _ =>
        {
            while (Interlocked.Decrement(ref remaining) >= 0)
            {
                await StopInTheWindowAsync(cancellationToken);
            }
        });

        await Task.WhenAll(lanes);

        UnobservedTaskGuard.ForceCollection();

        // Only this worker's own failures are claimed: the guard reports every other test's at the end of the run.
        var dropped = UnobservedTaskGuard.Take(static exception =>
            exception.StackTrace is { } stack
            && (stack.Contains(nameof(JobWorkerBackgroundService), StringComparison.Ordinal)
                || stack.Contains("SlotPool", StringComparison.Ordinal)));

        dropped.ShouldBeEmpty(
            "a job that held a slot after the slot pool was disposed left a faulted task nobody observed: "
            + string.Join(" | ", dropped.Select(static exception => exception.Message)));
    }

    private static async Task StopInTheWindowAsync(CancellationToken cancellationToken)
    {
        var store = new ProbeJobStore(new InMemoryJobStore());
        var (worker, host) = Worker(store, new FailOnceHandler());

        // The first attempt is held in its release until the host is stopping; the worker
        // leases the job again in the meantime and that attempt waits for this one.
        var leasedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstMayFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        store.Leased = job =>
        {
            if (job.Attempt == 2)
            {
                leasedAgain.TrySetResult();
            }
        };
        store.AfterReleaseForRetry = _ => firstMayFinish.Task;

        using (host)
        using (worker)
        {
            await EnqueueAsync(store, "flaky", maxAttempts: 2);
            await worker.StartAsync(cancellationToken);
            await leasedAgain.Task.WaitAsync(WaitUntil.DefaultTimeout, cancellationToken);

            var stopping = worker.StopAsync(CancellationToken.None);

            // No signal says the worker has reached its wait for running jobs; the pause only
            // lets it get there before the first attempt finishes.
            await Task.Delay(TimeSpan.FromMilliseconds(15), cancellationToken); // delay: negative

            firstMayFinish.TrySetResult();

            await stopping.WaitAsync(WaitUntil.DefaultTimeout, cancellationToken);
        }
    }

    private sealed class FailOnceHandler : IJobHandler
    {
        private int _calls;

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => Interlocked.Increment(ref _calls) == 1
                ? throw new InvalidOperationException("The first attempt fails (probe).")
                : ValueTask.CompletedTask;
    }
}
