using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Retention;

/// <summary>
/// <see cref="RetentionFanOutJobHandler"/> queues one retention job for each tenant a source names, in that tenant's
/// own name, and refuses to run for any tenant but the default one.
/// </summary>
/// <remarks>
/// The queue is the real <see cref="InMemoryJobStore"/> behind the real <see cref="JobDispatcher"/>: what a test reads
/// back is what the worker would lease.
/// </remarks>
public sealed class RetentionFanOutJobHandlerTests
{
    private const string Host = "host";

    [Fact]
    public async Task Queues_one_retention_job_for_each_tenant_in_that_tenants_own_name()
    {
        var rig = Rig(["acme", "tenant-b", "tenant-c"]);

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        var jobs = await rig.QueuedAsync();
        jobs.Select(static job => job.TenantId).ShouldBe(["acme", "tenant-b", "tenant-c"], ignoreOrder: true);
        jobs.ShouldAllBe(static job => Is(job.HandlerKey, JobHandlerKeys.Retention) && Is(job.TargetName, "*"));
        (await rig.Store.QueryAsync(new JobQuery { TenantId = Host }, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("the fan-out queues nothing for its own tenant unless the source names it");
    }

    [Fact]
    public async Task Each_queued_job_carries_one_item_so_the_retention_handler_can_report_it()
    {
        var rig = Rig(["acme"]);

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        var job = (await rig.QueuedAsync()).ShouldHaveSingleItem();
        var items = await rig.Store.ListItemsAsync(job.Id, TestContext.Current.CancellationToken);
        items.ShouldHaveSingleItem().Input.ShouldBe("*");
    }

    [Fact]
    public async Task Lower_cases_a_tenant_and_queues_it_once_however_often_the_source_names_it()
    {
        var rig = Rig(["Acme", "acme", "ACME", "tenant-b"]);

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).Select(static job => job.TenantId).ShouldBe(["acme", "tenant-b"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a tenant")]
    [InlineData("tenant/one")]
    public async Task A_name_that_is_not_a_tenant_identifier_is_skipped_and_the_others_still_run(string invalid)
    {
        var rig = Rig([invalid, "acme"]);

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).ShouldHaveSingleItem().TenantId.ShouldBe("acme");
    }

    [Fact]
    public async Task A_source_that_names_nobody_queues_nothing_and_the_job_completes()
    {
        var rig = Rig([]);
        var reported = new List<JobItemResult>();

        await rig.Handler.ExecuteAsync(Context(reported: reported), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).ShouldBeEmpty();
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    [Fact]
    public async Task The_target_of_the_fan_out_is_the_target_of_each_queued_job()
    {
        var rig = Rig(["acme", "tenant-b"]);

        await rig.Handler.ExecuteAsync(Context(target: RetentionTargets.RunEvents), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).ShouldAllBe(static job => Is(job.TargetName, RetentionTargets.RunEvents));
    }

    [Fact]
    public async Task An_empty_target_means_every_target()
    {
        var rig = Rig(["acme"]);

        await rig.Handler.ExecuteAsync(Context(target: ""), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).ShouldHaveSingleItem().TargetName.ShouldBe("*");
    }

    [Fact]
    public async Task An_unknown_target_fails_before_anything_is_queued_or_read()
    {
        var rig = Rig(["acme"]);

        await Should.ThrowAsync<ArgumentException>(async () =>
            await rig.Handler.ExecuteAsync(Context(target: "not_a_real_table"), TestContext.Current.CancellationToken));

        (await rig.QueuedAsync()).ShouldBeEmpty();
        rig.Source.Reads.ShouldBe(0);
    }

    /// <summary>
    /// The fan-out reaches every tenant's retention. A job of any other tenant is refused without reading the source,
    /// and the refusal is final: the same job cannot become the default tenant's by being run again.
    /// </summary>
    [Fact]
    public async Task A_job_of_another_tenant_is_refused_for_good_and_queues_nothing()
    {
        var rig = Rig(["acme", "tenant-b"]);

        var refusal = await Should.ThrowAsync<NonRetryableJobException>(async () =>
            await rig.Handler.ExecuteAsync(Context(tenant: "acme"), TestContext.Current.CancellationToken));

        refusal.Code.ShouldBe(TraconErrorTypes.JobHostOnlyHandlerRefused);
        refusal.Message.ShouldStartWith(TraconErrorTypes.JobHostOnlyHandlerRefused);
        refusal.Message.ShouldNotContain("acme", Case.Insensitive);
        (await rig.QueuedAsync()).ShouldBeEmpty();
        rig.Source.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData("HOST")]
    [InlineData("Host")]
    public async Task The_default_tenant_is_compared_by_its_canonical_form(string configured)
    {
        var rig = Rig(["acme"], defaultTenant: configured);

        await rig.Handler.ExecuteAsync(Context(tenant: Host), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_tenant_whose_retention_job_is_still_open_for_the_target_is_left_alone()
    {
        var rig = Rig(["acme", "tenant-b"]);
        await rig.Dispatcher.EnqueueAsync(RetentionRequest("acme", "*"), TestContext.Current.CancellationToken);

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        var jobs = await rig.QueuedAsync();
        jobs.Count(static job => Is(job.TenantId, "acme")).ShouldBe(1, "the open job is the only one acme has");
        jobs.Count(static job => Is(job.TenantId, "tenant-b")).ShouldBe(1);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task A_finished_retention_job_does_not_stop_the_next_one(JobStatus finished)
    {
        var rig = Rig(["acme"]);
        var old = await rig.Dispatcher.EnqueueAsync(RetentionRequest("acme", "*"), TestContext.Current.CancellationToken);
        await rig.FinishAsync(old, finished);

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).Count(static job => Is(job.TenantId, "acme")).ShouldBe(2);
    }

    [Fact]
    public async Task An_open_job_for_another_target_does_not_stop_this_one()
    {
        var rig = Rig(["acme"]);
        await rig.Dispatcher.EnqueueAsync(RetentionRequest("acme", RetentionTargets.Traces), TestContext.Current.CancellationToken);

        await rig.Handler.ExecuteAsync(Context(target: RetentionTargets.RunEvents), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).Count(static job => Is(job.TenantId, "acme")).ShouldBe(2);
    }

    [Fact]
    public async Task A_tenant_the_queue_refuses_does_not_stop_the_others_and_the_failure_is_raised_at_the_end()
    {
        var rig = Rig(["acme", "tenant-b", "tenant-c"], failFor: ["tenant-b"]);
        var reported = new List<JobItemResult>();

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await rig.Handler.ExecuteAsync(Context(reported: reported), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("tenant-b");
        (await rig.QueuedAsync()).Select(static job => job.TenantId).ShouldBe(["acme", "tenant-c"], ignoreOrder: true);
        reported.ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Failed);
    }

    [Fact]
    public async Task Several_tenants_the_queue_refuses_are_raised_together()
    {
        var rig = Rig(["acme", "tenant-b", "tenant-c"], failFor: ["acme", "tenant-c"]);

        var failure = await Should.ThrowAsync<AggregateException>(async () =>
            await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken));

        failure.InnerExceptions.Count.ShouldBe(2);
        (await rig.QueuedAsync()).ShouldHaveSingleItem().TenantId.ShouldBe("tenant-b");
    }

    [Fact]
    public async Task A_source_that_throws_fails_the_job_and_keeps_the_jobs_it_queued_before()
    {
        var rig = Rig(["acme", "tenant-b"], thenThrow: new InvalidOperationException("tenant directory is down"));

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken));

        failure.Message.ShouldBe("tenant directory is down");
        (await rig.QueuedAsync()).Select(static job => job.TenantId).ShouldBe(["acme", "tenant-b"], ignoreOrder: true);
    }

    [Fact]
    public async Task Stopping_ends_the_loop_and_the_jobs_already_queued_stay_queued()
    {
        using var stop = new CancellationTokenSource();
        var rig = Rig(["acme", "tenant-b", "tenant-c", "tenant-d"], onNamed: (index, _) =>
        {
            if (index == 1)
            {
                stop.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await rig.Handler.ExecuteAsync(Context(), stop.Token));

        var queued = (await rig.QueuedAsync()).Select(static job => job.TenantId).ToList();
        queued.ShouldContain(static tenant => Is(tenant, "acme"));
        queued.ShouldNotContain(static tenant => Is(tenant, "tenant-d"), "the loop stops at the next name once the token is cancelled");
    }

    /// <summary>
    /// A store error after the work must not make the repeat queue the work again: the repeat finds each tenant's
    /// job waiting and leaves it.
    /// </summary>
    [Fact]
    public async Task A_report_that_fails_ends_the_attempt_and_the_repeat_queues_nothing_new()
    {
        var rig = Rig(["acme", "tenant-b"]);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await rig.Handler.ExecuteAsync(
                Context(reportItem: static (_, _) => throw new InvalidOperationException("store is down")),
                TestContext.Current.CancellationToken));

        var afterFirst = (await rig.QueuedAsync()).Count;

        await rig.Handler.ExecuteAsync(Context(), TestContext.Current.CancellationToken);

        afterFirst.ShouldBe(2);
        (await rig.QueuedAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_job_without_items_runs_and_reports_nothing()
    {
        var rig = Rig(["acme"]);

        await rig.Handler.ExecuteAsync(Context(items: []), TestContext.Current.CancellationToken);

        (await rig.QueuedAsync()).ShouldHaveSingleItem();
    }

    private static Fixture Rig(
        IReadOnlyList<string> tenants,
        string defaultTenant = Host,
        IReadOnlyList<string>? failFor = null,
        Exception? thenThrow = null,
        Action<int, string>? onNamed = null)
    {
        var store = new InMemoryJobStore();
        var host = TestJobHandlerHost.For((JobHandlerKeys.Retention, new NoopHandler()));
        var dispatcher = new JobDispatcher(store, host.Registry, new StaticOptionsMonitor<TraconSchedulingOptions>(new TraconSchedulingOptions()));
        var source = new ScriptedSource(tenants, thenThrow, onNamed);
        var refusing = new RefusingDispatcher(dispatcher, failFor ?? []);

        var handler = new RetentionFanOutJobHandler(
            source,
            refusing,
            store,
            new StaticOptionsMonitor<TraconOptions>(new TraconOptions { DefaultTenantId = defaultTenant }));

        return new Fixture(handler, store, dispatcher, source, host);
    }

    private static bool Is(string actual, string expected) => string.Equals(actual, expected, StringComparison.Ordinal);

    private static JobRequest RetentionRequest(string tenant, string target)
        => new() { TenantId = tenant, HandlerKey = JobHandlerKeys.Retention, TargetName = target, Items = [target] };

    private static JobContext Context(
        string tenant = Host,
        string target = "*",
        List<JobItemResult>? reported = null,
        IReadOnlyList<JobItemRecord>? items = null,
        Func<JobItemResult, CancellationToken, ValueTask>? reportItem = null)
    {
        reported ??= [];
        var jobId = Guid.NewGuid();

        return new JobContext
        {
            Job = new JobRecord
            {
                Id = jobId,
                TenantId = tenant,
                HandlerKey = JobHandlerKeys.RetentionFanOut,
                TargetName = target,
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            Items = items ?? [new JobItemRecord { Id = Guid.NewGuid(), JobId = jobId, Seq = 0, Input = target, Status = JobItemStatus.Pending }],
            ReportItemAsync = reportItem ?? ((result, _) =>
            {
                reported.Add(result);

                return default;
            }),
            IsCancelledAsync = static _ => new ValueTask<bool>(false),
        };
    }

    private sealed record Fixture(
        RetentionFanOutJobHandler Handler,
        InMemoryJobStore Store,
        JobDispatcher Dispatcher,
        ScriptedSource Source,
        TestJobHandlerHost Host) : IDisposable
    {
        public async ValueTask<IReadOnlyList<JobRecord>> QueuedAsync()
            => await Store.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention, Take = 200 }, TestContext.Current.CancellationToken);

        public async ValueTask FinishAsync(JobRecord job, JobStatus status)
        {
            if (status == JobStatus.Cancelled)
            {
                await Store.CancelAsync(job.TenantId, job.Id, TestContext.Current.CancellationToken);

                return;
            }

            // A job is finished by the worker that holds its lease.
            var leased = await Store.LeaseAsync(
                new JobLeaseRequest { Owner = "test-worker", LeaseDuration = TimeSpan.FromMinutes(5) },
                TestContext.Current.CancellationToken);

            leased.ShouldNotBeNull();
            leased.Id.ShouldBe(job.Id);

            var completed = await Store.CompleteAsync(
                new JobLease { JobId = leased.Id, Owner = "test-worker", Attempt = leased.Attempt },
                new JobCompletion { Status = status, CompletedAt = DateTimeOffset.UtcNow },
                TestContext.Current.CancellationToken);

            completed.ShouldBeTrue();
        }

        public void Dispose() => Host.Dispose();
    }

    /// <summary>Names the tenants it was given in order, then throws if told to.</summary>
    private sealed class ScriptedSource(IReadOnlyList<string> tenants, Exception? thenThrow, Action<int, string>? onNamed) : IRetentionTenantSource
    {
        public int Reads { get; private set; }

        public async IAsyncEnumerable<string> ListTenantsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Reads++;

            for (var index = 0; index < tenants.Count; index++)
            {
                onNamed?.Invoke(index, tenants[index]);

                yield return tenants[index];

                await Task.Yield();
            }

            if (thenThrow is not null)
            {
                throw thenThrow;
            }
        }
    }

    /// <summary>Refuses to queue for the given tenants, the way a queue that is down for one tenant would.</summary>
    private sealed class RefusingDispatcher(IJobDispatcher inner, IReadOnlyList<string> refused) : IJobDispatcher
    {
        public ValueTask<JobRecord> EnqueueAsync(JobRequest request, CancellationToken cancellationToken = default)
            => refused.Contains(request.TenantId, StringComparer.Ordinal)
                ? throw new InvalidOperationException($"The queue refused tenant {request.TenantId}.")
                : inner.EnqueueAsync(request, cancellationToken);
    }

    private sealed class NoopHandler : IJobHandler
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
