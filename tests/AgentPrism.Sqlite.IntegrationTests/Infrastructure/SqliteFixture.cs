namespace AgentPrism.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// The disposable SQLite database file shared by all integration tests.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A container is NOT REQUIRED.</strong> Since SQLite is a single
/// file, a temporary file path is enough; this shortens CI time compared to
/// the PostgreSQL/SQL Server contract runs (docs/24-SQLITE.md, section 24.5).
/// </para>
/// <para>
/// The file is created once for the whole assembly; the table prefix is
/// shared per contract test CLASS (see <see cref="SqliteSchemaFixture"/>,
/// <see cref="SqliteTestContext"/>), and isolation between tests is achieved
/// by resetting data (K-390) — the same pattern as SQL Server/PostgreSQL.
/// </para>
/// </remarks>
public sealed class SqliteFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"agentprism-tests-{Guid.NewGuid():N}.db");

    /// <summary>Gets the connection string of the running database.</summary>
    public string ConnectionString => $"Data Source={_databasePath}";

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// Migration lock files are now scoped to the table prefix (K-389), so the
    /// file name varies per class; it is scanned with a directory glob instead
    /// of a fixed suffix list.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        var directory = Path.GetDirectoryName(_databasePath)!;
        var fileName = Path.GetFileName(_databasePath);

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }

        return ValueTask.CompletedTask;
    }
}
