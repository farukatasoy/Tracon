using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The width the audit trail shortens its labels to is the width of the columns that hold them.
/// </summary>
/// <remarks>
/// The audit log shortens an actor, an action or an entity that is longer than its column instead of failing the write
/// (<c>SqlText.Fit</c>). The number it shortens to lives in the dialect and the width of the column lives in a migration; this reads
/// the migrated schema, so a migration that changes one of them without the other fails here and not in a production
/// write that is lost.
/// </remarks>
public sealed class AuditLabelWidthTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    [Fact]
    public async Task The_dialects_limit_is_the_width_of_every_label_column_of_the_audit_log()
    {
        var context = await schema.ResetAsync(tenantId: "default");

        var widths = await context.CatalogScalarAsync<string>(
            "SELECT STRING_AGG(CAST(c.name + N'=' + CAST(c.max_length / 2 AS nvarchar(10)) AS nvarchar(max)), N';') " +
            "FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id " +
            $"WHERE SCHEMA_NAME(t.schema_id) = N'{context.SchemaName}' AND t.name = N'audit_log' " +
            "AND c.name IN (N'actor', N'action', N'entity');");

        var limit = new SqlServerDialect("dbo").AuditLabelLimit;

        (widths ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ShouldBe([$"action={limit}", $"actor={limit}", $"entity={limit}"]);
    }
}
