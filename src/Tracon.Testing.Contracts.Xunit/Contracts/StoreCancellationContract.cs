namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// The promise every store makes about a token that is <strong>already
/// cancelled</strong>, plus the lifecycle plumbing every store contract shares.
/// </summary>
/// <typeparam name="TStore">The store type under test.</typeparam>
/// <remarks>
/// <para>
/// A store handed a token that is already cancelled throws
/// <see cref="OperationCanceledException"/> and leaves no trace. The write
/// side is the half that matters: throwing on its own proves nothing, because
/// a check placed <em>after</em> the work throws too. The contract therefore
/// reads the store back and requires it to be untouched.
/// </para>
/// <para>
/// Cancellation <em>during</em> a call is deliberately outside this contract.
/// It races, so no deterministic case can assert where the work stopped. Only
/// a token that was already cancelled when the call started is covered.
/// </para>
/// <para>
/// <see cref="TenantIsolationContract{TStore}"/> derives from this class and
/// wires the three hooks to its own seed and count hooks, so a tenant-aware
/// store contract implements nothing extra. The four store contracts that
/// carry no tenant dimension derive from this class directly.
/// </para>
/// </remarks>
public abstract class StoreCancellationContract<TStore> : IAsyncLifetime
{
    /// <summary>The store under test.</summary>
    protected TStore Store { get; private set; } = default!;

    /// <summary>Produces an empty store for the test.</summary>
    /// <returns>A store ready for use.</returns>
    protected abstract ValueTask<TStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hook for a derived class to release its own resources.</summary>
    /// <returns>A completed task.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    // --- Cancellation hooks (phase 177) ---

    /// <summary>
    /// Calls one <strong>read</strong> method of the store with the given
    /// token.
    /// </summary>
    /// <param name="cancellationToken">The token the store must observe.</param>
    /// <returns>The completion task; any result is discarded.</returns>
    protected abstract ValueTask CancellableReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Calls one <strong>write</strong> method of the store with the given
    /// token.
    /// </summary>
    /// <param name="cancellationToken">The token the store must observe.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// The write must be one <see cref="WroteAnythingAsync"/> can see. A write
    /// this contract cannot read back proves nothing about side effects, which
    /// is why <see cref="The_write_this_contract_checks_can_be_read_back"/>
    /// runs the same pair with a live token and requires the read to find it.
    /// </remarks>
    protected abstract ValueTask CancellableWriteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether <see cref="CancellableWriteAsync"/> left anything
    /// behind.
    /// </summary>
    /// <returns><see langword="true"/> if the store holds any record the write would have created.</returns>
    protected abstract ValueTask<bool> WroteAnythingAsync();

    // --- Cancellation tests ---

    [Fact]
    public async Task Canceled_token_throws_on_read()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await CancellableReadAsync(source.Token));
    }

    [Fact]
    public async Task The_write_this_contract_checks_can_be_read_back()
    {
        await CancellableWriteAsync(TestContext.Current.CancellationToken);

        (await WroteAnythingAsync()).ShouldBeTrue(
            "The read-back cannot see the write this contract makes, so the no-trace "
            + "assertion in Canceled_token_throws_on_write_and_leaves_no_trace holds for "
            + "any store at all and proves nothing.");
    }

    [Fact]
    public async Task Canceled_token_throws_on_write_and_leaves_no_trace()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await CancellableWriteAsync(source.Token));

        // 🚨 The second half. Throwing alone would also pass if the store did
        // the work first and checked the token afterwards; reading the store
        // back is what proves the work never ran.
        (await WroteAnythingAsync()).ShouldBeFalse(
            "The cancelled write threw, but it still left a record behind.");
    }
}
