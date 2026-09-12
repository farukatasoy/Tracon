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
/// directly here.
/// </para>
/// </remarks>
public abstract class RetentionStoreContract : IAsyncLifetime
{
    /// <summary>The tenant that writes the data.</summary>
    protected const string TenantA = "tenant-a";

    /// <summary>The tenant whose data must be preserved.</summary>
    protected const string TenantB = "tenant-b";

    /// <summary>The data plane under test.</summary>
    protected IRetentionStore Store { get; private set; } = null!;

    /// <summary>Produces an empty data plane for testing.</summary>
    /// <returns>A store ready for use.</returns>
    protected abstract ValueTask<IRetentionStore> CreateStoreAsync();

    /// <summary>
    /// Writes a row for the given tenant, older than the cutoff date, that
    /// falls under the <see cref="RetentionTargets.VoiceSessions"/> target.
    /// </summary>
    /// <param name="tenantId">The tenant that owns the row.</param>
    /// <returns>The completion task.</returns>
    protected abstract ValueTask SeedOldRowAsync(string tenantId);

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hook for the derived class to release its own resources.</summary>
    /// <returns>The completion task.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    /// <summary>A cutoff date newer than all seeded rows.</summary>
    private static DateTimeOffset Cutoff { get; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
