using Microsoft.Data.Sqlite;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A temporary SQLite database file for a single test, deleted on dispose
/// together with the journal and migration-lock files beside it.
/// </summary>
/// <remarks>
/// <para>
/// The four tests that need a real file database (<c>:memory:</c> without a
/// shared cache gives every connection its own empty database, which is not
/// the scenario they exercise) each carried their own copy of the cleanup
/// loop. They now share this one.
/// </para>
/// <para>
/// <strong><see cref="SqliteConnection.ClearAllPools"/> is what makes the
/// cleanup work on Windows.</strong> Microsoft.Data.Sqlite pools connections,
/// so disposing the host returns the connection to the pool instead of closing
/// the file handle. POSIX allows deleting a file that is still open, so the
/// leak is invisible on Linux and macOS; Windows fails the delete with
/// "because it is being used by another process" (measured, 2026-08-28,
/// windows-latest CI leg). Clearing the pool closes the handle first.
/// </para>
/// </remarks>
internal sealed class TempSqliteDatabase : IDisposable
{
    public TempSqliteDatabase(string name)
        => Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"tracon-{name}-{Guid.NewGuid():N}.db");

    /// <summary>Gets the database file path. The file does not exist yet.</summary>
    public string Path { get; }

    /// <summary>Gets the connection string <c>UseSqlite</c> is given.</summary>
    public string ConnectionString => $"Data Source={Path}";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        // A glob, not a fixed suffix list: the migration lock file is named
        // after the table prefix (K-389), so the suffix varies per test.
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var fileName = System.IO.Path.GetFileName(Path);

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }
    }
}
