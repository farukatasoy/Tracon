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
    public async Task Concurrent_increments_are_all_counted()
    {
        // 🚨 The property a rate limit stands on. 64 callers count at the same moment: every one must get a
        // different count and the last count must be 64. A read-then-write store returns duplicates here.
        const int Callers = 64;
        var partition = Partition();

        var results = await Task.WhenAll(
            Enumerable.Range(0, Callers).Select(_ => Task.Run(
                async () => (await Store.IncrementAsync(partition, Window0, TestContext.Current.CancellationToken)).Count,
                TestContext.Current.CancellationToken)));

        results.Order().ShouldBe(Enumerable.Range(1, Callers).Select(count => (long)count));
        (await Store.IncrementAsync(partition, Window0)).Count.ShouldBe(Callers + 1);
    }

    [Fact]
    public async Task Concurrent_increments_that_open_a_new_window_restart_the_count_once()
    {
        // The window moves while callers are counting: the count restarts once, and no increment is lost
        // on either side of the boundary.
        const int Callers = 32;
        var partition = Partition();

        await Store.IncrementAsync(partition, Window0);

        var results = await Task.WhenAll(
            Enumerable.Range(0, Callers).Select(_ => Task.Run(
                async () => (await Store.IncrementAsync(partition, Window1, TestContext.Current.CancellationToken)).Count,
                TestContext.Current.CancellationToken)));

        results.Order().ShouldBe(Enumerable.Range(1, Callers).Select(count => (long)count));
    }
}
