using System.Collections.Concurrent;
using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Proof of the <c>FOR UPDATE SKIP LOCKED</c> guarantee: two real
/// <see cref="IJobStore"/> instances connect to the same database and race
/// for the same queue.
/// </summary>
/// <remarks>
/// In the in-memory store this guarantee is provided by a <c>lock</c> and is
/// uncontested in a single process; the real proof can only come from a real
/// PostgreSQL instance under real concurrency. Rationale:
/// docs/arsiv/fazlar/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md, section "Testler".
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

    private static async Task LeaseAllAsync(SqlJobStore store, string owner, ConcurrentBag<Guid> leased)
    {
        while (true)
        {
            var job = await store.LeaseAsync(owner, TimeSpan.FromMinutes(5), lanes: null);

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
            var job = await store.LeaseAsync(owner, TimeSpan.FromMinutes(5), lanes);

            if (job is null)
            {
                return;
            }

            leased.Add(job);
        }
    }
}
