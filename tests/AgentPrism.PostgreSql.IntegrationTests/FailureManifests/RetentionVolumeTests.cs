using System.Globalization;
using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.PostgreSql.IntegrationTests.FailureManifests;

/// <summary>
/// Failure manifest: <strong>a retention pass deletes at volume</strong>
/// (Phase 157).
/// </summary>
/// <remarks>
/// <para>
/// The written expectation, verified below:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       A delete takes row locks, not a table lock. Reads of the same table
///       run to completion WHILE a large delete is in flight, and so do writes
///       of rows the delete does not match.
///     </description>
///   </item>
///   <item>
///     <description>
///       The batch size is the operator's lock-duration control. A pass that
///       matches more rows than the batch size deletes exactly the batch and
///       returns, leaving the rest for the next call - which is why
///       <c>MaxRows</c> cleanup never turns into one unbounded statement.
///     </description>
///   </item>
/// </list>
/// <para>
/// 🚨 The claim is about LOCK behaviour, not speed. No duration is asserted as
/// a ceiling: on a shared machine a wall-clock ceiling is noise (Phase 116's
/// lesson). What is asserted is that concurrent work COMPLETES and returns
/// correct results, which a table-level lock would make impossible.
/// <para>
/// 🚨 <strong>The first test issues the delete itself rather than calling
/// <see cref="IRetentionStore.DeleteBatchAsync"/>.</strong> It has to: the
/// question is what happens while a delete is UNCOMMITTED, and the store owns
/// its own connection and commits before returning, so there is no way to hold
/// one open through the public API. The statement below matches the store's
/// own predicate (the same table, the same <c>created_at &lt; cutoff</c>
/// condition), so what is measured is PostgreSQL's row-lock behaviour for the
/// rows a retention pass targets - not a claim about the store's SQL text,
/// which <c>SqlTextSnapshotTests</c> pins separately. The second test does go
/// through the shipped method, and covers the batch semantics.
/// </para>
/// </para>
/// </remarks>
public sealed class RetentionVolumeTests(PostgresFixture fixture)
{
    private const int OldEventCount = 4000;

    [Fact]
    public async Task An_uncommitted_large_delete_does_not_block_reads_or_unrelated_writes()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var oldRun = await SeedRunAsync(context);
        var freshRun = await SeedRunAsync(context);

        await SeedEventsAsync(context, oldRun, OldEventCount, ageDays: 400);
        await SeedEventsAsync(context, freshRun, count: 10, ageDays: 0);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, null, cutoff))
            .ShouldBe(OldEventCount);

        // 🚨 The delete is held OPEN, uncommitted. A real retention pass
        // commits in milliseconds, so racing a committed delete would prove
        // nothing: a reader blocked by a table lock would simply wait for the
        // commit and then succeed, and the test would be green either way.
        // Holding the transaction open makes the lock question decidable - if
        // the statement took a table-level lock, every read below would block
        // until this transaction ends, and the timeout would fire.
        await using var deleting = context.DataSource.CreateConnection();
        await deleting.OpenAsync();
        await using var transaction = await deleting.BeginTransactionAsync();

        await using (var delete = deleting.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"DELETE FROM {context.SchemaName}.run_events WHERE created_at < now() - interval '30 days';";
            (await delete.ExecuteNonQueryAsync()).ShouldBe(OldEventCount);
        }

        // A SECOND connection, the way a serving process would be. Every call
        // is bounded: a blocked read fails the test instead of hanging it.
        await using var reader = PostgresTestContext.Create(fixture, schemaName);
        using var bounded = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Rows the delete does not match, and the run summary it never touches.
        (await reader.Runs.GetRunAsync(freshRun, bounded.Token)).ShouldNotBeNull();

        // Rows the delete DOES match: a reader still sees the pre-delete
        // snapshot rather than waiting for the writer (MVCC), so counting is
        // never blocked by a retention pass in flight.
        (await reader.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, null, cutoff, bounded.Token))
            .ShouldBe(OldEventCount);

        // An unrelated write lands too - retention does not freeze ingestion.
        await reader.Runs.StartRunAsync(TestData.Run(Guid.NewGuid()), bounded.Token);

        await transaction.CommitAsync();

        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, null, cutoff)).ShouldBe(0);
    }

    [Fact]
    public async Task A_pass_deletes_exactly_its_batch_and_leaves_the_rest()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName);
        await context.Migrations.ApplyAsync();

        var run = await SeedRunAsync(context);
        await SeedEventsAsync(context, run, count: 500, ageDays: 400);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        var first = await context.RetentionData.DeleteBatchAsync(RetentionTargets.RunEvents, null, cutoff, batchSize: 200);
        var remaining = await context.RetentionData.CountOlderThanAsync(RetentionTargets.RunEvents, null, cutoff);

        first.ShouldBe(200);
        remaining.ShouldBe(300);
    }

    private static async Task<Guid> SeedRunAsync(PostgresTestContext context)
    {
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(TestData.Run(runId));

        return runId;
    }

    /// <summary>
    /// Inserts the events with one statement. Volume is the point of this
    /// manifest, and 4000 round trips through the store would make the test
    /// measure the client rather than the delete.
    /// </summary>
    private static async Task SeedEventsAsync(PostgresTestContext context, Guid runId, int count, int ageDays)
        => await context.ExecuteAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"""
             INSERT INTO {context.SchemaName}.run_events (run_id, seq, type, text, created_at)
             SELECT '{runId}', seq, 3, 'delta', now() - interval '{ageDays} days'
             FROM generate_series(
                 (SELECT COALESCE(MAX(seq), 0) + 1 FROM {context.SchemaName}.run_events WHERE run_id = '{runId}'),
                 (SELECT COALESCE(MAX(seq), 0) + {count} FROM {context.SchemaName}.run_events WHERE run_id = '{runId}')) AS seq;
             """));
}
