using Tracon.Testing.Contracts.Internal;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IRateLimitCounterStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory store and the three SQL providers must pass the same scenarios; so must a
/// consumer's own store (Redis, for example). The test that matters most is
/// <see cref="Concurrent_increments_are_all_counted"/>: a store that reads the count and writes it back
/// loses increments under load, and a rate limit that loses increments admits more than it promises.
/// </para>
/// <para>
/// The windows in these tests are fixed moments, not the clock: the store never reads the time, the
/// caller passes the window.
/// </para>
/// <para>
/// There is no tenant concept here: the tenant is part of the opaque partition key. This is why the
/// contract derives from <see cref="StoreCancellationContract{TStore}"/> rather than from
/// <see cref="TenantIsolationContract{TStore}"/> -- the same pattern as <c>SingletonLeaseStoreContract</c>.
/// </para>
/// </remarks>
public abstract class RateLimitCounterStoreContract : StoreCancellationContract<IRateLimitCounterStore>
{
    private static readonly DateTimeOffset Window0 = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Window1 = Window0.AddMinutes(1);

    private static readonly DateTimeOffset Window2 = Window0.AddMinutes(2);

    /// <summary>The partition the cancellation contract counts in.</summary>
    private readonly string _cancellationPartition = Partition();

    private static string Partition() => $"endpoint:tracon:tenant:contract-{Guid.NewGuid():N}";

    /// <summary>
    /// Gets whether the store under test removes counters in <see cref="IRateLimitCounterStore.RemoveEndedWindowsAsync"/>.
    /// </summary>
    /// <remarks>
    /// Virtual and <see langword="false"/> by default: the member is optional (its default removes nothing), and a store whose
    /// counters expire by themselves, or one that was written before the member existed, keeps passing. A store that keeps its
    /// rows until someone removes them overrides it with <see langword="true"/>, and the scenarios of the removal run against
    /// it.
    /// </remarks>
    protected virtual bool RemovesEndedWindows => false;

    /// <inheritdoc />
    /// <remarks>
    /// A counter store has no pure query. Counting with a token that is already cancelled is the closest
    /// thing to one: it must throw before it counts.
    /// </remarks>
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.IncrementAsync(Partition(), Window0, cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
        => await Store.IncrementAsync(_cancellationPartition, Window0, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Counting once more reads the counter: a cancelled call that left a count behind makes this the
    /// second one.
    /// </remarks>
    protected override async ValueTask<bool> WroteAnythingAsync()
        => (await Store.IncrementAsync(_cancellationPartition, Window0, CancellationToken.None)).Count > 1;

    [Fact]
    public async Task The_first_increment_of_a_partition_counts_one_in_the_given_window()
    {
        var counted = await Store.IncrementAsync(Partition(), Window0);

        counted.Count.ShouldBe(1);
        counted.WindowStart.ShouldBe(Window0);
    }

    [Fact]
    public async Task Increments_in_the_same_window_count_up()
    {
        var partition = Partition();

        await Store.IncrementAsync(partition, Window0);
        await Store.IncrementAsync(partition, Window0);
        var counted = await Store.IncrementAsync(partition, Window0);

        counted.Count.ShouldBe(3);
        counted.WindowStart.ShouldBe(Window0);
    }

    [Fact]
    public async Task Partitions_are_counted_apart()
    {
        var first = Partition();
        var second = Partition();

        await Store.IncrementAsync(first, Window0);
        await Store.IncrementAsync(first, Window0);

        (await Store.IncrementAsync(second, Window0)).Count.ShouldBe(1);
        (await Store.IncrementAsync(first, Window0)).Count.ShouldBe(3);
    }

    /// <summary>
    /// The partition key is opaque to the store: one that differs only in letter case is another counter.
    /// </summary>
    [Fact]
    public async Task Partitions_that_differ_only_in_case_are_counted_apart()
    {
        var lower = Partition();
        var upper = lower.ToUpperInvariant();

        await Store.IncrementAsync(lower, Window0);
        await Store.IncrementAsync(lower, Window0);

        (await Store.IncrementAsync(upper, Window0)).Count.ShouldBe(1);
        (await Store.IncrementAsync(lower, Window0)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_partition_key_is_compared_as_a_whole()
    {
        // Keys that share a long prefix must not collapse into one counter: a store must not truncate.
        var prefix = $"endpoint:tracon:tenant:{new string('a', 250)}";

        await Store.IncrementAsync(prefix + "-one", Window0);
        await Store.IncrementAsync(prefix + "-one", Window0);

        (await Store.IncrementAsync(prefix + "-two", Window0)).Count.ShouldBe(1);
        (await Store.IncrementAsync(prefix + "-one", Window0)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_later_window_restarts_the_count()
    {
        var partition = Partition();

        await Store.IncrementAsync(partition, Window0);
        await Store.IncrementAsync(partition, Window0);

        var counted = await Store.IncrementAsync(partition, Window1);

        counted.Count.ShouldBe(1);
        counted.WindowStart.ShouldBe(Window1);
        (await Store.IncrementAsync(partition, Window1)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_earlier_window_is_counted_in_the_stored_window()
    {
        // An instance with a slow clock asks for the window that ended. The window must not move back
        // (that would restart the count and let a burst through); the call is counted in the stored one.
        var partition = Partition();

        await Store.IncrementAsync(partition, Window1);
        await Store.IncrementAsync(partition, Window1);

        var counted = await Store.IncrementAsync(partition, Window0);

        counted.WindowStart.ShouldBe(Window1);
        counted.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_slow_clock_does_not_move_the_stored_window_back()
    {
        var partition = Partition();

        await Store.IncrementAsync(partition, Window2);
        await Store.IncrementAsync(partition, Window0);

        var counted = await Store.IncrementAsync(partition, Window2);

        counted.WindowStart.ShouldBe(Window2);
        counted.Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_window_that_started_before_the_cutoff_is_removed_and_its_partition_counts_from_one_again()
    {
        if (!RemovesEndedWindows)
        {
            return;
        }

        var ended = Partition();

        await Store.IncrementAsync(ended, Window0);
        await Store.IncrementAsync(ended, Window0);

        var removed = await Store.RemoveEndedWindowsAsync(Window1);

        removed.ShouldBeGreaterThanOrEqualTo(1, "the counter of the window that ended was not reported as removed");
        (await Store.IncrementAsync(ended, Window0)).Count.ShouldBe(1, "a removed counter starts again at one");
    }

    [Fact]
    public async Task A_window_that_started_at_the_cutoff_or_later_keeps_its_count()
    {
        if (!RemovesEndedWindows)
        {
            return;
        }

        // The cutoff is exclusive: a counter whose window started at it is still open for the shortest window there is.
        // A counter in a window ahead of the cutoff (an instance whose clock runs ahead) is not over either, and removing
        // either one would lift the limit of a window that is open.
        var atCutoff = Partition();
        var ahead = Partition();

        await Store.IncrementAsync(atCutoff, Window1);
        await Store.IncrementAsync(atCutoff, Window1);
        await Store.IncrementAsync(ahead, Window2);
        await Store.IncrementAsync(ahead, Window2);

        await Store.RemoveEndedWindowsAsync(Window1);

        (await Store.IncrementAsync(atCutoff, Window1)).Count.ShouldBe(3, "the window that started at the cutoff was removed");
        (await Store.IncrementAsync(ahead, Window2)).Count.ShouldBe(3, "the window ahead of the cutoff was removed");
    }

    [Fact]
    public async Task A_removal_leaves_the_other_partitions_alone_and_removing_nothing_reports_zero()
    {
        if (!RemovesEndedWindows)
        {
            return;
        }

        var ended = Partition();
        var open = Partition();

        await Store.IncrementAsync(ended, Window0);
        await Store.IncrementAsync(open, Window1);
        await Store.IncrementAsync(open, Window1);

        (await Store.RemoveEndedWindowsAsync(Window1)).ShouldBeGreaterThanOrEqualTo(1);
        (await Store.RemoveEndedWindowsAsync(Window1)).ShouldBe(0, "a second removal with the same cutoff has nothing left to remove");
        (await Store.IncrementAsync(open, Window1)).Count.ShouldBe(3, "the counter of the open window was touched by the removal");
    }

    [Fact]
    public async Task Concurrent_increments_are_all_counted()
    {
        // 🚨 The property a rate limit stands on. 16 callers count at the same moment: every one must get a
        // different count and the last count must be 16. A read-then-write store returns duplicates here.
        //
        // The callers start on threads of their own, released together, at a partition of their own each round: calls
        // started one after the other on one thread never overlap, and the window two overlapping increments need is a few
        // instructions wide in a store that keeps its counts in memory, so a store that loses an increment loses one in
        // some round, not once in a while.
        const int Callers = 16;
        const int Rounds = 10;
        var token = TestContext.Current.CancellationToken;

        for (var round = 0; round < Rounds; round++)
        {
            var partition = Partition();

            var results = SimultaneousCalls.Run(
                Callers,
                _ => Store.IncrementAsync(partition, Window0, token).AsTask().GetAwaiter().GetResult().Count);

            results.Order().ShouldBe(Enumerable.Range(1, Callers).Select(count => (long)count), $"round {round}: an increment was lost or counted twice");
            (await Store.IncrementAsync(partition, Window0, token)).Count.ShouldBe(Callers + 1, $"round {round}");
        }
    }

    [Fact]
    public async Task Concurrent_increments_that_open_a_new_window_restart_the_count_once()
    {
        // The window moves while callers are counting: the count restarts once, and no increment is lost
        // on either side of the boundary. Callers on threads of their own, released together, at a partition of their own
        // each round (see the case above).
        const int Callers = 16;
        const int Rounds = 10;
        var token = TestContext.Current.CancellationToken;

        for (var round = 0; round < Rounds; round++)
        {
            var partition = Partition();

            await Store.IncrementAsync(partition, Window0, token);

            var results = SimultaneousCalls.Run(
                Callers,
                _ => Store.IncrementAsync(partition, Window1, token).AsTask().GetAwaiter().GetResult().Count);

            results.Order().ShouldBe(Enumerable.Range(1, Callers).Select(count => (long)count), $"round {round}: the count restarted twice, or an increment was lost");
        }
    }
}
