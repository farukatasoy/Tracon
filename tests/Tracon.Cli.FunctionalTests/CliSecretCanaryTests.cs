using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// The bearer token and the connection string a command is given reach no output of the command: not standard output,
/// not standard error, not a file the command writes, whether the value came on the command line or from the
/// environment, and whether the command succeeded or failed.
/// </summary>
/// <remarks>
/// <para>
/// The token is the one that guards a whole installation. The commands that carry it (<c>health</c>, <c>export</c>,
/// <c>import</c>, <c>eval</c>) run against a real listening host that REQUIRES that token, so a command that succeeds
/// proves the token was sent, and a failing one (a wrong token, a suite that does not exist, a server nothing listens
/// on) is the path where an error text could quote it. The commands that carry a connection string (<c>migrate</c>,
/// <c>migrate-status</c>, <c>state-check</c>) run against both server providers at an address nothing listens on, with
/// the password of that string a value of its own: the driver's failure text is the one an operator reads.
/// </para>
/// <para>
/// The value on the command line is the caller's choice and is visible to anyone who can list processes: the commands
/// take both secrets from the environment too, which the second half of the matrix drives.
/// </para>
/// </remarks>
[Collection(nameof(CliTestGroup))]
public sealed class CliSecretCanaryTests
{
    private const string TokenStem = "canary-cli-token";
    private const string Token = TokenStem + "-5b0e83d7a216c94f";
    private const string WrongStem = "canary-cli-wrong-token";
    private const string PasswordStem = "canary-cli-connection-password";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_token_is_in_no_output_and_no_file_of_a_command_that_succeeds_or_fails(bool fromEnvironment)
    {
        await using var host = await RealHttpHost.StartAsync(
            configureTracon: static builder => builder.AddAgent(new AgentDefinition
            {
                Name = "canary-agent",
                Instructions = "Answer briefly.",
                Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
            }),
            configureEndpoints: static options => options.AuthToken = Token);

        var root = Directory.CreateTempSubdirectory("tracon-cli-canary-").FullName;
        var outputs = new StringBuilder();
        var url = host.BaseAddress.ToString();

        Environment.SetEnvironmentVariable("TRACON_TOKEN", fromEnvironment ? Token : null);

        try
        {
            // On the command line, or from the environment: the value is the same and so is the command.
            string[] Auth(string value) => fromEnvironment && string.Equals(value, Token, StringComparison.Ordinal) ? [] : ["--token", value];

            var exported = Path.Combine(root, "bundle.json");

            // Succeeds: the host refuses a request without the token, so success is the proof that it was sent.
            foreach (var (name, args) in (ValueTuple<string, string[]>[])
            [
                ("health", ["health", "--url", url, .. Auth(Token)]),
                ("health --json", ["health", "--url", url, "--json", .. Auth(Token)]),
                ("export", ["export", "--url", url, "--output", exported, .. Auth(Token)]),
            ])
            {
                var result = await CliRunner.RunAsync(args);

                result.ExitCode.ShouldBe(0, $"{name} did not succeed with the token: {result.Combined}");
                outputs.AppendLine(name + "\n" + result.Combined);
            }

            File.Exists(exported).ShouldBeTrue("export wrote no file");
            outputs.AppendLine("exported file\n" + await File.ReadAllTextAsync(exported));

            foreach (var (name, args) in (ValueTuple<string, string[]>[])
            [
                ("import dry run", ["import", "--file", exported, "--url", url, .. Auth(Token)]),
                ("import --apply --json", ["import", "--file", exported, "--apply", "--json", "--url", url, .. Auth(Token)]),
            ])
            {
                var result = await CliRunner.RunAsync(args);

                outputs.AppendLine(name + "\n" + result.Combined);
            }

            // Fails: a wrong token, a suite that is not there, a server nothing listens on, a file that is not a bundle.
            var wrong = WrongStem + "-71c8";

            foreach (var (name, args) in (ValueTuple<string, string[]>[])
            [
                ("health with a wrong token", ["health", "--url", url, "--token", wrong]),
                ("export with a wrong token", ["export", "--url", url, "--token", wrong, "--output", Path.Combine(root, "wrong.json")]),
                ("eval, no such suite", ["eval", "--url", url, "--suite", "no-such-suite", .. Auth(Token)]),
                ("eval --json, no such suite", ["eval", "--url", url, "--suite", "no-such-suite", "--json", .. Auth(Token)]),
                ("health, nothing listens", ["health", "--url", "http://127.0.0.1:1/tracon/", .. Auth(Token)]),
                ("export, nothing listens", ["export", "--url", "http://127.0.0.1:1/tracon/", "--output", Path.Combine(root, "gone.json"), .. Auth(Token)]),
                ("import a file that is not a bundle", ["import", "--file", Path.Combine(root, "missing.json"), "--url", url, .. Auth(Token)]),
            ])
            {
                var result = await CliRunner.RunAsync(args);

                result.ExitCode.ShouldNotBe(0, $"{name} was meant to fail");
                outputs.AppendLine(name + "\n" + result.Combined);
            }

            // Every file the commands left in the directory is an output too.
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => !string.Equals(path, exported, StringComparison.Ordinal)))
            {
                outputs.AppendLine(path + "\n" + await File.ReadAllTextAsync(path));
            }

            SecretHits.Find(
                    [new("standard output, standard error and files", outputs.ToString())],
                    [new("token", TokenStem), new("wrong token", WrongStem)])
                .ShouldBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACON_TOKEN", null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("postgresql", "migrate", false)]
    [InlineData("postgresql", "migrate", true)]
    [InlineData("postgresql", "migrate-status", false)]
    [InlineData("postgresql", "state-check", true)]
    [InlineData("sqlserver", "migrate", false)]
    [InlineData("sqlserver", "migrate", true)]
    [InlineData("sqlserver", "migrate-status", false)]
    [InlineData("sqlserver", "state-check", true)]
    public async Task The_connection_password_is_in_no_output_of_a_command_that_cannot_connect(string provider, string command, bool fromEnvironment)
    {
        var password = $"{PasswordStem}-{provider}-e49a01d3";

        var connection = string.Equals(provider, "postgresql", StringComparison.Ordinal)
            ? $"Host=127.0.0.1;Port=1;Database=canary;Username=canary_login;Password={password};Timeout=1;Command Timeout=1"
            : $"Server=127.0.0.1,1;Database=canary;User Id=canary_login;Password={password};TrustServerCertificate=true;Connect Timeout=1";

        Environment.SetEnvironmentVariable("TRACON_CONNECTION", fromEnvironment ? connection : null);

        try
        {
            string[] args = fromEnvironment
                ? [command, "--provider", provider]
                : [command, "--provider", provider, "--connection", connection];

            var plain = await CliRunner.RunAsync(args);
            var json = await CliRunner.RunAsync([.. args, "--json"]);

            plain.ExitCode.ShouldNotBe(0, "the command has to fail to connect, or an empty search proves nothing");

            SecretHits.Find(
                    [new("standard output and standard error", plain.Combined + "\n" + json.Combined)],
                    [new("connection password", PasswordStem)])
                .ShouldBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACON_CONNECTION", null);
        }
    }
}
