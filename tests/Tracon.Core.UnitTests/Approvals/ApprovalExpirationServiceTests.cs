using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Approvals;

/// <summary>
/// What the approval expiry does to the run that asked: it closes it as failed, and it leaves
/// the values the run recorded when it paused.
/// </summary>
/// <remarks>
/// <para>
/// <c>IRunStore.CompleteRunAsync</c> REPLACES the event count, the usage and the cost of the row
/// with what the completion carries. The expiry built its completion from nothing, so a run that
/// had spent tokens before it paused for approval lost its usage, its cost and its event count the
/// moment the approval ran out (found while building the same close for workflow requests).
/// </para>
/// </remarks>
public sealed class ApprovalExpirationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_expired_approval_closes_its_run_failed_and_keeps_what_the_run_recorded()
    {
        var world = new World();
        var runId = await world.PausedRunAsync();

        await world.AskAsync(runId, expiresAt: Now.AddMinutes(-1));
        await world.Service.TickAsync(CancellationToken.None);

        var run = (await world.Runs.GetRunAsync(runId)).ShouldNotBeNull();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe("ApprovalExpired");
        run.CompletedAt.ShouldBe(Now);

        // What the run spent before it paused is still its own.
        run.EventCount.ShouldBe(42);
        run.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(300);
        run.Cost.ShouldNotBeNull().InputCost.ShouldBe(0.5m);
    }

    [Fact]
    public async Task An_approval_that_has_not_expired_leaves_its_run_waiting()
    {
        var world = new World();
        var runId = await world.PausedRunAsync();

        await world.AskAsync(runId, expiresAt: Now.AddMinutes(5));
        await world.Service.TickAsync(CancellationToken.None);

        (await world.Runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.AwaitingApproval);
    }

    [Fact]
    public async Task A_run_that_something_else_already_closed_is_left_as_it_is()
    {
        var world = new World();
        var runId = await world.PausedRunAsync();

        await world.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Canceled,
            CompletedAt = Now.AddMinutes(-10),
            EventCount = 7,
            TenantId = "default",
        });

        await world.AskAsync(runId, expiresAt: Now.AddMinutes(-1));
        await world.Service.TickAsync(CancellationToken.None);

        var run = (await world.Runs.GetRunAsync(runId)).ShouldNotBeNull();

        run.Status.ShouldBe(RunStatus.Canceled);
        run.EventCount.ShouldBe(7);
        run.Error.ShouldBeNull();
    }

    private sealed class World
    {
        public World()
        {
            Runs = new InMemoryRunStore();
            Approvals = new InMemoryPendingApprovalStore();
            Clock = new FixedClock(Now);

            using var provider = new ServiceCollection()
                .AddOptions<TraconApprovalOptions>().Services
                .AddOptions<SingletonExecutionOptions>().Services
                .BuildServiceProvider();

            Service = new ApprovalExpirationService(
                Approvals,
                Runs,
                new InMemorySingletonLeaseStore(Clock),
                provider.GetRequiredService<IOptionsMonitor<TraconApprovalOptions>>(),
                provider.GetRequiredService<IOptionsMonitor<SingletonExecutionOptions>>(),
                new SchemaReadyGate([]),
                Clock);
        }

        public InMemoryRunStore Runs { get; }

        public InMemoryPendingApprovalStore Approvals { get; }

        public FixedClock Clock { get; }

        public ApprovalExpirationService Service { get; }

        /// <summary>A run that spent tokens and then closed as waiting for an approval.</summary>
        public async Task<Guid> PausedRunAsync()
        {
            var runId = Guid.NewGuid();

            await Runs.StartRunAsync(new RunStartInfo
            {
                RunId = runId,
                AgentName = "support",
                StartedAt = Now.AddMinutes(-30),
                TenantId = "default",
            });
            await Runs.CompleteRunAsync(new RunCompletion
            {
                RunId = runId,
                Status = RunStatus.AwaitingApproval,
                CompletedAt = Now.AddMinutes(-29),
                EventCount = 42,
                Usage = new RunUsage { InputTokens = 200, OutputTokens = 100, TotalTokens = 300 },
                Cost = new RunCost { InputCost = 0.5m, OutputCost = 0.25m, Currency = "USD", Source = PricingSource.Catalog },
                TenantId = "default",
            });

            return runId;
        }

        public async Task AskAsync(Guid runId, DateTimeOffset expiresAt)
            => await Approvals.CreateAsync(new PendingApproval
            {
                Id = Guid.NewGuid(),
                TenantId = "default",
                RunId = runId,
                SessionId = "session",
                RequestId = "request",
                ToolName = "cancel_order",
                Status = ApprovalStatus.Pending,
                ExpiresAt = expiresAt,
                CreatedAt = Now.AddMinutes(-20),
            });
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
