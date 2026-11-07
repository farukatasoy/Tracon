using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.UpgradeGate;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A catalog read that runs without shared locks gives its connection back at the default level.
/// </summary>
/// <remarks>
/// The test classes draw from one connection pool. A read that sets <c>READ UNCOMMITTED</c> and does not undo it hands the next
/// command of any class a connection that takes no shared locks, and a run read on it can miss a row that a write moves at that
/// moment. The failure showed only in a full run, so the check here is the cause: the level of the next pooled connection.
/// </remarks>
public sealed class CatalogReadIsolationTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const string LevelOfThisConnection = "SELECT CAST(transaction_isolation_level AS int) FROM sys.dm_exec_sessions WHERE session_id = @@SPID;";

    private const int ReadCommitted = 2;

    [Fact]
    public async Task The_catalog_read_of_the_test_context_leaves_the_pooled_connection_at_read_committed()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await schema.Context.CatalogScalarAsync<int>(LevelOfThisConnection)).ShouldBe(1, "the read itself runs without shared locks");
            (await schema.Context.ScalarAsync<int>(LevelOfThisConnection)).ShouldBe(ReadCommitted, "the next command must not inherit the level of the read");
        }
    }

    [Fact]
    public async Task The_catalog_read_of_the_upgrade_harness_leaves_the_pooled_connection_at_read_committed()
    {
        var read = UpgradeHarness.ReadUncommitted(LevelOfThisConnection);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await schema.Context.ScalarAsync<int>(read)).ShouldBe(1, "the read itself runs without shared locks");
            (await schema.Context.ScalarAsync<int>(LevelOfThisConnection)).ShouldBe(ReadCommitted, "the next command must not inherit the level of the read");
        }
    }

    [Fact]
    public void A_read_with_single_quotes_is_sent_whole()
    {
        UpgradeHarness.ReadUncommitted("SELECT N'a''b';")
            .ShouldBe("EXEC sp_executesql N'SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED; SELECT N''a''''b'';';");
    }
}
