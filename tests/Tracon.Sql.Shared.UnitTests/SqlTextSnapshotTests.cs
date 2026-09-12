using System.Reflection;
using System.Text;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Guards against a silent drift in the SQL text produced by the three
/// providers while phase 94 moves identical queries into a shared base class.
/// </summary>
/// <remarks>
/// <para>
/// This is a byte-for-byte snapshot, not a ratchet: the produced SQL text must
/// match the checked-in baseline exactly. A whitespace or parenthesis change
/// passes every contract test (they exercise behaviour, not text) but changes
/// what is sent to the server; this is the only gate that would catch it.
/// </para>
/// <para>
/// Deliberately placed OUTSIDE the three <c>*.IntegrationTests</c> projects:
/// each of those carries an <c>[assembly: AssemblyFixture(...)]</c> that starts
/// a Docker container for the WHOLE assembly before any test runs, including
/// one that never opens a connection. Building a <c>PostgresQueries</c>/
/// <c>SqlServerQueries</c>/<c>SqliteQueries</c> instance needs no connection at
/// all -- only <see cref="InternalsVisibleToAttribute"/> from the three
/// provider projects to this one (phase 94 open question 3, resolved in
/// favour of this option once the Docker cost of the alternative was
/// measured).
/// </para>
/// <para>
/// Refresh a baseline after a deliberate, reviewed text change:
/// <c>TRACON_SQL_SNAPSHOT_REFRESH=1 dotnet test tests/Tracon.Sql.Shared.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class SqlTextSnapshotTests
{
    private const string RefreshEnvVar = "TRACON_SQL_SNAPSHOT_REFRESH";

    /// <summary>The schema name every provider ships as its own default.</summary>
    private const string PostgresAndSqlServerSchema = "tracon";

    /// <summary>The table prefix SQLite ships as its own default (K-193: no dot).</summary>
    private const string SqliteTablePrefix = "tracon_";

    [Fact]
    public void Postgres_sql_text_matches_baseline()
        => AssertMatchesBaseline("postgres", new PostgresQueries(PostgresAndSqlServerSchema));

    [Fact]
    public void SqlServer_sql_text_matches_baseline()
        => AssertMatchesBaseline("sqlserver", new SqlServerQueries(PostgresAndSqlServerSchema));

    [Fact]
    public void Sqlite_sql_text_matches_baseline()
        => AssertMatchesBaseline("sqlite", new SqliteQueries(SqliteTablePrefix));

    private static void AssertMatchesBaseline(string dialectName, object queries)
    {
        var actual = Render(queries);
        var path = BaselinePath(dialectName);

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(path, actual);
            return;
        }

        File.Exists(path).ShouldBeTrue(
            $"No baseline at '{path}'. Generate it once with {RefreshEnvVar}=1.");
        var expected = File.ReadAllText(path);

        actual.ShouldBe(
            expected,
            $"The SQL text produced by {queries.GetType().Name} drifted from '{path}'. " +
            $"If the change is deliberate and reviewed, refresh with {RefreshEnvVar}=1.");
    }

    /// <summary>
    /// Renders every public <see cref="string"/> property declared on the
    /// dialect's own compiled <c>SqlQueriesBase</c>, one per line, sorted by
    /// name so the diff is stable and each changed query is a single line.
    /// </summary>
    private static string Render(object queries)
    {
        var properties = queries.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal);

        var builder = new StringBuilder();

        foreach (var property in properties)
        {
            var value = (string?)property.GetValue(queries) ?? string.Empty;
            builder.Append(property.Name).Append('\t').Append(Escape(value)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Collapses a query text onto a single line so the baseline stays one-line-per-query.</summary>
    private static string Escape(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private static string BaselinePath(string dialectName) => Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Sql.Shared.UnitTests",
        "Baselines",
        $"sql-text-baseline.{dialectName}.txt");

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
