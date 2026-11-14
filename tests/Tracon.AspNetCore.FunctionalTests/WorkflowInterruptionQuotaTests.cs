using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow run that an interruption closed has its own share in the quota counter, once.
/// </summary>
/// <remarks>
/// <para>
/// The share of a workflow root (one run and what its function nodes reported beyond tokens) is written when the run ends:
/// by the process that ran it. A process that died never wrote it, and the rows that close such a run (the recovery that
/// continues it and the reconciliation that claims it) wrote nothing, so the counter stayed below what the run had spent.
/// The closers now write it, after the row is closed, from the items the run reported. A closer that crashes between the two
/// steps loses the share; it never writes it twice.
/// </para>
/// <para>
/// The recovery cases play the crash the way <see cref="WorkflowRecoveryBudgetTests"/> does: a worker leases the job, its
/// second step hangs and ignores the run's token, and its lease is not renewed. The assertions come before the probe is
/// disposed, because disposing it lets the hung attempt finish, and that attempt has its own end.
/// </para>
/// </remarks>
public sealed class WorkflowInterruptionQuotaTests
{
    private const string User = "ada";
    private static readonly TimeSpan DeadLease = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task The_run_that_a_recovery_closed_writes_its_run_and_what_it_reported_to_the_counter_and_the_continuation_its_own()
    {
        var probe = new RecoveryProbe { FirstAmount = 0.3m, SecondAmount = 0.2m };
        var quota = new EntryRecordingQuotaStore();

        await using var host = await StartWithQuotaAsync(probe, quota);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var headId = await EnqueueAsync(host);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "survivor");

        var continuation = (await WorkflowRunsAsync(host)).Single(run => run.Id != headId);

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);

        var interrupted = EntriesOf(quota, headId);

        var entry = interrupted.ShouldHaveSingleItem("the run the recovery closed has one entry in the counter");

        entry.Runs.ShouldBe(1);
        entry.Tokens.ShouldBe(0);
        entry.Cost.ShouldBe(0.5m, "the two steps reported 0.3 and 0.2 before the process died");
        entry.AgentName.ShouldBe(Recoverable);
        entry.UserId.ShouldBe(User);

        var next = EntriesOf(quota, continuation.Id).ShouldHaveSingleItem();

        next.Runs.ShouldBe(1);
        next.Cost.ShouldBe(0.2m, "the continuation writes its own share, as it did before");
    }

    [Fact]
    public async Task A_process_that_comes_back_after_the_recovery_closed_its_run_does_not_write_the_run_again()
    {
        var probe = new RecoveryProbe { FirstAmount = 0.3m, SecondAmount = 0.2m };
        var quota = new EntryRecordingQuotaStore();

        await using var host = await StartWithQuotaAsync(probe, quota);
        var lifetime = probe;
        await SaveRecoverableAsync(host);

        var headId = await EnqueueAsync(host);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "survivor");

        EntriesOf(quota, headId).Count.ShouldBe(1, "the recovery wrote the run");

        // The old process was not dead, only silent: it goes on and ends its run, which closes the row a second time.
        await lifetime.DisposeAsync();

        var after = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(headId)).ShouldNotBeNull();

        after.Status.ShouldNotBe(RunStatus.Running, "the old attempt ended and closed its row, the case this test is about");
        EntriesOf(quota, headId).Count.ShouldBe(1, "a run is counted once, by whoever closed it first");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_workflow_root_that_the_reconciliation_claimed_writes_its_run_and_what_it_reported_with_its_user(bool sqlite)
    {
        var quota = new EntryRecordingQuotaStore();
        using var database = sqlite ? new TempSqliteDatabase("interruption-quota") : null;
        await using var host = await StartReconcilingAsync(quota, database);

        var runId = await SeedOrphanAsync(host, kind: RunKind.Workflow, parent: null, events: true, amounts: [0.3m, 0.2m]);

        await WaitForReconciliationPassAsync(host, runId);

        var entry = EntriesOf(quota, runId).ShouldHaveSingleItem();

        entry.Runs.ShouldBe(1);
        entry.Tokens.ShouldBe(0);
        entry.Cost.ShouldBe(0.5m);
        entry.AgentName.ShouldBe(Orphan);
        entry.UserId.ShouldBe(User, "the claim of a SQL store does not return the user: the closer reads the row");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_row_that_the_reconciliation_claimed_is_counted_when_it_is_a_workflow_root_that_ran_and_not_otherwise(bool sqlite)
    {
        var quota = new EntryRecordingQuotaStore();
        using var database = sqlite ? new TempSqliteDatabase("interruption-quota-scope") : null;
        await using var host = await StartReconcilingAsync(quota, database);

        var parent = await SeedOrphanAsync(host, kind: RunKind.Workflow, parent: null, events: true, amounts: []);
        var step = await SeedOrphanAsync(host, kind: RunKind.Workflow, parent: parent, events: true, amounts: [0.4m]);
        var agent = await SeedOrphanAsync(host, kind: RunKind.Agent, parent: null, events: true, amounts: [0.4m]);
        var neverRan = await SeedOrphanAsync(host, kind: RunKind.Workflow, parent: null, events: false, amounts: [0.4m]);

        await WaitForReconciliationPassAsync(host, neverRan);

        quota.Entries.Select(static entry => entry.RunId).ShouldBe(
            [parent.ToString()],
            "a root that ran writes its run even with nothing reported; a step of a workflow, an agent run and a row that never wrote an event write nothing");
        quota.Entries.Single().Cost.ShouldBeNull();

        _ = (step, agent);
    }

    private const string Orphan = "doomed-workflow";

    private static List<QuotaConsumption> EntriesOf(EntryRecordingQuotaStore quota, Guid runId)
        => [.. quota.Entries.Where(entry => string.Equals(entry.RunId, runId.ToString(), StringComparison.Ordinal))];

    private static Task<TraconTestHost> StartWithQuotaAsync(RecoveryProbe probe, EntryRecordingQuotaStore quota)
        => StartAsync(
            probe,
            runWorker: false,
            configureServices: services =>
            {
                services.Configure<TraconOptions>(static options => options.Pricing.Currency = "USD");
                services.AddSingleton<IQuotaStore>(quota);
                services.AddSingleton<IRunAttributionContext>(new SettableAttribution { UserId = User });
            });

    private static Task<TraconTestHost> StartReconcilingAsync(EntryRecordingQuotaStore quota, TempSqliteDatabase? database)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                if (database is not null)
                {
                    builder.UseSqlite(database.ConnectionString);
                }
            },
            configureServices: services =>
            {
                services.Configure<TraconOptions>(static options => options.Pricing.Currency = "USD");
                services.Configure<RunReconciliationOptions>(static options =>
                {
                    options.Enabled = true;
                    options.ScanInterval = TimeSpan.FromMilliseconds(30);
                    options.OrphanThreshold = TimeSpan.FromMinutes(5);
                    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                });
                services.AddSingleton<IQuotaStore>(quota);
            });

    private static async Task<Guid> EnqueueAsync(TraconTestHost host)
    {
        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });

        return await AcceptedRunIdAsync(accepted);
    }

    /// <summary>Writes a run that a process left behind: started ten minutes ago, no heartbeat, still marked running.</summary>
    private static async Task<Guid> SeedOrphanAsync(TraconTestHost host, RunKind kind, Guid? parent, bool events, decimal[] amounts)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = Orphan,
            WorkflowName = kind == RunKind.Workflow ? Orphan : null,
            Kind = kind,
            ParentRunId = parent,
            RootRunId = parent,
            Depth = parent is null ? 0 : 1,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenant,
            UserId = User,
        });

        if (events)
        {
            await runs.AppendEventAsync(new RunEvent
            {
                RunId = runId,
                Sequence = 0,
                Type = RunEventType.RunStarted,
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
                TenantId = tenant,
            });
        }

        foreach (var amount in amounts)
        {
            await runs.RecordNonTokenUsageAsync(new NonTokenUsageRecord
            {
                Id = TraconId.NewId(),
                RunId = runId,
                NodeName = "synthesize",
                Usage = RecoveryProbe.Spent(amount),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-9),
                TenantId = tenant,
            });
        }

        return runId;
    }

    /// <summary>
    /// Waits until a reconciliation pass has closed the run and a LATER pass has closed a sentinel: passes run one at a time,
    /// and a pass writes the quota of the runs it claimed before it ends, so the earlier pass is over.
    /// </summary>
    private static async Task WaitForReconciliationPassAsync(TraconTestHost host, Guid runId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await WaitUntil.TrueAsync(
            async () =>
            {
                using (AmbientTenantScope.Begin(tenant))
                {
                    return (await runs.GetRunAsync(runId))?.Status == RunStatus.Failed;
                }
            },
            $"the reconciler to close run {runId}");

        var sentinelId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = sentinelId,
            AgentName = "sentinel",
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenant,
        });

        await WaitUntil.TrueAsync(
            async () => (await runs.GetRunAsync(sentinelId))?.Status == RunStatus.Failed,
            "a later reconciliation pass to close the sentinel run");
    }
}
