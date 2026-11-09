using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// The collection of every two-node class: it runs the classes one at a time and gives them one database server per
/// provider.
/// </summary>
/// <remarks>
/// <para>
/// One class at a time: each test starts two hosts, and four hosts on one machine stretch the very clocks the scenarios
/// wait on. The cancellation sweep also reads the whole server for a session that is still inside a transaction or that
/// holds a lock (<see cref="SqlSweepBackend"/>); a test of another class running at that moment would read as a leak.
/// </para>
/// <para>
/// One server per provider: the PostgreSQL and the SQL Server container start once, when the collection starts, and
/// every class of the collection uses them. When each class started containers of its own, the module started eighteen.
/// Each test still takes a schema of its own (<see cref="TwoNodeServer.NewDatabase"/>), so no row of one test is read by
/// another.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TwoNodeAcceptanceSerial
    : ICollectionFixture<PostgreSqlTwoNodeServer>, ICollectionFixture<SqlServerTwoNodeServer>
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Two-node acceptance";
}
