using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A retention run is one tenant's. The <c>"*"</c> policy is the default every tenant without a policy of its own is
/// given; it is not a sweep over tenants.
/// </summary>
/// <remarks>
/// The unit tests of the executor prove it names the tenant of its run. This proves what the database does with that
/// name: a host's run, under the <c>"*"</c> policy, deletes the host's old rows and leaves another tenant's old rows
/// where they are, and writes one history row, the host's.
/// </remarks>
public sealed class RetentionTenantIsolationTests(SqliteSchemaFixture schema)
    : IClassFixture<SqliteSchemaFixture>, IAsyncLifetime
{
    private const string Host = "default";
    private const string Other = "tenant-a";

    private ServiceProvider _services = null!;

    private string Prefix => schema.Context.TablePrefix;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await schema.ResetAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTracon().UseSqlite(options =>
        {
            options.ConnectionString = schema.Context.Options.ConnectionString;
            options.TablePrefix = schema.Context.TablePrefix;
            options.AutoApplyMigrations = false;
        });

        _services = services.BuildServiceProvider();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task A_run_of_the_host_under_the_default_policy_deletes_the_hosts_rows_and_leaves_another_tenants()
    {
        await SeedOldAttachmentsAsync(Host, 3);
        await SeedOldAttachmentsAsync(Other, 4);
        await SavePolicyAsync(RetentionPolicyTenant.AllTenants, maxAgeDays: 7);

        var runs = await _services.GetRequiredService<RetentionExecutor>()
            .RunAsync(Host, RetentionTargets.Attachments, TestContext.Current.CancellationToken);

        runs.ShouldHaveSingleItem().DeletedRows.ShouldBe(3);
        (await CountAsync(Host)).ShouldBe(0);
        (await CountAsync(Other)).ShouldBe(4, "the default policy gave the host its age; it did not reach the other tenant's rows");
    }

    [Fact]
    public async Task The_run_is_written_to_the_history_of_the_tenant_it_ran_for_and_to_no_other()
    {
        await SeedOldAttachmentsAsync(Host, 1);
        await SeedOldAttachmentsAsync(Other, 1);
        await SavePolicyAsync(RetentionPolicyTenant.AllTenants, maxAgeDays: 7);

        await _services.GetRequiredService<RetentionExecutor>()
            .RunAsync(Host, RetentionTargets.Attachments, TestContext.Current.CancellationToken);

        var policies = _services.GetRequiredService<IRetentionPolicyStore>();

        (await policies.ListRunsAsync(Host, target: null, skip: 0, take: 50, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        (await policies.ListRunsAsync(Other, target: null, skip: 0, take: 50, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_preview_of_the_host_counts_only_the_hosts_rows()
    {
        await SeedOldAttachmentsAsync(Host, 2);
        await SeedOldAttachmentsAsync(Other, 5);
        await SavePolicyAsync(RetentionPolicyTenant.AllTenants, maxAgeDays: 7);

        var preview = await _services.GetRequiredService<RetentionExecutor>()
            .PreviewAsync(Host, RetentionTargets.Attachments, TestContext.Current.CancellationToken);

        preview.ShouldHaveSingleItem().MatchingRows.ShouldBe(2);
    }

    private async Task SeedOldAttachmentsAsync(string tenant, int count)
    {
        var store = _services.GetRequiredService<IAttachmentStore>();

        for (var i = 0; i < count; i++)
        {
            await store.SaveAsync(
                new AttachmentContent
                {
                    TenantId = tenant,
                    SessionId = null,
                    FileName = "photo.png",
                    MediaType = "image/png",
                    Data = new byte[] { 1, 2, 3 },
                },
                TestContext.Current.CancellationToken);
        }

        await schema.Context.ExecuteAsync($"""
            UPDATE {Prefix}attachments
               SET created_at = '{DateTimeOffset.UtcNow.AddDays(-30).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}'
             WHERE tenant_id = '{tenant}';
            """);
    }

    private async Task SavePolicyAsync(string tenantId, int maxAgeDays)
    {
        var now = DateTimeOffset.UtcNow;

        await _services.GetRequiredService<IRetentionPolicyStore>().SavePolicyAsync(
            new RetentionPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Target = RetentionTargets.Attachments,
                MaxAgeDays = maxAgeDays,
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
            },
            TestContext.Current.CancellationToken);
    }

    private async Task<long> CountAsync(string tenant)
        => await schema.Context.ScalarAsync<long>($"SELECT COUNT(*) FROM {Prefix}attachments WHERE tenant_id = '{tenant}';");
}
