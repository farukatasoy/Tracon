using System.Reflection;
using System.Text.RegularExpressions;

namespace AgentPrism.SqlProviders.Tests;

/// <summary>
/// The <c>runs_v1</c> read contract view's <c>status_name</c> column (Phase 111):
/// a hand-written <c>CASE</c> mapping the numeric <c>status</c> to its
/// <see cref="RunStatus"/> name. The mapping is safe only as long as it
/// actually names every <see cref="RunStatus"/> value.
/// </summary>
/// <remarks>
/// Same shape as <see cref="ReadViewCostTermTests"/>: a future phase can
/// append a new <see cref="RunStatus"/> value (its numeric values are stable,
/// so it is always appended, never inserted) and forget to append the
/// matching branch inside the <c>-- status_name: BEGIN/END</c> block of all
/// three providers' <c>0001_read_views.sql</c>. Without this test that
/// mismatch reaches production silently: the new status reads back as
/// <c>NULL</c> in <c>runs_v1</c> instead of failing a build.
/// </remarks>
public sealed class ReadViewStatusNameTests
{
    [Fact]
    public void PostgreSql_status_name_names_every_RunStatus_value()
        => AssertStatusNames(typeof(PostgresQueries).Assembly, "AgentPrism.PostgreSql.MigrationsViews.");

    [Fact]
    public void SqlServer_status_name_names_every_RunStatus_value()
        => AssertStatusNames(typeof(SqlServerQueries).Assembly, "AgentPrism.SqlServer.MigrationsViews.");

    [Fact]
    public void Sqlite_status_name_names_every_RunStatus_value()
        => AssertStatusNames(typeof(SqliteQueries).Assembly, "AgentPrism.Sqlite.MigrationsViews.");

    private static void AssertStatusNames(Assembly assembly, string resourcePrefix)
    {
        var statusNameBlock = ReadViewSqlText.ExtractMarkedBlock(
            assembly, resourcePrefix, "status_name: BEGIN", "status_name: END");

        foreach (var statusName in Enum.GetNames<RunStatus>())
        {
            Regex.IsMatch(statusNameBlock, $@"\b{Regex.Escape(statusName)}\b", RegexOptions.None, TimeSpan.FromSeconds(2))
                .ShouldBeTrue(
                    $"runs_v1.status_name must map RunStatus.{statusName} -- a status value with no branch " +
                    "in this CASE expression reads back as NULL instead of its name.");
        }
    }
}
