using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The parameter values a workflow was started with live only in its
/// <c>run_inputs</c> row, and answering the run resumes with them. The retention
/// target keeps the row of a run waiting for a human, on SQL Server too.
/// </summary>
/// <remarks>
/// The target's SQL is shared (<c>RetentionTargetRegistry</c>), but the row-limit
/// expression is a <c>CASE WHEN EXISTS</c> inside a T-SQL <c>ORDER BY</c>; one
/// dialect passing proves the text, not this provider's parse of it.
/// </remarks>
public sealed class RunInputRetentionTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task A_waiting_workflow_runs_input_row_survives_retention()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var waiting = await SeedRunAsync(context, RunStatus.AwaitingInput, old);
        var finished = await SeedRunAsync(context, RunStatus.Completed, old);

        await SeedRunInputAsync(context, waiting, old);
        await SeedRunInputAsync(context, finished, old);

        // Two rows exist; only the finished run's one counts, so a limit of two
        // is not reached.
        (await context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.RunInputs, tenantId: null, maxRows: 2))
            .ShouldBeNull("a waiting run's input is not counted toward the row limit");

        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.RunInputs, tenantId: null, cutoff)).ShouldBe(1);
        (await context.RetentionData.DeleteBatchAsync(RetentionTargets.RunInputs, tenantId: null, cutoff, batchSize: 100)).ShouldBe(1);

        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.run_inputs WHERE run_id = '{waiting}';"))
            .ShouldBe(1);
        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {context.SchemaName}.run_inputs WHERE run_id = '{finished}';"))
            .ShouldBe(0);
    }

    private static async Task<Guid> SeedRunAsync(SqlServerTestContext context, RunStatus status, DateTimeOffset completedAt)
    {
        var id = Guid.NewGuid();

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.runs
                (id, tenant_id, agent_name, status, started_at, completed_at, is_streaming, event_count)
            VALUES
                ('{id}', 'default', 'support', {(int)status}, SYSDATETIMEOFFSET(), '{completedAt.UtcDateTime:O}', 0, 0);
            """);

        return id;
    }

    /// <summary>A parameter map as JSON. Avoids brace-escaping inside raw SQL.</summary>
    private const string ParametersJson = "{\"tone\":\"formal\"}";

    private static async Task SeedRunInputAsync(SqlServerTestContext context, Guid runId, DateTimeOffset createdAt)
        => await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.run_inputs (run_id, tenant_id, messages, created_at, parameters)
            VALUES ('{runId}', 'default', N'[]', '{createdAt.UtcDateTime:O}', N'{ParametersJson}');
            """);
}
