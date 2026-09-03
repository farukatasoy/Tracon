using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgentPrism.Samples.CustomJobHandler.Tests;

/// <summary>
/// Verifies the sample against the PACKED package, at the two boundaries a
/// unit test cannot reach: dependency injection, and a real running worker.
/// </summary>
/// <remarks>
/// 🚨 Phase 137 opened with this class red. Until then it only asserted that
/// the handler was in the container and declared <c>JobKind.AgentBatch</c> —
/// both true, and both irrelevant: the worker picked handlers by enum equality
/// in registration order, the built-in <c>AgentBatchJobHandler</c> was always
/// registered first, and so this sample — AgentPrism's own documented example
/// of the extension point — never ran a single job in a real host.
/// </remarks>
public sealed class NightlyReportJobHandlerRegistrationTests
{
    [Fact]
    public void Registration_is_duplicate_safe()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddNightlyReportJobHandler()
            .AddNightlyReportJobHandler();

        using var provider = services.BuildServiceProvider();

        // TryAddScoped keeps ONE registration of the type; the duplicate
        // handler-key registration is what would stop the host, so a repeated
        // call of the same extension method must not produce one.
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<NightlyReportJobHandler>().ShouldNotBeNull();
    }

    [Fact]
    public async Task The_sample_handler_runs_a_queued_job_in_a_real_worker()
    {
        using var host = BuildHost(builder => builder.AddNightlyReportJobHandler());

        await host.StartAsync(TestContext.Current.CancellationToken);

        var job = await EnqueueAsync(host, TestContext.Current.CancellationToken);
        var finished = await WaitForTerminalAsync(host, job.Id, TestContext.Current.CancellationToken);

        finished.Status.ShouldBe(JobStatus.Completed);
        finished.DoneItems.ShouldBe(2);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Registration_order_does_not_change_the_outcome()
    {
        // The SAME assertion as above, with the consumer's registration moved
        // BEFORE AddAgentPrism(). Under the old enum dispatch these two orders
        // gave opposite results; under key dispatch they must not differ.
        using var host = BuildHost(registerFirst: true);

        await host.StartAsync(TestContext.Current.CancellationToken);

        var job = await EnqueueAsync(host, TestContext.Current.CancellationToken);
        var finished = await WaitForTerminalAsync(host, job.Id, TestContext.Current.CancellationToken);

        finished.Status.ShouldBe(JobStatus.Completed);
        finished.DoneItems.ShouldBe(2);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_job_whose_key_nobody_registered_fails_without_leaking_the_key()
    {
        using var host = BuildHost(configure: null);

        await host.StartAsync(TestContext.Current.CancellationToken);

        var store = host.Services.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;

        var job = await store.EnqueueAsync(
            new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                HandlerKey = "samples.no-such-handler",
                TargetName = "report",
                Status = JobStatus.Pending,
                ScheduledFor = now,
                CreatedAt = now,
            },
            ["a"],
            TestContext.Current.CancellationToken);

        var finished = await WaitForTerminalAsync(host, job.Id, TestContext.Current.CancellationToken);

        finished.Status.ShouldBe(JobStatus.Failed);
        finished.ErrorMessage.ShouldNotBeNull();
        finished.ErrorMessage.ShouldContain(JobErrorCodes.UnknownHandlerKey);

        // The raw key may have come from an untrusted source and this field is
        // read back over HTTP; it belongs in the log, not in the record.
        finished.ErrorMessage.ShouldNotContain("samples.no-such-handler");

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void A_consumer_cannot_register_inside_the_reserved_namespace()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentException>(
            () => services.AddJobHandler<NightlyReportJobHandler>(JobHandlerKeys.Retention));

        exception.Message.ShouldContain(JobHandlerKeys.Retention);
    }

    [Fact]
    public async Task Two_handlers_sharing_one_key_stop_the_host()
    {
        using var host = BuildHost(builder =>
        {
            builder.AddNightlyReportJobHandler();
            builder.Services.AddJobHandler<SecondHandler>(
                NightlyReportJobHandlerRegistrationExtensions.HandlerKey);
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(NightlyReportJobHandlerRegistrationExtensions.HandlerKey);
    }

    private static IHost BuildHost(
        Action<IAgentPrismBuilder>? configure = null,
        bool registerFirst = false)
        => new HostBuilder()
            .ConfigureServices(services =>
            {
                if (registerFirst)
                {
                    services.AddJobHandler<NightlyReportJobHandler>(
                        NightlyReportJobHandlerRegistrationExtensions.HandlerKey);
                }

                var builder = services.AddAgentPrism();

                if (!registerFirst)
                {
                    configure?.Invoke(builder);
                }

                services.UseScheduling(static options =>
                {
                    options.RunWorker = true;
                    options.PollInterval = TimeSpan.FromMilliseconds(25);
                });
            })
            .Build();

    private static async ValueTask<JobRecord> EnqueueAsync(IHost host, CancellationToken cancellationToken)
        => await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = "default",
                HandlerKey = NightlyReportJobHandlerRegistrationExtensions.HandlerKey,
                TargetName = "nightly-report",
                Items = ["customer-1", "customer-2"],
            },
            cancellationToken);

    private static async Task<JobRecord> WaitForTerminalAsync(
        IHost host,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var store = host.Services.GetRequiredService<IJobStore>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var current = await store.GetAsync("default", jobId, cancellationToken);

            if (current is { Status: JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled })
            {
                return current;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        throw new TimeoutException($"Job {jobId} did not reach a terminal status.");
    }

    /// <summary>A second handler, only ever registered to provoke a key clash.</summary>
    private sealed class SecondHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
            => default;
    }
}
