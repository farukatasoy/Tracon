using System.Text.Json;

namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// Two in-memory stores whose write was two steps that another caller could land between: a rule added twice, a checkpoint
/// added to a list a delete had just taken away.
/// </summary>
/// <remarks>
/// <para>
/// Each case puts two or more dedicated threads on a <see cref="Barrier"/> and lets them make a fixed number of calls.
/// A window of a few hundred nanoseconds cannot be placed by a test, so the cases repeat the race many times and state
/// what no serial order of the calls can give. A store that makes the two steps one passes every run; a store that does
/// not fails with a probability that grows with the repeats.
/// </para>
/// </remarks>
public sealed class InMemoryStoreWriteRaceTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Two_callers_that_add_the_same_approval_rule_at_once_leave_one_rule()
    {
        const int rounds = 2_000;
        const int callers = 8;

        for (var round = 0; round < rounds; round++)
        {
            var store = new InMemoryToolApprovalRuleStore();
            using var start = new Barrier(callers);

            var adds = Enumerable.Range(0, callers)
                .Select(_ => Dedicated(async () =>
                {
                    start.SignalAndWait();

                    return await store.AddAsync(Rule(Guid.NewGuid()), TestContext.Current.CancellationToken);
                }))
                .ToArray();

            var added = await Task.WhenAll(adds);

            (await store.ListAsync(Tenant, TestContext.Current.CancellationToken)).Count
                .ShouldBe(1, $"round {round}: the callers added an equal rule at once and each found none");
            added.Select(static rule => rule.Id).Distinct().Count()
                .ShouldBe(1, $"round {round}: every caller is told about the one rule that is kept");
        }
    }

    [Fact]
    public async Task A_checkpoint_created_while_its_session_is_deleted_is_kept_or_counted_by_the_delete()
    {
        const int created = 100_000;
        const int batch = 25;
        var store = new InMemoryWorkflowCheckpointStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        var runId = Guid.NewGuid();
        using var start = new Barrier(2);
        var deleteCalls = 0;
        var writing = true;

        var creating = Dedicated(async () =>
        {
            start.SignalAndWait();

            for (var i = 0; i < created; i++)
            {
                await store.CreateAsync(Checkpoint(runId, i), cancellationToken);

                // The store keeps at most 50 checkpoints for a session and drops the oldest. The writer never gets a batch
                // ahead of the deleter, so nothing is dropped and every checkpoint has to be accounted for.
                if (i % batch == batch - 1)
                {
                    var seen = Volatile.Read(ref deleteCalls);

                    SpinWait.SpinUntil(() => Volatile.Read(ref deleteCalls) > seen);
                }
            }

            Volatile.Write(ref writing, false);

            return 0;
        });

        var deleting = Dedicated(async () =>
        {
            start.SignalAndWait();

            var deleted = 0;

            while (Volatile.Read(ref writing))
            {
                deleted += await store.DeleteAsync(Tenant, "session", cancellationToken);
                Interlocked.Increment(ref deleteCalls);
            }

            return deleted;
        });

        await Task.WhenAll(creating, deleting);

        var kept = (await store.ListByRunAsync(Tenant, runId, cancellationToken)).Count;

        (await deleting + kept).ShouldBe(created, "a checkpoint that was created was neither kept nor counted by a delete");
    }

    private static Task<T> Dedicated<T>(Func<Task<T>> work)
        => Task.Factory.StartNew(
            () => work().GetAwaiter().GetResult(),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static ToolApprovalRule Rule(Guid id)
        => new()
        {
            Id = id,
            TenantId = Tenant,
            AgentName = "support",
            ToolName = "lookup",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static WorkflowCheckpointRecord Checkpoint(Guid runId, int number)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            SessionId = "session",
            CheckpointId = $"checkpoint-{number}",
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = JsonSerializer.SerializeToElement(new { step = number }),
        };
}
