namespace Tracon.SqlProviders.Tests;

/// <summary>
/// A store opens its transaction through <c>SqlStoreContext.ExecuteInTransactionAsync</c>,
/// the one place a deadlock victim is run again.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Regression (2026-10-13): <c>RunScoreStoreContract</c>'s concurrent-write case
/// failed once in a full-solution run with SQL Server error 1205, and the upsert gave
/// the raw error to its caller. The fix retries at the transaction boundary, which is
/// the only boundary that is safe to repeat: the server rolled the victim back whole.
/// Nine stores opened their own transaction by hand and had no retry at all.
/// </para>
/// <para>
/// A text gate, like <c>SqlServerUpsertAtomicityTests</c>: a transaction opened by
/// hand passes every contract test and fails only when a full run happens to race.
/// <c>UpsertDeadlockRetryTests</c> (SQL Server integration) proves the retry itself.
/// </para>
/// </remarks>
public sealed class SqlTransactionSiteTests
{
    /// <summary>The files that may open a transaction themselves, and why.</summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Sql.Shared/Internal/SqlStoreContext.cs"] = "the boundary itself",
        ["src/Tracon.Sql.Shared/Migrations/MigrationRunner.cs"] =
            "retries its own guarded DDL on a deadlock and a unique violation ",
        ["src/Tracon.Sql.Shared/Stores/SqlDataSubjectStore.cs"] =
            "deletes attachment content outside the database and calls back before its commit; " +
            "running that again is not safe",
        ["src/Tracon.PostgreSql/Stores/PgVectorSearchStore.cs"] =
            "writes through its own NpgsqlDataSource, not a SqlStoreContext",
    };

    [Fact]
    public void Only_the_store_context_opens_a_store_transaction()
    {
        var sites = new List<string>();

        foreach (var project in (string[])["Tracon.Sql.Shared", "Tracon.PostgreSql", "Tracon.SqlServer", "Tracon.Sqlite"])
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src", project), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

                if (relative.Contains("/obj/", StringComparison.Ordinal) || Exceptions.ContainsKey(relative))
                {
                    continue;
                }

                var lines = File.ReadAllLines(path);

                for (var index = 0; index < lines.Length; index++)
                {
                    if (lines[index].Contains("BeginTransactionAsync(", StringComparison.Ordinal)
                        && !lines[index].TrimStart().StartsWith("//", StringComparison.Ordinal))
                    {
                        sites.Add($"{relative}:{index + 1}");
                    }
                }
            }
        }

        sites.ShouldBeEmpty(
            "Open a store transaction with SqlStoreContext.ExecuteInTransactionAsync, where a deadlock " +
            "victim is run again whole. A transaction that cannot be run again needs an entry in Exceptions " +
            "saying why.");
    }

    [Fact]
    public void Every_exception_still_opens_a_transaction()
    {
        // A stale entry would silently exempt a file that was rewritten later.
        foreach (var relative in Exceptions.Keys)
        {
            File.ReadAllText(Path.Combine(RepositoryRoot, relative))
                .ShouldContain("BeginTransactionAsync(", customMessage: relative);
        }
    }

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
