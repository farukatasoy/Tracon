namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior and tenant isolation tests for the <see cref="IRetentionStore"/>
/// data plane.
/// </summary>
/// <remarks>
/// <para>
/// This contract guards against a <strong>security
/// defect</strong>: a data plane with no tenant filter, where one
/// tenant's retention policy could delete <em>all</em> tenants' rows.
/// </para>
/// <para>
/// The contract does not derive from <see cref="TenantIsolationContract{TStore}"/>:
/// the data plane has no concept of a "record" (there is no write side),
/// seeding goes through the target table's own store. Isolation is tested
/// directly here, on top of
/// <see cref="StoreCancellationContract{TStore}"/>.
/// </para>
/// </remarks>
public abstract class RetentionStoreContract : StoreCancellationContract<IRetentionStore>
{
    /// <summary>The tenant that writes the data.</summary>
    protected const string TenantA = "tenant-a";

    /// <summary>The tenant whose data must be preserved.</summary>
    protected const string TenantB = "tenant-b";

    /// <summary>
    /// Writes a row for the given tenant, older than the cutoff date, that
    /// falls under the <see cref="RetentionTargets.VoiceSessions"/> target.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the row.</param>
    /// <returns>The completion task.</returns>
    protected abstract ValueTask SeedOldRowAsync(string tenantId);

    /// <summary>A cutoff date newer than all seeded rows.</summary>
    private static DateTimeOffset Cutoff { get; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The write of a retention data plane is a <em>delete</em>: the sweep is
    /// the only thing it changes.
    /// </remarks>
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
    {
        await SeedOldRowAsync(TenantA);

        await Store.DeleteBatchAsync(
            RetentionTargets.VoiceSessions, TenantA, Cutoff, batchSize: 100, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The seeded row must still be there. A sweep that threw but deleted
    /// anyway is exactly the defect this case exists for.
    /// </remarks>
    protected override async ValueTask<bool> WroteAnythingAsync()
        => await Store.CountOlderThanAsync(
            RetentionTargets.VoiceSessions, TenantA, Cutoff, CancellationToken.None) == 0;

    [Fact]
    public async Task Count_covers_only_the_given_tenant()
    {
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff)).ShouldBe(1);
        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantB, Cutoff)).ShouldBe(1);

        // When no tenant is given, the operation is installation-wide.
        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, tenantId: null, Cutoff)).ShouldBe(2);
    }

    [Fact]
    public async Task Delete_does_NOT_touch_the_others_data()
    {
        // 🚨 Before phase 41 this test was red: there was no tenant filter,
        // and a single tenant's policy deleted the entire installation.
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        var deleted = await Store.DeleteBatchAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantA, Cutoff)).ShouldBe(0);
        (await Store.CountOlderThanAsync(RetentionTargets.VoiceSessions, TenantB, Cutoff)).ShouldBe(1);
    }

    [Fact]
    public async Task Archive_read_returns_only_the_given_tenant()
    {
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        var rows = await Store.ReadForArchiveAsync(
            RetentionTargets.VoiceSessions,
            TenantA,
            Cutoff,
            batchSize: 100);

        rows.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Row_limit_threshold_counts_only_the_given_tenant()
    {
        // Two rows for tenant A, one row for tenant B. The "at most 2 rows"
        // threshold for A must not delete anything; if the filter did not
        // work, three rows total would be counted and the threshold would
        // appear full.
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantA);
        await SeedOldRowAsync(TenantB);

        (await Store.FindRowLimitCutoffAsync(RetentionTargets.VoiceSessions, TenantA, maxRows: 3)).ShouldBeNull();
        (await Store.FindRowLimitCutoffAsync(RetentionTargets.VoiceSessions, TenantA, maxRows: 2)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Unknown_target_throws()
        => await Should.ThrowAsync<ArgumentException>(
            async () => await Store.CountOlderThanAsync("unknown-target", TenantA, Cutoff));
}
