using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.PostgreSql.IntegrationTests.FailureManifests;

/// <summary>
/// Failure manifest: <strong>two versions are up at the same time</strong>
/// (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// The window every rolling deployment has: the new version has migrated the
/// schema, and old processes are still serving from it until the last one is
/// drained. The written expectation, verified below:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <strong>Migrations are additive during the window.</strong> An
///       already-applied migration set is a superset of what an older process
///       knows about, so that process keeps reading and writing the tables it
///       does know - the optional sets it never enabled are simply extra
///       tables it ignores.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>The queue stays shared.</strong> An old-version worker and a
///       new-version worker lease from one table and never take the same job,
///       because the lease is a database-level guarantee rather than a
///       process-level one.
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>The verification is read-only.</strong> Phase 156's state
///       preflight is what answers "can the new version read what is already
///       stored"; running it against the mixed schema is the check an operator
///       makes before starting the window, and it writes nothing.
///     </description>
///   </item>
/// </list>
/// <para>
/// 🚨 <strong>What this does NOT do.</strong> The repository ships one
/// version, so an "old process" here is a context that enabled FEWER optional
/// migration sets - a real difference in what a process knows the schema to
/// be, but not a second binary. Two AgentPrism releases running side by side
/// is not measured, and no compatibility window is promised on the strength of
/// this test.
/// </para>
/// </remarks>
public sealed class RollingUpgradeTests(PostgresFixture fixture)
{
    [Fact]
    public async Task An_older_process_keeps_reading_and_writing_after_the_new_version_migrated()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        // The NEW version: every optional migration set applied.
        await using var upgraded = PostgresTestContext.Create(
            fixture,
            schemaName,
            enableKnowledge: true,
            enableReadViews: true);

        await upgraded.Migrations.ApplyAsync();

        // The OLD version: the same schema, but a process that knows nothing
        // about the optional sets the upgrade added.
        await using var older = PostgresTestContext.Create(
            fixture,
            schemaName,
            enableKnowledge: false,
            enableReadViews: false);

        var runId = Guid.NewGuid();
        await older.Runs.StartRunAsync(TestData.Run(runId));
        await older.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        // The old process's write is visible to the new one, and vice versa.
        (await upgraded.Runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Completed);

        var newerRunId = Guid.NewGuid();
        await upgraded.Runs.StartRunAsync(TestData.Run(newerRunId));

        (await older.Runs.GetRunAsync(newerRunId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_two_versions_share_one_queue_and_never_lease_the_same_job()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var upgraded = PostgresTestContext.Create(
            fixture,
            schemaName,
            enableKnowledge: true,
            enableReadViews: true);

        await upgraded.Migrations.ApplyAsync();

        await using var older = PostgresTestContext.Create(
            fixture,
            schemaName,
            enableKnowledge: false,
            enableReadViews: false);

        var queued = new List<Guid>();

        for (var i = 0; i < 20; i++)
        {
            queued.Add((await upgraded.Jobs.EnqueueAsync(TestData.Job(), ["input"])).Id);
        }

        var leasedByOld = new List<Guid>();
        var leasedByNew = new List<Guid>();

        await Task.WhenAll(
            LeaseAllAsync(older.Jobs, "old-version", leasedByOld),
            LeaseAllAsync(upgraded.Jobs, "new-version", leasedByNew));

        var all = leasedByOld.Concat(leasedByNew).ToList();

        all.Count.ShouldBe(queued.Count);
        all.Distinct().Count().ShouldBe(queued.Count);
        all.ToHashSet().SetEquals(queued).ShouldBeTrue();
    }

    [Fact]
    public async Task The_preflight_reads_the_mixed_schema_and_writes_nothing()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var upgraded = PostgresTestContext.Create(
            fixture,
            schemaName,
            enableKnowledge: true,
            enableReadViews: true);

        await upgraded.Migrations.ApplyAsync();

        await using var older = PostgresTestContext.Create(
            fixture,
            schemaName,
            enableKnowledge: false,
            enableReadViews: false);

        await older.Sessions.SaveAsync(TestData.Session("written-by-the-old-version") with
        {
            TenantId = "default",
            StateSchemaVersion = 1,
            StateMafVersion = "1.18.0",
        });

        var before = await upgraded.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.sessions;");

        var report = await new StatePreflight(new SqlStatePreflightReader(upgraded.StoreContext))
            .RunAsync(samplePerGeneration: 5);

        // The new version can read what the old one wrote, and asking the
        // question changed nothing - the preflight is the operator's
        // pre-window check, not a migration.
        report.Sessions.ShouldHaveSingleItem().RecordCount.ShouldBe(1);
        report.SampleFailures.ShouldBeEmpty();
        (await upgraded.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.sessions;")).ShouldBe(before);
    }

    private static async Task LeaseAllAsync(SqlJobStore store, string owner, List<Guid> leased)
    {
        while (await store.LeaseAsync(owner, TimeSpan.FromMinutes(5), lanes: null) is { } job)
        {
            lock (leased)
            {
                leased.Add(job.Id);
            }
        }
    }
}
