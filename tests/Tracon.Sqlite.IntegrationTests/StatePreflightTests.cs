using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Proves the read-only state preflight against a real SQLite database.
/// </summary>
/// <remarks>
/// Two claims here cannot be made at the unit level, because both are about
/// the SQL that actually reaches the database:
/// <see cref="The_check_writes_nothing"/> compares a full table snapshot from
/// before and after, and <see cref="Counting_covers_every_tenant"/> shows the
/// count is NOT tenant-filtered the way every application-facing store is.
/// </remarks>
public sealed class StatePreflightTests(SqliteFixture fixture)
{
    [Fact]
    public async Task The_check_writes_nothing()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionsAsync(context, ("a", "t1", 1), ("b", "t1", 1), ("c", "t2", 1));
        await SeedCheckpointAsync(context, "cp-a", "t1", generation: 1);

        var before = await SnapshotAsync(context);
        before.ShouldContain("cp-a", Case.Sensitive, "a snapshot that came back empty would pass this test for the wrong reason");
        before.ShouldContain("|t2|", Case.Sensitive);

        var report = await new StatePreflight(new SqlStatePreflightReader(context.StoreContext)).RunAsync(samplePerGeneration: 5);

        var after = await SnapshotAsync(context);

        // Not "the row count is the same" — the whole content of both tables,
        // every column, byte for byte. A preflight that touched updated_at or
        // bumped version would still keep the count.
        after.ShouldBe(before);
        report.SampledCount.ShouldBeGreaterThan(0, "a check that read nothing would pass this test for the wrong reason");
    }

    [Fact]
    public async Task Counting_covers_every_tenant()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionsAsync(context, ("a", "t1", 1), ("b", "t2", 1), ("c", "t3", 1));

        var report = await new StatePreflight(new SqlStatePreflightReader(context.StoreContext)).RunAsync(samplePerGeneration: 0);

        // An upgrade replaces the process for every tenant at once, so a
        // tenant-filtered count would silently under-report the risk.
        report.Sessions.ShouldHaveSingleItem().RecordCount.ShouldBe(3);
    }

    [Fact]
    public async Task A_generation_this_build_cannot_read_is_counted_and_left_untouched()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionsAsync(context, ("current", "t1", 1), ("future", "t1", 99));

        var report = await new StatePreflight(new SqlStatePreflightReader(context.StoreContext)).RunAsync(samplePerGeneration: 5);

        report.HasUnreadableGeneration.ShouldBeTrue();
        report.UnreadableRecordCount.ShouldBe(1);

        var stillThere = await context.ScalarAsync<long>(
            $"SELECT state_schema_version FROM {context.TablePrefix}sessions WHERE id = 'future';");
        stillThere.ShouldBe(99, "an unreadable session is never rewritten or deleted");
    }

    [Fact]
    public async Task The_sample_is_capped_per_generation_rather_than_scanning_the_table()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedSessionsAsync(context, [.. Enumerable.Range(0, 12).Select(i => ($"s{i:D2}", "t1", 1))]);

        var report = await new StatePreflight(new SqlStatePreflightReader(context.StoreContext)).RunAsync(samplePerGeneration: 3);

        report.Sessions.ShouldHaveSingleItem().RecordCount.ShouldBe(12, "the COUNT still covers every row");
        report.SampledCount.ShouldBe(3, "the decode must not walk the whole table");
    }

    [Fact]
    public async Task An_unstamped_checkpoint_is_counted_under_its_own_generation()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        await SeedCheckpointAsync(context, "stamped", "t1", generation: 1);
        await SeedCheckpointAsync(context, "legacy", "t1", generation: null);

        var report = await new StatePreflight(new SqlStatePreflightReader(context.StoreContext)).RunAsync(samplePerGeneration: 5);

        report.Checkpoints.Count.ShouldBe(2);
        report.Checkpoints.ShouldContain(static generation => generation.SchemaGeneration == null && generation.ReadableByThisBuild);
        report.HasUnreadableGeneration.ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_database_reports_no_generations_and_no_error()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);

        var report = await new StatePreflight(new SqlStatePreflightReader(context.StoreContext)).RunAsync(samplePerGeneration: 5);

        report.Sessions.ShouldBeEmpty();
        report.Checkpoints.ShouldBeEmpty();
        report.IsClean.ShouldBeTrue();
    }

    [Fact]
    public async Task An_encrypted_session_is_reported_as_structure_only_not_as_a_failure()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var protectingContext = ProtectingStoreContext.Build(
            context, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });
        var sessions = new SqlSessionStore(protectingContext, context.TenantContext);
        await sessions.SaveAsync(TestData.Session("encrypted"));

        // 🚨 Read through a context carrying NullContentProtector, NOT a null
        // one. That is what AddTracon() registers, and it is what the CLI
        // therefore has. The difference is not cosmetic: a null protector makes
        // ProtectedValue.Read short-circuit and hand back the envelope, while
        // NullContentProtector.Unprotect THROWS on an envelope. An earlier
        // version of this test used the plain context and stayed green while
        // `tracon state-check` crashed against the real sample database.
        var keylessContext = ProtectingStoreContext.Keyless(context);

        var report = await new StatePreflight(new SqlStatePreflightReader(keylessContext)).RunAsync(samplePerGeneration: 5);

        report.SampleFailures.ShouldBeEmpty("a row this process cannot decrypt is not a row the application cannot read");
        report.StructureOnlySampleCount.ShouldBe(1);
        report.DecodedSampleCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_key_that_no_longer_resolves_is_structure_only_too_rather_than_a_crash()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var writer = ProtectingStoreContext.Build(
            context, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });
        await new SqlSessionStore(writer, context.TenantContext).SaveAsync(TestData.Session("rotated"));

        // A protector that IS configured, but not with the key this row was
        // written under - a rotated or retired key id. It throws the same way
        // the keyless one does, and means the same thing here: this process
        // cannot open the row, which says nothing about whether the
        // application can.
        var otherKeyContext = ProtectingStoreContext.Build(
            context, "k2", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });

        var report = await new StatePreflight(new SqlStatePreflightReader(otherKeyContext)).RunAsync(samplePerGeneration: 5);

        report.SampleFailures.ShouldBeEmpty();
        report.StructureOnlySampleCount.ShouldBe(1);
    }

    private static async Task SeedSessionsAsync(
        SqliteTestContext context,
        params (string Id, string TenantId, int Generation)[] rows)
    {
        foreach (var row in rows)
        {
            await context.Sessions.SaveAsync(TestData.Session(row.Id) with
            {
                TenantId = row.TenantId,
                StateSchemaVersion = row.Generation,
                StateMafVersion = "1.18.0",
            });
        }
    }

    private static async Task SeedCheckpointAsync(
        SqliteTestContext context,
        string checkpointId,
        string tenantId,
        int? generation)
    {
        await context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SessionId = "session-1",
            CheckpointId = checkpointId,
            CreatedAt = DateTimeOffset.UtcNow,
            State = TestData.State("""{"step":1}"""),
            StateSchemaVersion = generation,
            StateMafVersion = generation is null ? null : "1.18.0",
        });
    }

    /// <summary>Dumps every column of both state tables, ordered, as one string.</summary>
    /// <param name="context">The test context.</param>
    /// <returns>The snapshot.</returns>
    private static async Task<string> SnapshotAsync(SqliteTestContext context)
    {
        var sessions = await context.ScalarAsync<string>($"""
            SELECT COALESCE(group_concat(row, char(10)), '')
            FROM (
                SELECT id || '|' || tenant_id || '|' || agent_name || '|' || state || '|' || state_schema_version
                       || '|' || COALESCE(state_maf_version, '') || '|' || created_at || '|' || updated_at
                       || '|' || version || '|' || COALESCE(owner_id, '') AS row
                FROM {context.TablePrefix}sessions
                ORDER BY id
            );
            """);

        var checkpoints = await context.ScalarAsync<string>($"""
            SELECT COALESCE(group_concat(row, char(10)), '')
            FROM (
                SELECT id || '|' || tenant_id || '|' || session_id || '|' || checkpoint_id
                       || '|' || COALESCE(parent_id, '') || '|' || COALESCE(run_id, '') || '|' || state
                       || '|' || created_at || '|' || COALESCE(state_schema_version, '')
                       || '|' || COALESCE(state_maf_version, '') AS row
                FROM {context.TablePrefix}workflow_checkpoints
                ORDER BY id
            );
            """);

        return $"sessions:\n{sessions}\ncheckpoints:\n{checkpoints}";
    }
}
