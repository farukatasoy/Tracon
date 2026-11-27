using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a real host stop does to a job whose handler is running, with the queue in a SQLite file two hosts share: a
/// handler that had not begun its work hands the attempt back, one that finishes anyway completes its job, and the next
/// host runs what is left.
/// </summary>
/// <remarks>
/// The worker-level rules are <c>JobWorkerShutdownTests</c>. These cases add what a unit test cannot show: the stop is the
/// host's own (<c>IHost.StopAsync</c>, through <see cref="TraconTestHost.DisposeAsync"/>), the handler is built by the
/// container and the store is a database that outlives the host.
/// </remarks>
public sealed class JobShutdownHostTests
{
    private const string Key = "contoso.shutdown-probe";

    [Fact]
    public async Task A_job_limited_to_one_attempt_runs_on_the_next_host_after_a_handler_that_had_not_begun_was_cut_off()
    {
        using var database = new TempSqliteDatabase("job-shutdown");
        var firstProbe = new Probe();
        Guid jobId;
        string tenantId;

        await using (var first = await StartAsync(database, firstProbe, static builder => builder.AddJobHandler<InterruptedOnStopHandler>(Key)))
        {
            tenantId = first.Services.GetRequiredService<ITenantContext>().TenantId;
            jobId = await EnqueueAsync(first, tenantId, maxAttempts: 1);

            await firstProbe.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // Leaving the block stops the host: its worker cancels the handler's token, the handler says it had not begun,
            // and the job goes back to the queue before the host is gone.
        }

        var secondProbe = new Probe();

        await using var second = await StartAsync(database, secondProbe, static builder => builder.AddJobHandler<CompletingHandler>(Key));

        var done = await WaitUntil.ValueAsync(
            () => second.Services.GetRequiredService<IJobStore>().GetAsync(tenantId, jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to be run by the second host");

        done!.Status.ShouldBe(JobStatus.Completed, done.ErrorMessage);
        done.Attempt.ShouldBe(1, "the cut-off lease counted an attempt that was never used");
        secondProbe.Ran.Task.IsCompletedSuccessfully.ShouldBeTrue("the handler did not run on the second host");
    }

    [Fact]
    public async Task A_handler_that_finishes_while_the_host_stops_completes_its_job_and_the_next_host_does_not_run_it_again()
    {
        using var database = new TempSqliteDatabase("job-shutdown");
        var firstProbe = new Probe();
        var first = await StartAsync(database, firstProbe, static builder => builder.AddJobHandler<FinishesAfterStopHandler>(Key));
        var stopping = Task.CompletedTask;
        var disposed = false;
        Guid jobId;
        string tenantId;

        try
        {
            tenantId = first.Services.GetRequiredService<ITenantContext>().TenantId;
            jobId = await EnqueueAsync(first, tenantId, maxAttempts: 1);

            await firstProbe.Started.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

            // The host stops while the handler is still at its work; the handler learns of the stop and finishes anyway.
            disposed = true;
            stopping = first.DisposeAsync().AsTask();

            await firstProbe.SawTheStop.Task.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            firstProbe.Finish.TrySetResult();
        }
        finally
        {
            if (!disposed)
            {
                await first.DisposeAsync();
            }
        }

        await stopping.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        var secondProbe = new Probe();

        await using var second = await StartAsync(database, secondProbe, static builder => builder.AddJobHandler<CompletingHandler>(Key));

        var job = (await second.Services.GetRequiredService<IJobStore>().GetAsync(tenantId, jobId)).ShouldNotBeNull();

        job.Status.ShouldBe(JobStatus.Completed, "the work was done; the stop lost its completion and the job would run again");
        job.Attempt.ShouldBe(1);
        secondProbe.Ran.Task.IsCompleted.ShouldBeFalse("the second host ran a job that was already complete");
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database, Probe probe, Action<ITraconBuilder> addHandler)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                addHandler(builder);
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            },
            configureServices: services => services.AddSingleton(probe));

    private static async Task<Guid> EnqueueAsync(TraconTestHost host, string tenantId, int maxAttempts)
    {
        var now = DateTimeOffset.UtcNow;
        var queued = await host.Services.GetRequiredService<IJobStore>().EnqueueAsync(
            new JobRecord
            {
                Id = TraconId.NewId(),
                TenantId = tenantId,
                HandlerKey = Key,
                TargetName = "shutdown-probe",
                Status = JobStatus.Pending,
                Payload = JsonSerializer.SerializeToElement(new { }),
                MaxAttempts = maxAttempts,
                ScheduledFor = now,
                CreatedAt = now,
            },
            []);

        return queued.Id;
    }

    /// <summary>The signals one host's handlers and the test pass between them.</summary>
    private sealed class Probe
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SawTheStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Says it started, waits for the host to stop, and then says it had not begun its work.</summary>
    private sealed class InterruptedOnStopHandler(Probe probe) : IJobHandler
    {
        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            probe.Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated
            }
            catch (OperationCanceledException)
            {
                throw new JobInterruptedException("The host stopped before the first call out.");
            }
        }
    }

    /// <summary>Notices the host's stop, and returns normally once the test lets it: its work is done.</summary>
    private sealed class FinishesAfterStopHandler(Probe probe) : IJobHandler
    {
        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken.Register(static state => ((Probe)state!).SawTheStop.TrySetResult(), probe);
            probe.Started.TrySetResult();

            await probe.Finish.Task;
        }
    }

    /// <summary>Says it ran and succeeds.</summary>
    private sealed class CompletingHandler(Probe probe) : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            probe.Ran.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }
}
