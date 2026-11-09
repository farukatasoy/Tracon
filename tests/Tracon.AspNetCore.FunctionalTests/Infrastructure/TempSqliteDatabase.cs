using System.Diagnostics;
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
    /// Reads every byte of the database file and of the files beside it (the write-ahead log, the shared-memory file, the
    /// journal), including the bytes a connection that is still open has not folded into the main file yet.
    /// </summary>
    /// <returns>The bytes of every file, one after the other.</returns>
    public byte[] ReadAllBytes()
    {
        var directory = System.IO.Path.GetDirectoryName(Path)!;
        var fileName = System.IO.Path.GetFileName(Path);
        using var all = new MemoryStream();

        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*"))
        {
            try
            {
                // The host may hold the file open; Windows refuses a plain read unless writers are allowed to share.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                stream.CopyTo(all);
            }
            catch (IOException exception)
            {
                // A sharing violation on a Unix file means somebody holds a lock on it. It showed once in a full run on
                // macOS and could not be repeated with the three test classes that read the file; the failure names the
                // holder so the next time says who it is instead of guessing.
                throw new IOException($"{exception.Message}{Environment.NewLine}Holders of the file, from lsof:{Environment.NewLine}{WhoHolds(path)}", exception);
            }
        }

        return all.ToArray();
    }

    private static string WhoHolds(string path)
    {
        try
        {
            using var lsof = Process.Start(new ProcessStartInfo("lsof", ["-nP", "--", path])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (lsof is null)
            {
                return "(lsof did not start)";
            }

            var output = lsof.StandardOutput.ReadToEnd();

            lsof.WaitForExit(TimeSpan.FromSeconds(5));

            return output.Length == 0 ? "(no process holds the file now)" : output;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"(lsof is not available: {exception.Message})";
        }
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
