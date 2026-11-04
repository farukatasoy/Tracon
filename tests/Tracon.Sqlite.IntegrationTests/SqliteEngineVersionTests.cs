using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The SQLite library a run loads is the version the run was asked for.
/// </summary>
/// <remarks>
/// A leg of the platform matrix replaces the native library of the SQLite driver with an older one from the same package
/// family and runs the whole project against it. If the replacement is not the file the process loads (a different runtime
/// folder, a library found elsewhere first), the leg would run on the bundled version and pass without saying so. This case
/// reads the version from the engine itself, so such a leg fails instead. A run that sets no version uses the library the
/// package ships and the case does not apply.
/// </remarks>
public sealed class SqliteEngineVersionTests
{
    /// <summary>The environment variable that names the version of the SQLite library the run expects.</summary>
    public const string VersionVariable = "TRACON_TEST_SQLITE_VERSION";

    [Fact]
    public async Task The_engine_is_the_version_the_run_was_asked_for()
    {
        var expected = Environment.GetEnvironmentVariable(VersionVariable);

        Assert.SkipWhen(string.IsNullOrWhiteSpace(expected), $"{VersionVariable} is not set: the run uses the library the package ships.");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var actual = Convert.ToString(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);

        actual.ShouldBe(expected);
    }
}
