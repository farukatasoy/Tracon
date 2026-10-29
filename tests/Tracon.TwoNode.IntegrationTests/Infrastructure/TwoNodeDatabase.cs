namespace Tracon.TwoNode.IntegrationTests.Infrastructure;

/// <summary>
/// One schema on a shared server, as the two hosts of a test see it: how each of them is pointed at it.
/// </summary>
public abstract class TwoNodeDatabase
{
    /// <summary>Points a host's Tracon chain at the database.</summary>
    /// <param name="builder">The chain of one host.</param>
    public abstract void Use(ITraconBuilder builder);
}

/// <summary>A schema on the PostgreSQL container.</summary>
/// <param name="connectionString">The container's connection string.</param>
/// <param name="schemaName">The schema this test owns.</param>
internal sealed class PostgreSqlTwoNodeDatabase(string connectionString, string schemaName) : TwoNodeDatabase
{
    /// <inheritdoc />
    public override void Use(ITraconBuilder builder)
        => builder.UsePostgreSql(options =>
        {
            options.ConnectionString = connectionString;
            options.SchemaName = schemaName;
        });
}

/// <summary>A schema on the SQL Server container.</summary>
/// <param name="connectionString">The container's connection string.</param>
/// <param name="schemaName">The schema this test owns.</param>
internal sealed class SqlServerTwoNodeDatabase(string connectionString, string schemaName) : TwoNodeDatabase
{
    /// <inheritdoc />
    public override void Use(ITraconBuilder builder)
        => builder.UseSqlServer(options =>
        {
            options.ConnectionString = connectionString;
            options.SchemaName = schemaName;
        });
}
