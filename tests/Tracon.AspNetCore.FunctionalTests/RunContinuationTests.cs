using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// End-to-end tests for automatic interrupted-run continuation (Phase 87).
/// </summary>
/// <remarks>
/// A real process crash cannot be simulated in-process; a "crashed" run is
/// SEEDED directly (a <c>Running</c> row with a stale heartbeat, a recorded
/// input, and a recorded tool call) — the same technique
/// <c>RunReconciliationTests</c> uses for plain orphan reconciliation. The
/// scenario is otherwise real: the job queue, the worker, and the compiled
/// agent all run for real.
/// </remarks>
public sealed class RunContinuationTests
{
    private const string AgentName = "continuable";
    private const string ModelId = "cont-1";
    private const string SessionId = "session-under-test";

    [Fact]
    public async Task Continuation_replays_the_completed_call_and_runs_the_new_one_live()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var sourceRunId = await SeedCrashedRunAsync(host);

        await host.Services.GetRequiredService<IRunStore>().RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = sourceRunId,
            ToolName = "get_order_status",
            Arguments = "orderId=ORD-7",
            Result = "RECORDED: in transit",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);

        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.ContinuedFromRunId.ShouldBe(sourceRunId);
        continuation.Status.ShouldBe(RunStatus.Completed);

        // 🚨 The FIRST call (ORD-7) has a recorded result; a live re-run would
        // ALSO increment this counter -- it must not. Only the SECOND call
        // (ORD-8), which was never recorded because it happened after the
        // simulated crash, actually runs.
        ContinuationProbeTools.Calls.ShouldBe(1);
        ContinuationProbeTools.LastLiveOrderId.ShouldBe("ORD-8");

        var events = new List<RunEvent>();

        await foreach (var runEvent in host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(continuationRunId))
        {
            events.Add(runEvent);
        }

        // The recorded (replayed) result must reach the model UNCHANGED —
        // this is the phase's first (and most likely) failure mode: if the
        // mismatch policy were not reversed for continuation, this run would
        // have stopped with a mismatch at the FIRST call instead of reaching Completed.
        events.Any(e => e.Type == RunEventType.ToolInvoked &&
            string.Equals(e.ToolName, "get_order_status", StringComparison.Ordinal))
            .ShouldBeTrue();

        // Source run is untouched (K-014): still Failed, no lineage pointing forward.
        var source = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(sourceRunId);
        source.ShouldNotBeNull();
        source.Status.ShouldBe(RunStatus.Failed);
    }

    [Fact]
    public async Task Disabled_by_default_orphaned_run_stays_Failed_with_no_continuation()
    {
        await using var host = await StartHostAsync(static o => o.Enabled = false);

        var sourceRunId = await SeedCrashedRunAsync(host);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var source = await runs.GetRunAsync(sourceRunId);

        source.ShouldNotBeNull();
        source.Status.ShouldBe(RunStatus.Failed);

        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_destructive_tool_call_blocks_continuation_and_records_why()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var sourceRunId = await SeedCrashedRunAsync(host);

        await host.Services.GetRequiredService<IRunStore>().RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = sourceRunId,
            ToolName = "delete_order",
            Arguments = "orderId=ORD-7",
            Result = "deleted",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var runs = host.Services.GetRequiredService<IRunStore>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var blocked = false;

        while (DateTime.UtcNow < deadline && !blocked)
        {
            await foreach (var runEvent in runs.ReadEventsAsync(sourceRunId))
            {
                if (runEvent.Type == RunEventType.RunContinuationBlocked)
                {
                    blocked = true;
                    runEvent.Text.ShouldNotBeNull().ShouldContain("delete_order");
                }
            }

            if (!blocked)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }

        blocked.ShouldBeTrue("expected a RunContinuationBlocked event");

        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.Count.ShouldBe(1);
        ContinuationProbeTools.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_tool_declaring_SafeToRepeat_allows_continuation_despite_a_destructive_effect()
    {
        ContinuationProbeTools.Reset();

        await using var host = await StartHostAsync(o => o.Enabled = true);

        var sourceRunId = await SeedCrashedRunAsync(host);

        await host.Services.GetRequiredService<IRunStore>().RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = sourceRunId,
            ToolName = "idempotent_delete_order",
            Arguments = "orderId=ORD-7",
            Result = "deleted",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var continuationRunId = await WaitForContinuationAsync(host, sourceRunId);
        var continuation = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(continuationRunId);

        continuation.ShouldNotBeNull();
        continuation.ContinuedFromRunId.ShouldBe(sourceRunId);
    }

    [Fact]
    public async Task A_sessionless_run_is_never_continued()
    {
        await using var host = await StartHostAsync(o => o.Enabled = true);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),

            // No SessionId: nothing to continue.
        });

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        (await runs.GetRunAsync(runId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);

        var jobs = host.Services.GetRequiredService<IJobStore>();
        var jobsForRun = await jobs.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.RunContinuation });
        jobsForRun.ShouldBeEmpty();
    }

    [Fact]
    public async Task MaxAttempts_stops_a_second_continuation_in_the_same_chain()
    {
        await using var host = await StartHostAsync(o =>
        {
            o.Enabled = true;
            o.MaxAttempts = 1;
        });

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();

        // The chain's original run: already settled, not orphaned now.
        var ancestorId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = ancestorId,
            AgentName = AgentName,
            Status = RunStatus.Failed,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-1),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
        });

        // A run that is ITSELF the chain's first continuation, now ALSO
        // crashed. With MaxAttempts = 1, this must NOT be continued again.
        var sourceRunId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = sourceRunId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
            ContinuedFromRunId = ancestorId,
        });

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        (await runs.GetRunAsync(sourceRunId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);

        // Exactly the two seeded rows -- no second continuation was opened.
        var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
        all.Count.ShouldBe(2);
    }

    private static async Task<TraconTestHost> StartHostAsync(Action<TraconRunContinuationOptions> configureContinuation)
        => await TraconTestHost.StartAsync(
            ConfigureAgent,
            configureServices: services =>
            {
                services.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                services.Configure<RunReconciliationOptions>(static options =>
                {
                    options.Enabled = true;
                    options.ScanInterval = TimeSpan.FromMilliseconds(30);
                    options.OrphanThreshold = TimeSpan.FromMinutes(5);
                    options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                });
                services.Configure(configureContinuation);
            });

    private static void ConfigureAgent(ITraconBuilder builder)
    {
        builder
            .AddModelProvider(new FakeModelProvider("cont-model")
                .CallsTool("get_order_status", new { orderId = "ORD-7" })
                .CallsTool("get_order_status", new { orderId = "ORD-8" })
                .EchoesLastToolResult())
            .AddToolsFrom(typeof(ContinuationProbeTools));

        var definitions = builder.Services;

        definitions.AddSingleton<IStartupSeed>(new StartupSeed(new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = new ModelBinding { Provider = "cont-model", Model = ModelId },
            ToolNames = ["get_order_status", "delete_order", "idempotent_delete_order"],
        }));
    }

    private static async ValueTask<Guid> SeedCrashedRunAsync(TraconTestHost host)
    {
        foreach (var seed in host.Services.GetServices<IStartupSeed>())
        {
            await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(seed.Definition);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            TenantId = tenants.TenantId,
            SessionId = SessionId,
            ModelId = ModelId,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenants.TenantId,
            Messages = [new ChatMessage(ChatRole.User, "where is ORD-7, and check ORD-8 too")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }

    private static async Task<Guid> WaitForContinuationAsync(TraconTestHost host, Guid sourceRunId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var all = await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false });
            var continuation = all.FirstOrDefault(record => record.Id != sourceRunId);

            if (continuation is { Status: RunStatus.Completed or RunStatus.Failed })
            {
                return continuation.Id;
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No continuation of run '{sourceRunId}' settled within the deadline.");
    }

    /// <summary>Marker interface carrying definitions to be saved during startup.</summary>
    private interface IStartupSeed
    {
        AgentDefinition Definition { get; }
    }

    private sealed record StartupSeed(AgentDefinition Definition) : IStartupSeed;
}

/// <summary>
/// The tools for the continuation tests. Measures whether the body ACTUALLY ran.
/// </summary>
internal static class ContinuationProbeTools
{
    private static int _calls;
    private static string? _lastLiveOrderId;

    /// <summary>How many times a tool body ran (as opposed to being replayed).</summary>
    public static int Calls => Volatile.Read(ref _calls);

    /// <summary>The order id of the most recent LIVE call.</summary>
    public static string? LastLiveOrderId => Volatile.Read(ref _lastLiveOrderId);

    /// <summary>Resets the counters.</summary>
    public static void Reset()
    {
        Volatile.Write(ref _calls, 0);
        Volatile.Write(ref _lastLiveOrderId, null);
    }

    /// <summary>Returns an order's status.</summary>
    [TraconTool("get_order_status", "Returns an order's shipping status.")]
    public static string GetOrderStatus(string orderId)
    {
        Interlocked.Increment(ref _calls);
        Volatile.Write(ref _lastLiveOrderId, orderId);

        return $"{orderId}: RAN LIVE";
    }

    /// <summary>Deletes an order. Irreversible.</summary>
    [TraconTool("delete_order", "Deletes an order.", Effect = ToolEffect.Destructive)]
    public static string DeleteOrder(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId} deleted.";
    }

    /// <summary>
    /// Deletes an order. Irreversible, but carries its own idempotency key,
    /// so the tool's author declares it safe to repeat.
    /// </summary>
    [TraconTool("idempotent_delete_order", "Deletes an order (idempotent).", Effect = ToolEffect.Destructive, SafeToRepeat = true)]
    public static string IdempotentDeleteOrder(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId} deleted (idempotent).";
    }
}
