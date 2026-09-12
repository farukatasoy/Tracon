using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The <c>runs_v1</c> read contract view (Phase 111, 111.5 "column set gate"):
/// a published column is never lost or renamed. Adding a column is free.
/// </summary>
/// <remarks>
/// Reads the embedded migration SQL TEXT of the "views" optional set directly
/// (no database, same pattern as <c>SqlTextSnapshotTests</c>) and checks the
/// column list between the <c>-- runs_v1: columns BEGIN/END</c> markers that
/// every provider's <c>0001_read_views.sql</c> carries.
/// </remarks>
public sealed class ReadViewColumnSetTests
{
    /// <summary>
    /// The published contract (docs/arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md,
    /// 111.2). A column dropped from here is a BREAKING change and ships as
    /// <c>runs_v2</c>, never edited in place.
    /// </summary>
    private static readonly string[] ExpectedColumns =
    [
        "run_id",
        "tenant_id",
        "agent_name",
        "session_id",
        "status",
        "status_name",
        "started_at",
        "completed_at",
        "is_streaming",
        "input_tokens",
        "output_tokens",
        "cached_input_tokens",
        "reasoning_tokens",
        "total_tokens",
        "input_cost",
        "output_cost",
        "cached_input_cost",
        "total_cost",
        "cost_currency",
        "error_type",
        "model_provider",
        "input_price_per_mtok",
        "output_price_per_mtok",
        "cached_input_price_per_mtok",
    ];

    /// <summary>
    /// Raw column names underlying every <c>ProtectedColumn</c> (Security/ProtectedColumn.cs).
    /// Hand-maintained against that enum's XML docs; a genuine collision here
    /// (not just this list going stale) is what the test guards against.
    /// </summary>
    private static readonly string[] ProtectedRawColumnNames =
    [
        "state", "item", "messages", "text", "payload", "arguments", "result", "content",
    ];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public void PostgreSql_runs_v1_carries_every_published_column()
        => AssertColumnSet(typeof(PostgresQueries).Assembly, "Tracon.PostgreSql.MigrationsViews.");

    [Fact]
    public void SqlServer_runs_v1_carries_every_published_column()
        => AssertColumnSet(typeof(SqlServerQueries).Assembly, "Tracon.SqlServer.MigrationsViews.");

    [Fact]
    public void Sqlite_runs_v1_carries_every_published_column()
        => AssertColumnSet(typeof(SqliteQueries).Assembly, "Tracon.Sqlite.MigrationsViews.");

    [Fact]
    public void PostgreSql_runs_v1_carries_no_protected_column()
        => AssertNoProtectedColumn(typeof(PostgresQueries).Assembly, "Tracon.PostgreSql.MigrationsViews.");

    [Fact]
    public void SqlServer_runs_v1_carries_no_protected_column()
        => AssertNoProtectedColumn(typeof(SqlServerQueries).Assembly, "Tracon.SqlServer.MigrationsViews.");

    [Fact]
    public void Sqlite_runs_v1_carries_no_protected_column()
        => AssertNoProtectedColumn(typeof(SqliteQueries).Assembly, "Tracon.Sqlite.MigrationsViews.");

    private static void AssertColumnSet(Assembly assembly, string resourcePrefix)
    {
        var columnsBlock = ReadColumnsBlock(assembly, resourcePrefix);

        foreach (var column in ExpectedColumns)
        {
            Regex.IsMatch(columnsBlock, $@"\b{Regex.Escape(column)}\b", RegexOptions.None, RegexTimeout).ShouldBeTrue(
                $"runs_v1 must publish column '{column}' -- a published view column is never dropped " +
                "or renamed (111.1); a breaking change ships as runs_v2 instead.");
        }
    }

    private static void AssertNoProtectedColumn(Assembly assembly, string resourcePrefix)
    {
        var columnsBlock = ReadColumnsBlock(assembly, resourcePrefix);

        foreach (var protectedColumn in ProtectedRawColumnNames)
        {
            Regex.IsMatch(columnsBlock, $@"\b{Regex.Escape(protectedColumn)}\b", RegexOptions.None, RegexTimeout).ShouldBeFalse(
                $"runs_v1 must never publish protected column '{protectedColumn}' -- a read contract " +
                "view carries metadata and derived values only (111.1).");
        }
    }

    private static string ReadColumnsBlock(Assembly assembly, string resourcePrefix)
        => ReadViewSqlText.ExtractMarkedBlock(assembly, resourcePrefix, "runs_v1: columns BEGIN", "runs_v1: columns END");
}
