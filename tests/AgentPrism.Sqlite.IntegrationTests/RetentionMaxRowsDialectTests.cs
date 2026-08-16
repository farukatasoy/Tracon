namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// Tests for <see cref="SqlDialect.BuildRetentionFindNthRowCutoffSql"/>'s
/// SQLite SQL generation (Phase 36, <c>MaxRows</c>). Requires no live database
/// — <see cref="SqliteDialect"/> only builds SQL text.
/// </summary>
public sealed class RetentionMaxRowsDialectTests : IDisposable
{
    private readonly SqliteDialect _dialect = new("t_ab12cd34");

    [Fact]
    public void Generated_sql_carries_limit_offset_and_null_filtering()
    {
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql("t_ab12cd34run_events", "created_at", extraPredicate: null);

        sql.ShouldContain("SELECT created_at");
        sql.ShouldContain("FROM t_ab12cd34run_events");
        sql.ShouldContain("WHERE created_at IS NOT NULL");
        sql.ShouldContain("ORDER BY created_at DESC");
        sql.ShouldContain("LIMIT 1 OFFSET @n - 1");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Every_target_resolves_without_error_via_the_registry(string target)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);

        definition.RowLimitOrderExpression.ShouldNotBeNullOrWhiteSpace();

        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(definition.Table, definition.RowLimitOrderExpression, extraPredicate: null);

        sql.ShouldContain("LIMIT 1 OFFSET @n - 1");
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

    /// <inheritdoc />
    public void Dispose() => _dialect.Dispose();
}
