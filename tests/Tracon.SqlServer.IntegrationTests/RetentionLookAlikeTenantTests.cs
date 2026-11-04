using System.Globalization;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A retention sweep for one tenant never reaches the rows of a tenant whose name only looks the same to the database.
/// </summary>
/// <remarks>
/// <para>
/// The default collation of a SQL Server database ignores case and width, so <c>tenant_id = @tenant_id</c> is true for
/// <c>acme</c> and for the same name written with a full-width <c>a</c>. The stores never write such a name (the tenant
/// alphabet is closed), but a row from an earlier release or from a hand-run script can hold one, and the migration guard
/// only looks when the migrations run. The rows are written here with raw SQL for that reason.
/// </para>
/// </remarks>
public sealed class RetentionLookAlikeTenantTests(SqlServerFixture fixture)
{
    // The first letter is U+FF41, the full-width "a". The default collation compares it equal to the ASCII "a".
    private const string LookAlike = "ａcme";

    [Fact]
    public async Task A_direct_delete_for_one_tenant_leaves_the_rows_of_a_look_alike_tenant()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        var own = await SeedFinishedJobAsync(context, "acme", old);
        var other = await SeedFinishedJobAsync(context, LookAlike, old);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.Jobs, "acme", cutoff)).ShouldBe(1);
        (await context.RetentionData.DeleteBatchAsync(RetentionTargets.Jobs, "acme", cutoff, batchSize: 100)).ShouldBe(1);

        (await JobExistsAsync(context, own)).ShouldBeFalse("the tenant's own row is swept");
        (await JobExistsAsync(context, other)).ShouldBeTrue("a row of another tenant is not swept for acme");
    }

    [Fact]
    public async Task A_delete_of_sessions_for_one_tenant_leaves_the_rows_of_a_look_alike_tenant()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        var own = await SeedOldSessionAsync(context, "acme", old);
        var other = await SeedOldSessionAsync(context, LookAlike, old);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

        (await context.RetentionData.CountOlderThanAsync(RetentionTargets.Sessions, "acme", cutoff)).ShouldBe(1);
        (await context.RetentionData.DeleteBatchAsync(RetentionTargets.Sessions, "acme", cutoff, batchSize: 100)).ShouldBe(1);

        (await SessionExistsAsync(context, own)).ShouldBeFalse("the tenant's own row is swept");
        (await SessionExistsAsync(context, other)).ShouldBeTrue("a row of another tenant is not swept for acme");
    }

    private static async Task<Guid> SeedFinishedJobAsync(SqlServerTestContext context, string tenantId, DateTimeOffset completedAt)
    {
        var job = await context.Jobs.EnqueueAsync(TestData.Job("seed"), ["a"]);

        // Raw SQL: no store writes a tenant name outside the alphabet.
        await context.ExecuteAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"UPDATE [{context.SchemaName}].jobs SET tenant_id = N'{tenantId}', status = 3, completed_at = '{completedAt:O}' WHERE id = '{job.Id}';"));

        return job.Id;
    }

    private static async Task<bool> JobExistsAsync(SqlServerTestContext context, Guid id)
        => await context.ScalarAsync<int>(string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [{context.SchemaName}].jobs WHERE id = '{id}';")) == 1;

    private static async Task<string> SeedOldSessionAsync(SqlServerTestContext context, string tenantId, DateTimeOffset updatedAt)
    {
        var id = $"session-{Guid.NewGuid():N}";

        await context.Sessions.SaveAsync(TestData.Session(id));
        await context.ExecuteAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"UPDATE [{context.SchemaName}].sessions SET tenant_id = N'{tenantId}', updated_at = '{updatedAt:O}' WHERE id = N'{id}';"));

        return id;
    }

    private static async Task<bool> SessionExistsAsync(SqlServerTestContext context, string id)
        => await context.ScalarAsync<int>(string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [{context.SchemaName}].sessions WHERE id = N'{id}';")) == 1;
}
