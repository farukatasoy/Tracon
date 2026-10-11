using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// The disposable directory that holds the SQLite database files of the
/// integration tests: one file per table prefix.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A container is NOT REQUIRED.</strong> Since SQLite is a single
/// file, a temporary file path is enough; this shortens CI time compared to
/// the PostgreSQL/SQL Server contract runs (docs/arsiv/fazlar/24-SQLITE.md, section 24.5).
/// </para>
/// <para>
/// 🚨 <strong>Every table prefix gets its OWN database file</strong>
/// (<see cref="ConnectionStringFor"/>), not a prefix inside one file shared
/// by the whole assembly. SQLite has ONE writer lock per file, WAL included.
/// While every class shared a single file, every write in the assembly - the
/// tests' own writes, each class fixture's migration set, each reset, each
/// cleanup's DROP statements - queued for that one lock, and xunit runs the
/// classes in parallel. The busy handler is a sleep-and-poll loop, not a
/// queue: a writer that has waited long polls rarely, and newcomers take the
/// lock first. Under a saturated machine one statement starved past
/// Microsoft.Data.Sqlite's 30-second command timeout and failed with
/// <c>SQLite Error 5: 'database is locked'</c> in a random test (F-307;
/// traced: no transaction held the lock longer than 1.7 s and commits never
/// paused longer, while single statements in the same runs waited 30-61 s).
/// PostgreSQL and SQL Server have no per-database writer lock, so the
/// one-server, schema-per-class model (K-390) stays right for them; for SQLite
/// the lock domain must be no wider than the unit xunit runs in parallel.
/// </para>
/// <para>
/// Contexts that name the SAME prefix share the file on purpose: the
/// deliberate concurrency tests open several contexts on one prefix
/// (<see cref="SqliteTestContext.Create(SqliteFixture, string, string, bool)"/>).
/// Two prefixes in one physical file - a supported production layout (K-193,
/// K-389) - is proven by tests that build that layout explicitly.
/// </para>
/// </remarks>
public sealed class SqliteFixture : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"tracon-tests-{Guid.NewGuid():N}");

    /// <summary>Creates the fixture and its temporary directory.</summary>
    public SqliteFixture() => Directory.CreateDirectory(_directory);

    /// <summary>Gets the connection string of the database file that holds the given table prefix.</summary>
    /// <param name="tablePrefix">The table prefix; a fresh prefix yields a fresh, empty file.</param>
    /// <returns>A connection string whose file lives in this fixture's directory.</returns>
    public string ConnectionStringFor(string tablePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tablePrefix);

        return $"Data Source={Path.Combine(_directory, tablePrefix + "db.sqlite")}";
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    /// <remarks>
    /// The directory also holds the journal files and the migration lock files
    /// (scoped to the table prefix, K-389), so it is removed as a whole.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        // Microsoft.Data.Sqlite pools connections, so the file handle outlives
        // the last store. POSIX deletes an open file happily; Windows refuses
        // with "being used by another process" (measured, 2026-08-28).
        SqliteConnection.ClearAllPools();

        Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }
}
