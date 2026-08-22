using AgentPrism.Cli.FunctionalTests.Infrastructure;

namespace AgentPrism.Cli.FunctionalTests;

/// <summary>
/// The connection string and the bearer token never appear in CLI output,
/// success or failure (decision K-059, docs/83-TIPLI-ISTEMCI-VE-CLI.md,
/// section 83.5, manual case 9).
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class CliSecretRedactionTests
{
    private const string SecretMarker = "SwordFish-Secret-Marker-7f3a";

    [Fact]
    public async Task A_broken_connection_string_never_echoes_back_its_password()
    {
        var connectionString = $"Data Source=/no/such/directory/{SecretMarker}/db.sqlite";

        var result = await CliRunner.RunAsync("migrate", "--provider", "sqlite", "--connection", connectionString);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldNotContain(SecretMarker);
    }

    [Fact]
    public async Task An_invalid_token_never_appears_in_health_output()
    {
        await using var host = await RealHttpHost.StartAsync();

        var result = await CliRunner.RunAsync(
            "health", "--url", host.BaseAddress.ToString(), "--token", SecretMarker);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldNotContain(SecretMarker);
    }

    [Fact]
    public async Task Reading_the_connection_string_from_the_environment_variable_still_redacts_it()
    {
        var connectionString = $"Data Source=/no/such/directory/{SecretMarker}/db.sqlite";
        Environment.SetEnvironmentVariable("AGENTPRISM_CONNECTION", connectionString);

        try
        {
            var result = await CliRunner.RunAsync("migrate", "--provider", "sqlite");

            result.ExitCode.ShouldNotBe(0);
            result.Combined.ShouldNotContain(SecretMarker);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTPRISM_CONNECTION", null);
        }
    }
}
