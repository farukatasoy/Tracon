using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// A check at the start of new work in a running tree counts each of the tree's entries once, even when a run of the
/// same tree writes its entry while the check reads.
/// </summary>
/// <remarks>
/// <para>
/// The check adds two values: the counter, and the part of the tree's spend the counter has not seen. A run that ends
/// writes the counter and tells the tree's ledger. When the entry landed between the check's two reads it was in both,
/// and a start the quota allowed was refused: a background child started while its root was closing read 20 of 15
/// tokens on a tree that had spent 10.
/// </para>
/// <para>
/// A store that holds one call until the test lets it go puts the writer and the check in the order each case names.
/// The order is set by the test, so nothing waits on time.
/// </para>
/// </remarks>
public sealed class TreeQuotaCountOnceTests
{
    private const string Tenant = "acme";
    private const string Root = "router";
    private const long Limit = 15;
    private const long RootTokens = 10;
    private const decimal RootCost = 0.25m;

    [Fact]
    public async Task A_root_entry_that_reaches_the_counter_while_a_check_waits_to_read_it_is_counted_once()
    {
        var tree = await Tree.StartAsync();
        var read = tree.Store.HoldNextUsageRead();

        var check = TreeQuotaGate.RefusalAsync(tree.Budget, TreeStartKind.AgentStep, CancellationToken.None).AsTask();

        await read.Entered.WaitAsync(TestContext.Current.CancellationToken);

        // The root closes and writes its whole entry before the check reads the counter.
        await tree.RecordRootAsync(CancellationToken.None);
        read.Release();

        var refusal = await check;

        refusal.ShouldBeNull($"the tree has spent {RootTokens} of {Limit} tokens: {refusal?.Reason}");
    }

    [Fact]
    public async Task A_check_made_while_a_root_entry_is_being_written_counts_it_once()
    {
        var tree = await Tree.StartAsync();

        // The entry is in the store; the writer is still claiming the thresholds it crossed.
        var claim = tree.Store.HoldNextList();
        var recording = tree.RecordRootAsync(CancellationToken.None);

        await claim.Entered.WaitAsync(TestContext.Current.CancellationToken);

        var refusal = await TreeQuotaGate.RefusalAsync(tree.Budget, TreeStartKind.AgentStep, CancellationToken.None);

        claim.Release();
        await recording;

        refusal.ShouldBeNull($"the tree has spent {RootTokens} of {Limit} tokens: {refusal?.Reason}");
    }

    [Fact]
    public async Task A_root_entry_whose_write_is_cancelled_stays_the_trees_to_count()
    {
        var tree = await Tree.StartAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => tree.RecordRootAsync(new CancellationToken(canceled: true)));

        tree.Budget.UnrecordedSpend().ShouldBe((RootTokens, 0m), "the counter did not take the entry, so the tree still owes it");
    }

    [Fact]
    public async Task A_priced_root_entry_whose_write_is_cancelled_stays_the_trees_to_count()
    {
        var tree = await Tree.StartAsync(RootCost);

        await Should.ThrowAsync<OperationCanceledException>(() => tree.RecordRootAsync(new CancellationToken(canceled: true), RootCost));

        // A function node's start compares only the amount, so the amount must be owed as well as the tokens.
        tree.Budget.UnrecordedSpend().ShouldBe((RootTokens, RootCost), "the counter did not take the entry, so the tree still owes its amount");
    }

    /// <summary>A root that has spent its tokens and names a counter with a token limit.</summary>
    private sealed class Tree
    {
        private readonly ManualTimeProvider _clock;
        private readonly TreeAccount _account;

        private Tree(ManualTimeProvider clock, HoldingQuotaStore store, TreeAccount account, AgentRunBudget budget)
        {
            _clock = clock;
            _account = account;
            Store = store;
            Budget = budget;
        }

        public HoldingQuotaStore Store { get; }

        public AgentRunBudget Budget { get; }

        public static async Task<Tree> StartAsync(decimal? cost = null)
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));
            var store = new HoldingQuotaStore(new InMemoryQuotaStore());
            var enforcer = new QuotaEnforcer(
                store,
                new StaticOptionsMonitor<TraconQuotaOptions>(new TraconQuotaOptions()),
                webhookPublisher: null,
                clock);

            await store.SaveAsync(new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                AgentName = Root,
                Period = QuotaPeriod.Daily,
                MaxTokens = Limit,
                Enabled = true,
                CreatedAt = clock.GetUtcNow(),
                UpdatedAt = clock.GetUtcNow(),
            });

            var account = new TreeAccount(Tenant, Root, UserId: null, enforcer);
            var budget = new AgentRunBudget { Account = account };

            // The root's own model call: in the budget, not yet in the counter.
            budget.RecordUsage(RootTokens, cost);

            return new Tree(clock, store, account, budget);
        }

        public async Task RecordRootAsync(CancellationToken cancellationToken, decimal? amount = null)
            => _ = await TreeQuota.RecordAsync(_account, Guid.NewGuid(), runs: 1, RootTokens, amount, _clock.GetUtcNow(), cancellationToken);
    }

    /// <summary>One call held: it signals that it has been reached, then waits until the test lets it go on.</summary>
    private sealed class Hold
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public void Release() => _released.TrySetResult();

        public Task PassAsync()
        {
            _entered.TrySetResult();

            return _released.Task;
        }
    }

    /// <summary>A quota store that can hold its next rule list or its next usage read.</summary>
    private sealed class HoldingQuotaStore(IQuotaStore inner) : IQuotaStore
    {
        private Hold? _nextList;
        private Hold? _nextUsageRead;

        public Hold HoldNextList() => _nextList = new Hold();

        public Hold HoldNextUsageRead() => _nextUsageRead = new Hold();

        public async ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _nextList, null) is { } hold)
            {
                await hold.PassAsync();
            }

            return await inner.ListAsync(tenantId, cancellationToken);
        }

        public async ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _nextUsageRead, null) is { } hold)
            {
                await hold.PassAsync();
            }

            return await inner.GetUsageAsync(query, cancellationToken);
        }

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
        {
            // A store that sees a stopped caller does not write.
            cancellationToken.ThrowIfCancellationRequested();

            return inner.AddUsageAsync(consumption, periodStarts, cancellationToken);
        }

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => inner.SaveAsync(definition, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, id, cancellationToken);

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => inner.TryClaimThresholdNotificationAsync(tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);
    }
}
