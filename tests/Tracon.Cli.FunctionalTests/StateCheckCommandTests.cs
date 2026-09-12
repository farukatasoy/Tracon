using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// <c>tracon state-check</c> against a real SQLite file, without an
/// application ever starting (Phase 156, manual cases 1-5).
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class StateCheckCommandTests
{
    private const string SecretMarker = "SwordFish-Secret-Marker-156";

    /// <summary>The smallest payload the <c>json_valid(state)</c> check constraint accepts.</summary>
    private const string EmptyStateJson = "{}";

    /// <summary>
    /// SQLite has one object namespace per database, so Tracon prefixes
    /// its tables instead of using a schema. This is
    /// <c>TraconSqliteOptions.TablePrefix</c>'s default, which is what
    /// <c>tracon migrate --provider sqlite</c> creates.
    /// </summary>
    private const string Tables = "tracon_";

    [Fact]
    public async Task A_populated_database_reports_a_count_per_generation_and_exits_zero()
    {
        var connectionString = await MigratedDatabaseAsync();
        await InsertSessionAsync(connectionString, "a", generation: 1);
        await InsertSessionAsync(connectionString, "b", generation: 1);

        var result = await CliRunner.RunAsync("state-check", "--provider", "sqlite", "--connection", connectionString);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("generation 1: 2 row(s), readable by this build");
    }

    [Fact]
    public async Task A_generation_from_the_future_exits_three_and_leaves_the_row_alone()
    {
        var connectionString = await MigratedDatabaseAsync();
        await InsertSessionAsync(connectionString, "future", generation: 99);

        var result = await CliRunner.RunAsync("state-check", "--provider", "sqlite", "--connection", connectionString);

        result.ExitCode.ShouldBe(3);
        result.Combined.ShouldContain("NOT readable by this build");
        result.Combined.ShouldContain("1 row(s) carry a schema generation this build cannot read");

        (await ScalarAsync(connectionString, $"SELECT state_schema_version FROM {Tables}sessions WHERE id = 'future';"))
            .ShouldBe(99L, "an unreadable session is never rewritten or deleted");
    }

    [Fact]
    public async Task The_command_writes_nothing()
    {
        var connectionString = await MigratedDatabaseAsync();
        await InsertSessionAsync(connectionString, "a", generation: 1);
        await InsertSessionAsync(connectionString, "b", generation: 1);

        var before = await SnapshotAsync(connectionString);
        before.ShouldNotBeEmpty();

        (await CliRunner.RunAsync("state-check", "--provider", "sqlite", "--connection", connectionString))
            .ExitCode.ShouldBe(0);

        (await SnapshotAsync(connectionString)).ShouldBe(before);
    }

    [Fact]
    public async Task The_output_says_it_sampled_rather_than_claiming_every_row_is_readable()
    {
        var connectionString = await MigratedDatabaseAsync();

        for (var i = 0; i < 8; i++)
        {
            await InsertSessionAsync(connectionString, $"s{i}", generation: 1);
        }

        var result = await CliRunner.RunAsync(
            "state-check", "--provider", "sqlite", "--connection", connectionString, "--sample", "5");

        result.ExitCode.ShouldBe(0);

        // 🚨 The whole value of this command rests on this distinction. The
        // COUNT covers every row; the DECODE covers five of them. An operator
        // who reads "all readable" here upgrades on evidence nobody collected.
        result.StandardOutput.ShouldContain("Sampled 5 row(s), at most 5 per generation");
        result.StandardOutput.ShouldContain("This is a sample, not a survey");
        result.StandardOutput.ShouldNotContain("all readable");
        result.StandardOutput.ShouldNotContain("every row");
    }

    [Fact]
    public async Task An_empty_database_is_reported_rather_than_treated_as_an_error()
    {
        var connectionString = await MigratedDatabaseAsync();

        var result = await CliRunner.RunAsync("state-check", "--provider", "sqlite", "--connection", connectionString);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("(no rows)");
    }

    [Fact]
    public async Task An_unreachable_database_fails_with_one_line_and_no_stack_trace()
    {
        var result = await CliRunner.RunAsync(
            "state-check", "--provider", "sqlite", "--connection", "Data Source=/no/such/directory/db.sqlite");

        result.ExitCode.ShouldBe(2);
        result.Combined.ShouldContain("State check failed");
        result.Combined.ShouldNotContain("   at Tracon.", Case.Sensitive);
    }

    [Fact]
    public async Task A_broken_connection_string_never_echoes_back_its_password()
    {
        var result = await CliRunner.RunAsync(
            "state-check",
            "--provider",
            "sqlite",
            "--connection",
            $"Data Source=/no/such/directory/{SecretMarker}/db.sqlite");

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldNotContain(SecretMarker);
    }

    [Fact]
    public async Task Reading_the_connection_string_from_the_environment_variable_still_redacts_it()
    {
        Environment.SetEnvironmentVariable(
            "TRACON_CONNECTION",
            $"Data Source=/no/such/directory/{SecretMarker}/db.sqlite");

        try
        {
            var result = await CliRunner.RunAsync("state-check", "--provider", "sqlite");

            result.ExitCode.ShouldNotBe(0);
            result.Combined.ShouldNotContain(SecretMarker);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACON_CONNECTION", null);
        }
    }

    [Fact]
    public async Task A_sample_of_zero_counts_generations_without_decoding_anything()
    {
        var connectionString = await MigratedDatabaseAsync();
        await InsertSessionAsync(connectionString, "a", generation: 1);

        var result = await CliRunner.RunAsync(
            "state-check", "--provider", "sqlite", "--connection", connectionString, "--sample", "0");

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.ShouldContain("generation 1: 1 row(s)");
        result.StandardOutput.ShouldContain("Sampled 0 row(s)");
    }

    [Fact]
    public async Task A_negative_sample_is_an_argument_error_not_a_silent_default()
    {
        var connectionString = await MigratedDatabaseAsync();

        var result = await CliRunner.RunAsync(
            "state-check", "--provider", "sqlite", "--connection", connectionString, "--sample", "-3");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--sample");
    }

    [Fact]
    public async Task Json_output_is_one_machine_readable_document_on_stdout()
    {
        var connectionString = await MigratedDatabaseAsync();
        await InsertSessionAsync(connectionString, "a", generation: 1);

        var result = await CliRunner.RunAsync(
            "state-check", "--provider", "sqlite", "--connection", connectionString, "--json");

        result.ExitCode.ShouldBe(0);

        using var document = JsonDocument.Parse(result.StandardOutput);
        var sessions = document.RootElement.GetProperty("sessions");
        sessions.GetArrayLength().ShouldBe(1);
        document.RootElement.GetProperty("isClean").GetBoolean().ShouldBeTrue();

        // A bare `0` here would tell a reader nothing; every enum this product
        // serializes is a string.
        sessions[0].GetProperty("target").GetString().ShouldBe("sessions");
    }

    [Fact]
    public async Task A_future_generation_is_not_explained_away_as_encryption()
    {
        var connectionString = await MigratedDatabaseAsync();
        await InsertSessionAsync(connectionString, "future", generation: 99);

        var result = await CliRunner.RunAsync("state-check", "--provider", "sqlite", "--connection", connectionString);

        // The row IS counted as structure-only (its generation already named
        // it, so decoding it again would only add a vaguer second message).
        // The line that explains structure-only rows therefore has to name
        // that reason too, or it sends the operator hunting for an encryption
        // setting that was never turned on.
        result.ExitCode.ShouldBe(3);
        result.StandardOutput.ShouldContain("already reported above as unreadable");
    }

    [Fact]
    public async Task A_missing_connection_string_fails_with_a_readable_error()
    {
        var result = await CliRunner.RunAsync("state-check", "--provider", "sqlite");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--connection");
    }

    private static async Task<string> MigratedDatabaseAsync()
    {
        var connectionString =
            $"Data Source={Path.Combine(Path.GetTempPath(), $"tracon-state-check-{Guid.NewGuid():N}.db")}";

        (await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString))
            .ExitCode.ShouldBe(0);

        return connectionString;
    }

    private static async Task InsertSessionAsync(string connectionString, string id, int generation)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

        await ExecuteAsync(
            connectionString,
            $"""
            INSERT INTO {Tables}sessions (id, tenant_id, agent_name, state, state_schema_version, state_maf_version, created_at, updated_at, version)
            VALUES ('{id}', 'default', 'test-agent', '{EmptyStateJson}', {generation.ToString(CultureInfo.InvariantCulture)}, '1.18.0', '{now}', '{now}', 1);
            """);
    }

    private static async Task<string> SnapshotAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE(group_concat(row, char(10)), '')
            FROM (
                SELECT id || '|' || tenant_id || '|' || agent_name || '|' || state || '|' || state_schema_version
                       || '|' || COALESCE(state_maf_version, '') || '|' || created_at || '|' || updated_at
                       || '|' || version || '|' || COALESCE(owner_id, '') AS row
                FROM {Tables}sessions
                ORDER BY id
            );
            """;

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync();
    }
}
