using System.Text.Json;

namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// In-memory stores whose write was two steps that another caller could land between: a rule added twice, a checkpoint
/// added to a list a delete had just taken away. The experiment store is here for the same reason in the other direction:
/// its writes are one step each, under one lock, and the three pairs of writes that can overlap are held to it.
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

    [Fact]
    public async Task Two_experiments_of_one_agent_started_at_once_leave_one_of_them_running()
    {
        const int rounds = 2_000;
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var round = 0; round < rounds; round++)
        {
            var store = new InMemoryExperimentStore();
            await store.SaveAsync(Draft("first"), cancellationToken);
            await store.SaveAsync(Draft("second"), cancellationToken);
            using var start = new Barrier(2);

            var started = await Task.WhenAll(
                Dedicated(() => StartedAsync(store, "first", start, cancellationToken)),
                Dedicated(() => StartedAsync(store, "second", start, cancellationToken)));

            started.Count(static won => won).ShouldBe(1, $"round {round}: both starts saw no running experiment of the agent");
            (await store.ListAsync(Tenant, cancellationToken)).Count(static experiment => experiment.Status == ExperimentStatus.Running)
                .ShouldBe(1, $"round {round}: two experiments of one agent are running");
        }
    }

    [Fact]
    public async Task A_draft_saved_while_it_is_started_stays_started()
    {
        const int rounds = 2_000;
        var cancellationToken = TestContext.Current.CancellationToken;

        for (var round = 0; round < rounds; round++)
        {
            var store = new InMemoryExperimentStore();
            await store.SaveAsync(Draft("checkout"), cancellationToken);
            using var start = new Barrier(2);

            await Task.WhenAll(
                Dedicated(() => StartedAsync(store, "checkout", start, cancellationToken)),
                Dedicated(async () =>
                {
                    start.SignalAndWait(cancellationToken);

                    try
                    {
                        await store.SaveAsync(Draft("checkout"), cancellationToken);
                    }
                    catch (TraconException)
                    {
                        // The start came first and a running experiment is not edited: refused, as it must be.
                    }

                    return true;
                }));

            var kept = (await store.GetAsync(Tenant, "checkout", cancellationToken)).ShouldNotBeNull();

            kept.Status.ShouldBe(ExperimentStatus.Running, $"round {round}: the save wrote the draft back over the start");
            kept.StartedAt.ShouldNotBeNull($"round {round}: the start time was lost");
        }
    }

    [Fact]
    public async Task A_canary_rule_set_while_the_canary_is_rolled_back_leaves_the_experiment_stopped()
    {
        const int rounds = 2_000;
        var cancellationToken = TestContext.Current.CancellationToken;
        var policy = new CanaryPolicy { CanaryVariant = "candidate", MaxErrorRateDelta = 0.1, MinSampleSize = 20 };

        for (var round = 0; round < rounds; round++)
        {
            var store = new InMemoryExperimentStore();
            await store.SaveAsync(Draft("checkout"), cancellationToken);
            await store.StartAsync(Tenant, "checkout", cancellationToken);
            using var start = new Barrier(2);

            await Task.WhenAll(
                Dedicated(async () =>
                {
                    start.SignalAndWait(cancellationToken);
                    await store.SetCanaryPolicyAsync(Tenant, "checkout", policy, cancellationToken);

                    return true;
                }),
                Dedicated(async () =>
                {
                    start.SignalAndWait(cancellationToken);
                    await store.RollbackCanaryAsync(Tenant, "checkout", Draft("checkout").Variants, "error rate", cancellationToken);

                    return true;
                }));

            var kept = (await store.GetAsync(Tenant, "checkout", cancellationToken)).ShouldNotBeNull();

            kept.Status.ShouldBe(ExperimentStatus.Stopped, $"round {round}: the rule write brought a rolled-back experiment back to life");
            kept.RollbackReason.ShouldBe("error rate", $"round {round}: the reason of the rollback was lost");
        }
    }

    // Both threads have read the store when the first write lands: the one that finds the agent taken is refused, as it must be.
    private static async Task<bool> StartedAsync(InMemoryExperimentStore store, string name, Barrier start, CancellationToken cancellationToken)
    {
        start.SignalAndWait(cancellationToken);

        try
        {
            await store.StartAsync(Tenant, name, cancellationToken);

            return true;
        }
        catch (TraconException)
        {
            return false;
        }
    }

    private static Experiment Draft(string name)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Name = name,
            AgentName = "support",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "candidate", Version = 2, Weight = 50 },
            ],
        };

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
