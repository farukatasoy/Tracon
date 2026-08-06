namespace AgentPrism.Sqlite.IntegrationTests;

/// <summary>
/// <see cref="SqlDialect.BuildRetentionFindNthRowCutoffSql"/>'in SQLite SQL
/// uretiminin testleri (Faz 36, <c>MaxRows</c>). Canli veritabani gerektirmez —
/// <see cref="SqliteDialect"/> yalniz SQL metni kurar.
/// </summary>
public sealed class RetentionMaxRowsDialectTests : IDisposable
{
    private readonly SqliteDialect _dialect = new("t_ab12cd34");

    [Fact]
    public void Uretilen_sql_limit_offset_ve_null_elemeyi_tasir()
    {
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql("t_ab12cd34run_events", "created_at");

        sql.ShouldContain("SELECT created_at");
        sql.ShouldContain("FROM t_ab12cd34run_events");
        sql.ShouldContain("WHERE created_at IS NOT NULL");
        sql.ShouldContain("ORDER BY created_at DESC");
        sql.ShouldContain("LIMIT 1 OFFSET @n - 1");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Her_hedef_registry_ustunden_hatasiz_cozulur(string target)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);

        definition.RowLimitOrderExpression.ShouldNotBeNullOrWhiteSpace();

        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(definition.Table, definition.RowLimitOrderExpression);

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
