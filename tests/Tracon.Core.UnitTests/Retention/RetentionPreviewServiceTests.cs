using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Retention;

/// <summary>
/// <see cref="IRetentionPreviewService"/> reads what a retention run would delete for the tenant of the flow it runs
/// in, and for no other, and deletes nothing.
/// </summary>
public sealed class RetentionPreviewServiceTests
{
    [Fact]
    public async Task Each_tenant_sees_only_its_own_count()
    {
        var rig = Rig(("tenant-a", 7), ("tenant-b", 300));
        await rig.SavePolicyAsync("tenant-a", RetentionTargets.RunEvents, maxAgeDays: 30);
        await rig.SavePolicyAsync("tenant-b", RetentionTargets.RunEvents, maxAgeDays: 30);

        long forA;
        long forB;

        using (AmbientTenantScope.Begin("tenant-a"))
        {
            forA = (await rig.Service.PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken)).ShouldHaveSingleItem().MatchingRows;
        }

        using (AmbientTenantScope.Begin("tenant-b"))
        {
            forB = (await rig.Service.PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken)).ShouldHaveSingleItem().MatchingRows;
        }

        forA.ShouldBe(7);
        forB.ShouldBe(300);
        rig.Data.TenantsRead.ShouldBe(["tenant-a", "tenant-b"]);
    }

    [Fact]
    public async Task A_tenant_that_has_no_policy_of_its_own_gets_the_default_policy_and_still_counts_its_own_rows()
    {
        var rig = Rig(("tenant-a", 12));
        await rig.SavePolicyAsync(RetentionPolicyTenant.AllTenants, RetentionTargets.RunEvents, maxAgeDays: 14);

        using var scope = AmbientTenantScope.Begin("tenant-a");
        var preview = (await rig.Service.PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        preview.Enabled.ShouldBeTrue();
        preview.MaxAgeDays.ShouldBe(14);
        preview.MatchingRows.ShouldBe(12);
        rig.Data.TenantsRead.ShouldBe(["tenant-a"], "the default policy decides the age; the rows counted are the tenant's");
    }

    [Fact]
    public async Task Without_a_scope_the_default_tenant_is_the_one_read()
    {
        var rig = Rig(("default", 5));
        await rig.SavePolicyAsync("default", RetentionTargets.RunEvents, maxAgeDays: 30);

        var preview = (await rig.Service.PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        preview.MatchingRows.ShouldBe(5);
    }

    [Fact]
    public async Task Without_a_target_every_target_is_previewed_in_the_order_of_the_catalog()
    {
        var rig = Rig(("default", 1));

        var previews = await rig.Service.PreviewAsync(cancellationToken: TestContext.Current.CancellationToken);

        previews.Select(static preview => preview.Target).ShouldBe(RetentionTargets.All);
    }

    [Fact]
    public async Task The_preview_deletes_nothing()
    {
        var rig = Rig(("default", 9));
        await rig.SavePolicyAsync("default", RetentionTargets.RunEvents, maxAgeDays: 30);

        await rig.Service.PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken);

        rig.Data.Deletes.ShouldBe(0);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("not a tenant")]
    public async Task A_context_that_cannot_name_a_valid_tenant_is_refused_before_any_store_is_read(string tenant)
    {
        var rig = Rig(contextTenant: tenant);

        await Should.ThrowAsync<ArgumentException>(async () =>
            await rig.Service.PreviewAsync(RetentionTargets.RunEvents, TestContext.Current.CancellationToken));

        rig.Data.TenantsRead.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_target_is_refused()
    {
        var rig = Rig();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await rig.Service.PreviewAsync("not_a_real_table", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_service_can_be_registered_by_the_host_and_theirs_wins()
    {
        var services = new ServiceCollection();
        var own = new OwnService();
        services.AddSingleton<IRetentionPreviewService>(own);
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRetentionPreviewService>().ShouldBeSameAs(own);
    }

    private static Fixture Rig(params (string Tenant, long Count)[] counts)
        => Rig(counts, contextTenant: null);

    private static Fixture Rig(string contextTenant)
        => Rig([], contextTenant);

    private static Fixture Rig((string Tenant, long Count)[] counts, string? contextTenant)
    {
        var policies = new InMemoryRetentionPolicyStore();
        var data = new CountingStore(counts.ToDictionary(static entry => entry.Tenant, static entry => entry.Count, StringComparer.Ordinal));
        var options = new TraconRetentionOptions { BatchDelay = TimeSpan.Zero };
        var monitor = new StaticOptionsMonitor<TraconRetentionOptions>(options);

        var executor = new RetentionExecutor(
            policies,
            data,
            new RetentionPolicyResolver(policies, monitor),
            monitor,
            archiveSink: null,
            new ManualClock(new DateTimeOffset(2026, 8, 5, 3, 0, 0, TimeSpan.Zero)));

        ITenantContext context = contextTenant is null
            ? new SingleTenantContext(Options.Create(new TraconOptions()))
            : new FixedTenant(contextTenant);

        return new Fixture(new RetentionPreviewService(executor, context), policies, data);
    }

    private sealed record Fixture(RetentionPreviewService Service, InMemoryRetentionPolicyStore Policies, CountingStore Data)
    {
        public async Task SavePolicyAsync(string tenantId, string target, int maxAgeDays)
        {
            var now = DateTimeOffset.UtcNow;

            await Policies.SavePolicyAsync(
                new RetentionPolicy
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Target = target,
                    MaxAgeDays = maxAgeDays,
                    Enabled = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                TestContext.Current.CancellationToken);
        }
    }

    private sealed class FixedTenant(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class OwnService : IRetentionPreviewService
    {
        public ValueTask<IReadOnlyList<RetentionPreview>> PreviewAsync(string? target = null, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<RetentionPreview>)[]);
    }

    /// <summary>Counts rows by tenant, the way a data plane that filters by tenant does.</summary>
    private sealed class CountingStore(Dictionary<string, long> rowsByTenant) : IRetentionStore
    {
        public List<string?> TenantsRead { get; } = [];

        public int Deletes { get; private set; }

        public ValueTask<long> CountOlderThanAsync(string target, string? tenantId, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            TenantsRead.Add(tenantId);

            return new ValueTask<long>(tenantId is not null && rowsByTenant.TryGetValue(tenantId, out var count) ? count : 0);
        }

        public ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(string target, string? tenantId, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<ArchiveRow>)[]);

        public ValueTask<int> DeleteBatchAsync(string target, string? tenantId, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default)
        {
            Deletes++;

            return new ValueTask<int>(0);
        }

        public ValueTask<int> ArchiveBatchAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            int batchSize,
            Func<IReadOnlyList<ArchiveRow>, CancellationToken, ValueTask> archiveAsync,
            CancellationToken cancellationToken = default)
        {
            Deletes++;

            return new ValueTask<int>(0);
        }

        public ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(string target, string? tenantId, long maxRows, CancellationToken cancellationToken = default)
            => new((DateTimeOffset?)null);
    }
}
