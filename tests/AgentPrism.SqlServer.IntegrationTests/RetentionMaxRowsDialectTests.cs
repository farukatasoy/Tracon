namespace AgentPrism.SqlServer.IntegrationTests;

/// <summary>
/// <see cref="SqlDialect.BuildRetentionFindNthRowCutoffSql"/>'in SQL Server SQL
/// uretiminin testleri (Faz 36, <c>MaxRows</c>). Canli veritabani gerektirmez —
/// <see cref="SqlServerDialect"/> yalniz SQL metni kurar.
/// </summary>
public sealed class RetentionMaxRowsDialectTests
{
    private readonly SqlServerDialect _dialect = new("agentprism");

    /// <summary>
    /// 🚨 K-026 tuzagi: SQL Server'da <c>OFFSET</c>/<c>FETCH</c>, <c>ORDER BY</c>
    /// OLMADAN hata verir. Bu sablon her zaman bir <c>ORDER BY</c> tasimalidir.
    /// </summary>
    [Fact]
    public void Uretilen_sql_order_by_offset_fetch_ve_null_elemeyi_tasir()
    {
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql("agentprism.run_events", "created_at");

        sql.ShouldContain("SELECT created_at");
        sql.ShouldContain("FROM agentprism.run_events");
        sql.ShouldContain("WHERE created_at IS NOT NULL");
        sql.ShouldContain("ORDER BY created_at DESC");
        sql.ShouldContain("OFFSET (@n - 1) ROWS FETCH NEXT 1 ROWS ONLY");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Her_hedef_registry_ustunden_hatasiz_cozulur(string target)
    {
        var definition = RetentionTargetRegistry.Resolve(_dialect, target);

        definition.RowLimitOrderExpression.ShouldNotBeNullOrWhiteSpace();

        var sql = _dialect.BuildRetentionFindNthRowCutoffSql(definition.Table, definition.RowLimitOrderExpression);

        sql.ShouldContain("ORDER BY");
        sql.ShouldContain("OFFSET (@n - 1) ROWS FETCH NEXT 1 ROWS ONLY");
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
