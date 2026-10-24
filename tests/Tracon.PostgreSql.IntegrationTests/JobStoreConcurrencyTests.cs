using System.Collections.Concurrent;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Proof of the <c>FOR UPDATE SKIP LOCKED</c> guarantee: two real
/// <see cref="IJobStore"/> instances connect to the same database and race
/// for the same queue.
/// </summary>
/// <remarks>
/// In the in-memory store this guarantee is provided by a <c>lock</c> and is
/// uncontested in a single process; the real proof can only come from a real
/// PostgreSQL instance under real concurrency. Rationale:
/// Phase 17, section "Testler".
/// </remarks>
public sealed class JobStoreConcurrencyTests(PostgresFixture fixture)
{
    private const int JobCount = 50;

    [Fact]
    public async Task Two_workers_never_lease_the_same_job_twice()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var seed = PostgresTestContext.Create(fixture, schemaName);
        await seed.Migrations.ApplyAsync();

        var expectedIds = new List<Guid>();

        for (var i = 0; i < JobCount; i++)
        {
            var job = await seed.Jobs.EnqueueAsync(TestData.Job(), ["input"]);
            expectedIds.Add(job.Id);
        }

        await using var workerA = PostgresTestContext.Create(fixture, schemaName);
        await using var workerB = PostgresTestContext.Create(fixture, schemaName);

        var leasedByA = new ConcurrentBag<Guid>();
        var leasedByB = new ConcurrentBag<Guid>();

        await Task.WhenAll(
            LeaseAllAsync(workerA.Jobs, "worker-a", leasedByA),
            LeaseAllAsync(workerB.Jobs, "worker-b", leasedByB));

        var all = leasedByA.Concat(leasedByB).ToList();

        // No job was leased twice (total count equals distinct count) and
        // every job in the queue was picked up exactly once. Set equality is
        // checked; the split between the two workers does not matter.
        all.Count.ShouldBe(JobCount);
        all.Distinct().Count().ShouldBe(JobCount);
        all.ToHashSet().SetEquals(expectedIds).ShouldBeTrue();
    }

    [Fact]
    public async Task A_worker_scoped_to_one_lane_never_leases_another_lanes_job_under_race()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var seed = PostgresTestContext.Create(fixture, schemaName);
        await seed.Migrations.ApplyAsync();

        for (var i = 0; i < JobCount; i++)
        {
            await seed.Jobs.EnqueueAsync(TestData.Job() with { Lane = i % 2 == 0 ? "media" : "default" }, ["input"]);
        }

        await using var workerA = PostgresTestContext.Create(fixture, schemaName);
        await using var workerB = PostgresTestContext.Create(fixture, schemaName);

        var leasedByA = new ConcurrentBag<JobRecord>();
        var leasedByB = new ConcurrentBag<JobRecord>();

        await Task.WhenAll(
            LeaseAllAsync(workerA.Jobs, "worker-a", leasedByA, ["media"]),
            LeaseAllAsync(workerB.Jobs, "worker-b", leasedByB, ["default"]));

        (leasedByA.Count + leasedByB.Count).ShouldBe(JobCount);
        leasedByA.ShouldAllBe(static job => job.Lane == "media");
        leasedByB.ShouldAllBe(static job => job.Lane == "default");
    }

    /// <summary>
    /// Phase 240: the tenant ceiling is a count that is read, not locked, so workers that lease at the same
    /// moment can take a tenant past it. The bound is the one the contract documents: by at most the number of
    /// workers (the ceiling less one, plus every worker that read a count under it).
    /// </summary>
    [Fact]
    public async Task Workers_leasing_under_a_tenant_ceiling_lease_every_job_once_and_pass_it_by_no_more_than_their_own_number()
    {
        const int workers = 4;
        const int ceiling = 3;
        const int rounds = 15;

        var schemaName = PostgresTestContext.NewSchemaName();

        await using var seed = PostgresTestContext.Create(fixture, schemaName);
        await seed.Migrations.ApplyAsync();

        var contexts = new List<PostgresTestContext>();

        try
        {
            for (var worker = 0; worker < workers; worker++)
            {
                contexts.Add(PostgresTestContext.Create(fixture, schemaName));
            }

            var cap = new JobTenantCap { HandlerKeys = [JobHandlerKeys.Workflow, JobHandlerKeys.WorkflowRun], MaxLeasedPerTenant = ceiling };

            for (var round = 0; round < rounds; round++)
            {
                // A tenant of its own each round: the leases of the last round stay alive and must not count.
                var tenant = $"tenant-{round}";

                for (var index = 0; index < 40; index++)
                {
                    await seed.Jobs.EnqueueAsync(TestData.Job(tenant) with { HandlerKey = JobHandlerKeys.Workflow }, ["input"]);
                }

                var leased = new ConcurrentBag<JobRecord>();

                await Task.WhenAll(contexts.Select(context => LeaseUnderCapAsync(context.Jobs, tenant, cap, leased)));

                var ofTenant = leased.Where(job => string.Equals(job.TenantId, tenant, StringComparison.OrdinalIgnoreCase)).ToList();

                ofTenant.Select(static job => job.Id).Distinct().Count().ShouldBe(ofTenant.Count, "a job was leased twice");
                ofTenant.Count.ShouldBeGreaterThanOrEqualTo(ceiling);
                ofTenant.Count.ShouldBeLessThanOrEqualTo(ceiling - 1 + workers);
            }

        }
        finally
        {
            foreach (var context in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    private static async Task LeaseUnderCapAsync(SqlJobStore store, string owner, JobTenantCap cap, ConcurrentBag<JobRecord> leased)
    {
        while (true)
        {
            var job = await store.LeaseAsync(
                new JobLeaseRequest { Owner = $"{owner}-{Guid.NewGuid():N}", LeaseDuration = TimeSpan.FromMinutes(5), TenantCap = cap });

            if (job is null)
            {
                return;
            }

            leased.Add(job);
        }
    }

    private static async Task LeaseAllAsync(SqlJobStore store, string owner, ConcurrentBag<Guid> leased)
    {
        while (true)
        {
            var job = await store.LeaseAsync(new JobLeaseRequest { Owner = owner, LeaseDuration = TimeSpan.FromMinutes(5) });

            if (job is null)
            {
                return;
            }

            leased.Add(job.Id);
        }
    }

    private static async Task LeaseAllAsync(
        SqlJobStore store,
        string owner,
        ConcurrentBag<JobRecord> leased,
        IReadOnlyList<string> lanes)
    {
        while (true)
        {
            var job = await store.LeaseAsync(new JobLeaseRequest { Owner = owner, LeaseDuration = TimeSpan.FromMinutes(5), Lanes = lanes });

            if (job is null)
            {
                return;
            }

            leased.Add(job);
        }
    }
}
