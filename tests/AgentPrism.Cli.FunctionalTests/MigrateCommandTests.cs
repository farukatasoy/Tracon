using System.Globalization;
using System.Text.RegularExpressions;
using AgentPrism.Cli.FunctionalTests.Infrastructure;

namespace AgentPrism.Cli.FunctionalTests;

/// <summary>
/// <c>agentprism migrate</c> / <c>migrate status</c> against a real SQLite
/// file, without an application ever starting (docs/arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md,
/// section 83.5, manual cases 1-3).
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class MigrateCommandTests
{
    [Fact]
    public async Task Migrate_applies_pending_migrations_to_an_empty_database()
    {
        var connectionString = NewSqliteConnectionString();

        var result = await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString);

        result.ExitCode.ShouldBe(0);
        CountOf(result.StandardOutput, "applied").ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Migrate_run_a_second_time_is_idempotent()
    {
        var connectionString = NewSqliteConnectionString();

        (await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString))
            .ExitCode.ShouldBe(0);

        var second = await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString);

        second.ExitCode.ShouldBe(0);
        CountOf(second.StandardOutput, "applied").ShouldBe(0);
    }

    [Fact]
    public async Task Status_reports_no_pending_migrations_and_writes_nothing()
    {
        var connectionString = NewSqliteConnectionString();
        var databasePath = ExtractDataSource(connectionString);

        (await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString))
            .ExitCode.ShouldBe(0);

        var beforeStatus = new FileInfo(databasePath).LastWriteTimeUtc;

        var status = await CliRunner.RunAsync("migrate", "status", "--provider", "sqlite", "--connection", connectionString);

        status.ExitCode.ShouldBe(0);
        CountOf(status.StandardOutput, "pending").ShouldBe(0);
        new FileInfo(databasePath).LastWriteTimeUtc.ShouldBe(beforeStatus);
    }

    [Fact]
    public async Task Status_before_any_migrate_lists_pending_names()
    {
        var connectionString = NewSqliteConnectionString();

        var status = await CliRunner.RunAsync("migrate", "status", "--provider", "sqlite", "--connection", connectionString);

        status.ExitCode.ShouldBe(0);
        CountOf(status.StandardOutput, "pending").ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task An_unknown_provider_name_fails_with_a_readable_error_and_writes_nothing()
    {
        var connectionString = NewSqliteConnectionString();

        var result = await CliRunner.RunAsync("migrate", "--provider", "no-such-provider", "--connection", connectionString);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldContain("no-such-provider");
        File.Exists(ExtractDataSource(connectionString)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_missing_connection_string_fails_with_a_readable_error()
    {
        var result = await CliRunner.RunAsync("migrate", "--provider", "sqlite");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--connection");
    }

    private static string NewSqliteConnectionString()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentprism-cli-tests-{Guid.NewGuid():N}.db");
        return $"Data Source={path}";
    }

    private static string ExtractDataSource(string connectionString) =>
        connectionString["Data Source=".Length..];

    /// <summary>
    /// Reads the <c>&lt;n&gt; applied</c> / <c>&lt;n&gt; pending</c> count out
    /// of the command's first line.
    /// </summary>
    /// <param name="output">The command's standard output.</param>
    /// <param name="noun">Either <c>applied</c> or <c>pending</c>.</param>
    /// <returns>The number reported.</returns>
    /// <remarks>
    /// 🚨 The number is PARSED, not string-matched. These assertions used to
    /// read <c>ShouldNotContain("0 applied")</c>, which passes only while the
    /// real count has no trailing zero: the migration set reaching 30 turned
    /// <c>"30 applied"</c> into a match for <c>"0 applied"</c> and failed a
    /// test that was describing correct behaviour. Any assertion about a
    /// COUNT compares numbers.
    /// </remarks>
    private static int CountOf(string output, string noun)
    {
        var match = Regex.Match(
            output,
            $@"(?m)^(?<count>\d+) {Regex.Escape(noun)}\b",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        match.Success.ShouldBeTrue($"the output has no '<n> {noun}' line: {output}");

        return int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
    }

}
