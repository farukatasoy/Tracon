namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Tests for <see cref="SqlDialect.BuildRetentionFindNthRowCutoffSql"/>'s
/// PostgreSQL SQL generation (Phase 36, <c>MaxRows</c>). Requires no live
/// database — <see cref="PostgresDialect"/> only builds SQL text.
/// </summary>
public sealed class RetentionMaxRowsDialectTests
{
    private readonly PostgresDialect _dialect = new("tracon");

    [Fact]
    public void Generated_sql_carries_offset_limit_and_null_filtering()
    {
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql("tracon.run_events", "created_at", extraPredicate: null);

        sql.ShouldContain("SELECT created_at");
        sql.ShouldContain("FROM tracon.run_events");
        sql.ShouldContain("WHERE created_at IS NOT NULL");
        sql.ShouldContain("ORDER BY created_at DESC");
        sql.ShouldContain("OFFSET @n - 1");
        sql.ShouldContain("LIMIT 1");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Every_target_resolves_without_error_via_the_registry(string target)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);

        definition.RowLimitOrderExpression.ShouldNotBeNullOrWhiteSpace();

        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(definition.Table, definition.RowLimitOrderExpression, extraPredicate: null);

        sql.ShouldContain("OFFSET @n - 1");
    }

    public static TheoryData<string> AllTargets()
    {
        var data = new TheoryData<string>();

        foreach (var target in RetentionTargets.All)
        {
            data.Add(target);
        }

        return data;
    }
}
