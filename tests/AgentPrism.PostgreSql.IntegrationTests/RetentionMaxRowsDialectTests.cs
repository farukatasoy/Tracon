namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="SqlDialect.BuildRetentionFindNthRowCutoffSql"/>'in PostgreSQL SQL
/// uretiminin testleri (Faz 36, <c>MaxRows</c>). Canli veritabani gerektirmez —
/// <see cref="PostgresDialect"/> yalniz SQL metni kurar.
/// </summary>
public sealed class RetentionMaxRowsDialectTests
{
    private readonly PostgresDialect _dialect = new("agentprism");

    [Fact]
    public void Uretilen_sql_offset_limit_ve_null_elemeyi_tasir()
    {
        var sql = _dialect.BuildRetentionFindNthRowCutoffSql("agentprism.run_events", "created_at", extraPredicate: null);

        sql.ShouldContain("SELECT created_at");
        sql.ShouldContain("FROM agentprism.run_events");
        sql.ShouldContain("WHERE created_at IS NOT NULL");
        sql.ShouldContain("ORDER BY created_at DESC");
        sql.ShouldContain("OFFSET @n - 1");
        sql.ShouldContain("LIMIT 1");
    }

    [Theory]
    [MemberData(nameof(AllTargets))]
    public void Her_hedef_registry_ustunden_hatasiz_cozulur(string target)
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
