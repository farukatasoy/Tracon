using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The <c>workflow_requests</c> retention target against a real SQLite database: only a
/// CLOSED row (the request expired) goes by age; an open row is the request's entry in the inbox
/// and is neither deleted nor counted toward the row limit.
/// </summary>
/// <remarks>
/// The target's SQL is shared (<c>RetentionTargetRegistry</c>), but the row-limit expression is a
/// <c>CASE WHEN</c> inside an <c>ORDER BY</c> and the key is two columns; one dialect passing proves
/// the text, not this provider's parse of it.
/// </remarks>
public sealed class WorkflowRequestRetentionTests(SqliteFixture fixture)
{
    [Fact]
    public async Task A_closed_row_goes_by_age_and_an_open_row_never_does()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var tenantId = context.TenantContext.TenantId;
        var expired = await SeedRunAsync(context, tenantId);
        var waiting = await SeedRunAsync(context, tenantId);
        var requestedAt = DateTimeOffset.UtcNow.AddDays(-60);

        await context.WorkflowRequests.AddAsync(
        [
            Row(expired, tenantId, requestedAt, expiresAt: requestedAt.AddHours(1)),
            Row(waiting, tenantId, requestedAt, expiresAt: null),
        ]);

        // Closed 59 days ago.
        (await context.WorkflowRequests.ExpireAsync(requestedAt.AddDays(1), max: 10)).ShouldHaveSingleItem().RunId.ShouldBe(expired);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.WorkflowRequests, tenantId: null, cutoff)).ShouldBe(1);
        (await context.RetentionData.DeleteBatchAsync(RetentionTargets.WorkflowRequests, tenantId: null, cutoff, batchSize: 100)).ShouldBe(1);

        (await context.WorkflowRequests.ListByRunAsync(expired)).ShouldBeEmpty();

        var kept = (await context.WorkflowRequests.ListByRunAsync(waiting)).ShouldHaveSingleItem();

        kept.Status.ShouldBe(WorkflowRequestStatus.Open);
    }

    [Fact]
    public async Task A_row_closed_after_the_cutoff_stays()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var tenantId = context.TenantContext.TenantId;
        var run = await SeedRunAsync(context, tenantId);
        var requestedAt = DateTimeOffset.UtcNow.AddMinutes(-30);

        await context.WorkflowRequests.AddAsync([Row(run, tenantId, requestedAt, expiresAt: requestedAt.AddMinutes(10))]);
        await context.WorkflowRequests.ExpireAsync(DateTimeOffset.UtcNow, max: 10);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.WorkflowRequests, tenantId: null, cutoff)).ShouldBe(0);
        (await context.RetentionData.DeleteBatchAsync(RetentionTargets.WorkflowRequests, tenantId: null, cutoff, batchSize: 100)).ShouldBe(0);
        (await context.WorkflowRequests.ListByRunAsync(run)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowRequestStatus.Closed);
    }

    [Fact]
    public async Task Open_rows_are_not_counted_toward_the_row_limit()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var tenantId = context.TenantContext.TenantId;
        var requestedAt = DateTimeOffset.UtcNow.AddDays(-10);

        for (var i = 0; i < 3; i++)
        {
            var run = await SeedRunAsync(context, tenantId);

            await context.WorkflowRequests.AddAsync([Row(run, tenantId, requestedAt, expiresAt: null)]);
        }

        // Three open rows exist; none counts, so a limit of two is not reached.
        (await context.RetentionData.FindRowLimitCutoffAsync(RetentionTargets.WorkflowRequests, tenantId: null, maxRows: 2))
            .ShouldBeNull("an open row is not counted toward the row limit");
    }

    private static async Task<Guid> SeedRunAsync(SqliteTestContext context, string tenantId)
    {
        var runId = Guid.NewGuid();

        await context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "retention",
            Kind = RunKind.Workflow,
            WorkflowName = "retention",
            SessionId = $"session-{runId:N}",
            StartedAt = DateTimeOffset.UtcNow.AddDays(-61),
            Status = RunStatus.AwaitingInput,
            TenantId = tenantId,
        });

        return runId;
    }

    private static WorkflowRequestRecord Row(Guid runId, string tenantId, DateTimeOffset requestedAt, DateTimeOffset? expiresAt)
        => new()
        {
            RunId = runId,
            RequestId = $"request-{runId:N}",
            SessionId = $"session-{runId:N}",
            WorkflowName = "retention",
            PortId = "port",
            EventSequence = 3,
            RequestedAt = requestedAt,
            ExpiresAt = expiresAt,
            Status = WorkflowRequestStatus.Open,
            TenantId = tenantId,
        };
}
