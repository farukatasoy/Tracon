using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Runs the read-only state preflight against a real PostgreSQL database
/// (Phase 156).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The SQLite suite is not enough for this surface, and that is a measured
/// fact rather than a precaution. <c>workflow_checkpoints.id</c> is
/// <c>uuid</c> here and <c>uniqueidentifier</c> on SQL Server — both come back
/// as a boxed <see cref="Guid"/> — while SQLite stores it as <c>TEXT</c>. The
/// first version of <c>SqlStatePreflightReader</c> read it with
/// <c>GetString</c>, which is green on SQLite and throws
/// <see cref="InvalidCastException"/> here; the phase audit caught it, no
/// test did.
/// </para>
/// <para>
/// The four preflight queries also live in <c>BuildSharedQueries</c>, so they
/// are ONE text for three providers. A snapshot test pins that text; only a
/// real database proves the text runs — window function, <c>CAST(... AS
/// BIGINT)</c> and all.
/// </para>
/// </remarks>
public sealed class StatePreflightTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Sessions_and_checkpoints_are_both_read_on_this_provider()
    {
        await _context.Sessions.SaveAsync(TestData.Session("s1") with
        {
            TenantId = "t1",
            StateSchemaVersion = 1,
            StateMafVersion = "1.18.0",
        });

        await _context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            SessionId = "session-1",
            CheckpointId = "cp-1",
            CreatedAt = DateTimeOffset.UtcNow,
            State = TestData.State("""{"step":1}"""),
            StateSchemaVersion = 1,
            StateMafVersion = "1.18.0",
        });

        var report = await new StatePreflight(new SqlStatePreflightReader(_context.StoreContext))
            .RunAsync(samplePerGeneration: 5);

        report.Sessions.ShouldHaveSingleItem().RecordCount.ShouldBe(1);

        // The assertion the SQLite suite structurally cannot make: reading a
        // sampled checkpoint row here goes through a Guid-typed id column.
        report.Checkpoints.ShouldHaveSingleItem().RecordCount.ShouldBe(1);
        report.SampledCount.ShouldBe(2);
        report.SampleFailures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Counting_covers_every_tenant_on_this_provider()
    {
        foreach (var tenant in (string[])["t1", "t2", "t3"])
        {
            await _context.Sessions.SaveAsync(TestData.Session($"s-{tenant}") with
            {
                TenantId = tenant,
                StateSchemaVersion = 1,
            });
        }

        var report = await new StatePreflight(new SqlStatePreflightReader(_context.StoreContext))
            .RunAsync(samplePerGeneration: 0);

        // Every application-facing read on this store filters by tenant; this
        // one must not, or an upgrade sees a third of its own risk.
        report.Sessions.ShouldHaveSingleItem().RecordCount.ShouldBe(3);
    }

    [Fact]
    public async Task The_check_writes_nothing_on_this_provider()
    {
        await _context.Sessions.SaveAsync(TestData.Session("s1") with { TenantId = "t1", StateSchemaVersion = 1 });
        await _context.WorkflowCheckpoints.CreateAsync(new WorkflowCheckpointRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            SessionId = "session-1",
            CheckpointId = "cp-1",
            CreatedAt = DateTimeOffset.UtcNow,
            State = TestData.State("""{"step":1}"""),
            StateSchemaVersion = 1,
        });

        var before = await SnapshotAsync();
        before.ShouldNotBeEmpty("a snapshot that came back empty would pass this test for the wrong reason");

        await new StatePreflight(new SqlStatePreflightReader(_context.StoreContext)).RunAsync(samplePerGeneration: 5);

        (await SnapshotAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task A_generation_this_build_cannot_read_is_counted_and_left_untouched()
    {
        await _context.Sessions.SaveAsync(TestData.Session("current") with { TenantId = "t1", StateSchemaVersion = 1 });
        await _context.Sessions.SaveAsync(TestData.Session("future") with { TenantId = "t1", StateSchemaVersion = 99 });

        var report = await new StatePreflight(new SqlStatePreflightReader(_context.StoreContext))
            .RunAsync(samplePerGeneration: 5);

        report.HasUnreadableGeneration.ShouldBeTrue();
        report.UnreadableRecordCount.ShouldBe(1);

        var stored = await _context.ScalarAsync<int>(
            $"SELECT state_schema_version FROM {_context.SchemaName}.sessions WHERE id = 'future';");
        stored.ShouldBe(99, "an unreadable session is never rewritten or deleted");
    }

    /// <summary>Dumps every column of both state tables, ordered, as one string.</summary>
    /// <returns>The snapshot.</returns>
    private async Task<string> SnapshotAsync()
    {
        var sessions = await _context.ScalarAsync<string>($"""
            SELECT COALESCE(string_agg(row, E'\n' ORDER BY row), '')
            FROM (
                SELECT id || '|' || tenant_id || '|' || agent_name || '|' || state || '|' || state_schema_version
                       || '|' || COALESCE(state_maf_version, '') || '|' || created_at || '|' || updated_at
                       || '|' || version || '|' || COALESCE(owner_id, '') AS row
                FROM {_context.SchemaName}.sessions
            ) s;
            """);

        var checkpoints = await _context.ScalarAsync<string>($"""
            SELECT COALESCE(string_agg(row, E'\n' ORDER BY row), '')
            FROM (
                SELECT id || '|' || tenant_id || '|' || session_id || '|' || checkpoint_id
                       || '|' || COALESCE(parent_id, '') || '|' || COALESCE(run_id::text, '') || '|' || state
                       || '|' || created_at || '|' || COALESCE(state_schema_version::text, '')
                       || '|' || COALESCE(state_maf_version, '') AS row
                FROM {_context.SchemaName}.workflow_checkpoints
            ) c;
            """);

        return $"sessions:\n{sessions}\ncheckpoints:\n{checkpoints}";
    }
}
