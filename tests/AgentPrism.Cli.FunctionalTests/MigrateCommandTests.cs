using AgentPrism.Cli.FunctionalTests.Infrastructure;

namespace AgentPrism.Cli.FunctionalTests;

/// <summary>
/// <c>agentprism migrate</c> / <c>migrate status</c> against a real SQLite
/// file, without an application ever starting (docs/83-TIPLI-ISTEMCI-VE-CLI.md,
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
        result.StandardOutput.ShouldContain("applied");
        result.StandardOutput.ShouldNotContain("0 applied");
    }

    [Fact]
    public async Task Migrate_run_a_second_time_is_idempotent()
    {
        var connectionString = NewSqliteConnectionString();

        (await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString))
            .ExitCode.ShouldBe(0);

        var second = await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString);

        second.ExitCode.ShouldBe(0);
        second.StandardOutput.ShouldContain("0 applied");
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
        status.StandardOutput.ShouldContain("0 pending");
        new FileInfo(databasePath).LastWriteTimeUtc.ShouldBe(beforeStatus);
    }

    [Fact]
    public async Task Status_before_any_migrate_lists_pending_names()
    {
        var connectionString = NewSqliteConnectionString();

        var status = await CliRunner.RunAsync("migrate", "status", "--provider", "sqlite", "--connection", connectionString);

        status.ExitCode.ShouldBe(0);
        status.StandardOutput.ShouldNotContain("0 pending");
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
}
