using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The retention fan-out over a real database: the jobs it queues are real rows of the job queue, and each job deletes
/// the rows of its own tenant by that tenant's own policy.
/// </summary>
public sealed class RetentionFanOutDatabaseTests(SqliteSchemaFixture schema)
    : IClassFixture<SqliteSchemaFixture>, IAsyncLifetime
{
    private const string Host = "default";

    private ServiceProvider _services = null!;

    private string Prefix => schema.Context.TablePrefix;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        await schema.ResetAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRetentionTenantSource>(new FixedSource("tenant-a", "tenant-b", "tenant-c"));
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
    public async Task Each_tenant_is_cleaned_by_its_own_policy_and_the_tenant_nobody_named_is_left_alone()
    {
        foreach (var tenant in new[] { "tenant-a", "tenant-b", "tenant-c", "tenant-d" })
        {
            await SeedOldAttachmentsAsync(tenant, 2);
        }

        // The default of every tenant: seven days. tenant-b keeps ten years; tenant-c has no policy of its own.
        await SavePolicyAsync(RetentionPolicyTenant.AllTenants, maxAgeDays: 7, enabled: true);
        await SavePolicyAsync("tenant-b", maxAgeDays: 3650, enabled: true);
        await SavePolicyAsync("tenant-a", maxAgeDays: 7, enabled: false);

        await RunFanOutThenEveryRetentionJobAsync();

        (await CountAsync("tenant-a")).ShouldBe(2, "its own policy is off");
        (await CountAsync("tenant-b")).ShouldBe(2, "its own policy keeps ten years");
        (await CountAsync("tenant-c")).ShouldBe(0, "the default policy applies");
        (await CountAsync("tenant-d")).ShouldBe(2, "the source did not name it");
    }

    [Fact]
    public async Task The_queued_retention_jobs_are_rows_in_the_queue_in_each_tenants_name()
    {
        await RunFanOutAsync();

        var jobs = await _services.GetRequiredService<IJobStore>()
            .QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention, Take = 50 }, TestContext.Current.CancellationToken);

        jobs.Select(static job => job.TenantId).Order(StringComparer.Ordinal).ToArray().ShouldBe(["tenant-a", "tenant-b", "tenant-c"]);
        jobs.ShouldAllBe(static job => job.Status == JobStatus.Pending);
    }

    [Fact]
    public async Task A_second_fan_out_over_the_waiting_jobs_adds_none()
    {
        await RunFanOutAsync();
        await RunFanOutAsync();

        (await _services.GetRequiredService<IJobStore>()
            .QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention, Take = 50 }, TestContext.Current.CancellationToken))
            .Count.ShouldBe(3);
    }

    private async Task RunFanOutThenEveryRetentionJobAsync()
    {
        await RunFanOutAsync();

        var store = _services.GetRequiredService<IJobStore>();
        var jobs = await store.QueryAsync(new JobQuery { HandlerKey = JobHandlerKeys.Retention, Take = 50 }, TestContext.Current.CancellationToken);

        foreach (var job in jobs)
        {
            await using var scope = _services.CreateAsyncScope();
            using var tenant = AmbientTenantScope.Begin(job.TenantId);

            await scope.ServiceProvider.GetRequiredService<RetentionJobHandler>()
                .ExecuteAsync(ContextOf(job), TestContext.Current.CancellationToken);
        }
    }

    private async Task RunFanOutAsync()
    {
        await using var scope = _services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<RetentionFanOutJobHandler>().ExecuteAsync(
            ContextOf(new JobRecord
            {
                Id = Guid.NewGuid(),
                TenantId = Host,
                HandlerKey = JobHandlerKeys.RetentionFanOut,
                TargetName = RetentionTargets.Attachments,
                Status = JobStatus.Running,
                ScheduledFor = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
            }),
            TestContext.Current.CancellationToken);
    }

    private static JobContext ContextOf(JobRecord job)
        => new()
        {
            Job = job,
            Items = [],
            ReportItemAsync = static (_, _) => default,
            IsCancelledAsync = static _ => new ValueTask<bool>(false),
        };

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

    private async Task SavePolicyAsync(string tenantId, int maxAgeDays, bool enabled)
    {
        var now = DateTimeOffset.UtcNow;

        await _services.GetRequiredService<IRetentionPolicyStore>().SavePolicyAsync(
            new RetentionPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Target = RetentionTargets.Attachments,
                MaxAgeDays = maxAgeDays,
                Enabled = enabled,
                CreatedAt = now,
                UpdatedAt = now,
            },
            TestContext.Current.CancellationToken);
    }

    private async Task<long> CountAsync(string tenant)
        => await schema.Context.ScalarAsync<long>($"SELECT COUNT(*) FROM {Prefix}attachments WHERE tenant_id = '{tenant}';");

    private sealed class FixedSource(params string[] tenants) : IRetentionTenantSource
    {
        public async IAsyncEnumerable<string> ListTenantsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var tenant in tenants)
            {
                yield return tenant;

                await Task.Yield();
            }
        }
    }
}
