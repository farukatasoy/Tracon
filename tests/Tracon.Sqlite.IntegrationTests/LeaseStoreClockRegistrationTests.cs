using Microsoft.Extensions.DependencyInjection;
using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The clock a host registers with the container is the clock the lease stores stamp and compare with.
/// </summary>
/// <remarks>
/// Both lease stores take their clock by constructor, and the container fills it in. A store the container built with
/// the system clock instead would pass every other test, and a host that registered a fake clock would never know its
/// leases were still read against the wall clock. The check goes through <c>UseSqlite()</c>, the registration a host
/// uses; the PostgreSQL and SQL Server registrations are written the same way.
/// </remarks>
/// <param name="schema">The table prefix shared by the tests of this class.</param>
public sealed class LeaseStoreClockRegistrationTests(SqliteSchemaFixture schema) : IClassFixture<SqliteSchemaFixture>
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task The_single_executor_lease_is_stamped_with_the_registered_clock()
    {
        await schema.ResetAsync();
        var clock = new ManualClock();

        await using var provider = Build(clock);

        var registered = provider.GetRequiredService<ISingletonLeaseStore>();

        (await registered.TryAcquireAsync("registered-clock", "registered", Lease, TestContext.Current.CancellationToken)).ShouldBeTrue();

        // A store on a clock one second past the lease reads the row as over only if the row was stamped with the
        // registered clock: stamped with the wall clock it would end months after this clock.
        var later = new ManualClock();
        later.Advance(Lease + TimeSpan.FromSeconds(1));

        var other = new SqlSingletonLeaseStore(schema.Context.StoreContext, later);

        (await other.TryAcquireAsync("registered-clock", "other", Lease, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_job_lease_is_stamped_with_the_registered_clock()
    {
        await schema.ResetAsync();
        var clock = new ManualClock();

        await using var provider = Build(clock);

        var jobs = provider.GetRequiredService<IJobStore>();

        await jobs.EnqueueAsync(TestData.Job(scheduledFor: clock.GetUtcNow()), ["a"], TestContext.Current.CancellationToken);

        var leased = await jobs.LeaseAsync(
            new JobLeaseRequest { Owner = "registered", LeaseDuration = TimeSpan.FromMinutes(5) },
            TestContext.Current.CancellationToken);

        leased.ShouldNotBeNull().LeaseUntil.ShouldBe(clock.GetUtcNow() + TimeSpan.FromMinutes(5));
    }

    private ServiceProvider Build(TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddTracon().UseSqlite(options =>
        {
            options.ConnectionString = schema.Context.Options.ConnectionString;
            options.TablePrefix = schema.Context.TablePrefix;
            options.AutoApplyMigrations = false;
        });

        return services.BuildServiceProvider();
    }
}
