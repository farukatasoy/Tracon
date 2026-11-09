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

    /// <summary>
    /// Reads every byte of the database file, its write-ahead log and its rollback journal, including the bytes a
    /// connection that is still open has not folded into the main file yet.
    /// </summary>
    /// <returns>The bytes of every file, one after the other.</returns>
    /// <remarks>
    /// <para>
    /// .NET on Unix takes a shared <c>flock</c> on every file it opens, and on macOS that lock conflicts with the
    /// byte-range locks SQLite takes. A read fails with "being used by another process" while SQLite holds a write
    /// lock on the file, even inside one process; Linux keeps the two kinds of lock apart. Read only a database whose
    /// connections all live in this process: closing a descriptor drops every byte-range lock the process holds on
    /// the file, and SQLite needs those locks only against other processes.
    /// </para>
    /// <para>
    /// The last connection to close takes an exclusive lock on the main file while it folds the log in and deletes it.
    /// Any pool clear in the process can make a host's idle connection that last one (every <see cref="Dispose"/>
    /// calls <see cref="SqliteConnection.ClearAllPools"/>, which closes the idle connections of every test running at
    /// the same time). So the files are read under a read transaction of their own: while it holds the database, no
    /// other close is the last, and the log stays where it is.
    /// </para>
    /// <para>
    /// The shared-memory file (<c>-shm</c>) is not read. It is the index of the log, page numbers with no row content,
    /// and SQLite locks a byte of it for every write transaction.
    /// </para>
    /// </remarks>
    public byte[] ReadAllBytes() => ReadAllBytes(whileHeld: static () => { });

    /// <summary>Reads the files as <see cref="ReadAllBytes()"/> does and runs a step while the read transaction holds the database.</summary>
    /// <param name="whileHeld">Runs after the read transaction is taken and before the first file is read.</param>
    /// <returns>The bytes of every file, one after the other.</returns>
    internal byte[] ReadAllBytes(Action whileHeld)
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        using var reader = new SqliteConnection(ConnectionString + ";Pooling=False");
        reader.Open();

        using var transaction = reader.BeginTransaction(deferred: true);

        using (var read = reader.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT count(*) FROM sqlite_master";
            read.ExecuteScalar();
        }

        whileHeld();

        using var all = new MemoryStream();

        foreach (var path in new[] { Path, Path + "-wal", Path + "-journal" }.Where(File.Exists))
        {
            // Windows refuses a plain read of a file another handle writes to unless writers are allowed to share.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            stream.CopyTo(all);
        }

        return all.ToArray();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        // A glob, not a fixed suffix list: the migration lock file is named
        // after the table prefix, so the suffix varies per test.
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var fileName = System.IO.Path.GetFileName(Path);

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            File.Delete(path);
        }
    }
}
