namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="ISingletonLeaseStore"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory store and the three SQL providers must pass the same
/// scenarios. The lease-expiry test runs against real time (a short lease
/// plus a short wait): the SQL implementations do not take their clock from
/// an injectable <see cref="TimeProvider"/> -- the same rationale as
/// <c>JobStoreContract</c>.
/// </para>
/// <para>
/// There is no tenant concept here: single-executor election is a
/// deployment-wide concern. This is why the contract derives from
/// <see cref="StoreCancellationContract{TStore}"/> rather than from
/// <see cref="TenantIsolationContract{TStore}"/> -- the same pattern as
/// <c>RetentionStoreContract</c>.
/// </para>
/// </remarks>
public abstract class SingletonLeaseStoreContract : StoreCancellationContract<ISingletonLeaseStore>
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>The lease the cancellation contract tries to acquire.</summary>
    private readonly string _cancellationLease = Lease();

    private static string Lease() => $"lease-{Guid.NewGuid():N}";

    private static string Owner() => $"owner-{Guid.NewGuid():N}";

    /// <inheritdoc />
    /// <remarks>
    /// A lease store has no pure query. Renewing a lease nobody holds is the
    /// closest thing to one: it reports <see langword="false"/> and writes
    /// nothing.
    /// </remarks>
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.RenewAsync(Lease(), Owner(), LeaseDuration, cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
        => await Store.TryAcquireAsync(_cancellationLease, Owner(), LeaseDuration, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A <em>different</em> owner asks for the same lease. It can only take
    /// it if the cancelled call left no row behind.
    /// </remarks>
    protected override async ValueTask<bool> WroteAnythingAsync()
        => !await Store.TryAcquireAsync(_cancellationLease, Owner(), LeaseDuration, CancellationToken.None);

    [Fact]
    public async Task Empty_lease_can_be_acquired()
    {
        var acquired = await Store.TryAcquireAsync(Lease(), Owner(), TimeSpan.FromMinutes(5));

        acquired.ShouldBeTrue();
    }

    [Fact]
    public async Task Second_owner_cannot_acquire_while_lease_is_held()
    {
        var lease = Lease();
        await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5));

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Same_owner_can_reacquire_its_own_lease()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));

        (await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task Someone_else_can_acquire_once_the_lease_expires()
    {
        var lease = Lease();

        await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task Owner_can_renew_its_lease()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));

        (await Store.RenewAsync(lease, owner, TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task RenewAsync_returns_false_once_the_lease_has_passed_to_someone_else()
    {
        var lease = Lease();
        var firstOwner = Owner();

        await Store.TryAcquireAsync(lease, firstOwner, TimeSpan.FromMilliseconds(20));
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5));

        // 🚨 If the former owner tries to renew its lease, this must return
        // false -- the caller MUST step down (Tests table,
        // docs/arsiv/fazlar/42-TEK-YURUTUCU-SECIMI.md).
        (await Store.RenewAsync(lease, firstOwner, TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task RenewAsync_returns_false_for_a_non_owner()
    {
        var lease = Lease();

        (await Store.RenewAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }

    [Fact]
    public async Task Released_lease_can_be_reacquired_immediately_by_someone_else()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));
        await Store.ReleaseAsync(lease, owner);

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeTrue();
    }

    [Fact]
    public async Task ReleaseAsync_by_a_non_owner_does_nothing()
    {
        var lease = Lease();
        var owner = Owner();

        await Store.TryAcquireAsync(lease, owner, TimeSpan.FromMinutes(5));

        // A release attempt by another 'owner' must NOT AFFECT the current lease.
        await Store.ReleaseAsync(lease, Owner());

        (await Store.TryAcquireAsync(lease, Owner(), TimeSpan.FromMinutes(5))).ShouldBeFalse();
    }
}
