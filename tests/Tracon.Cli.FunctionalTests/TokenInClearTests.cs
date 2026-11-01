using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// A command that would send its bearer token over plain <c>http</c> to another machine says so on standard error.
/// </summary>
/// <remarks>
/// The token is a credential for the whole installation. The command still runs: a plain-http address inside a trusted
/// network, or behind a proxy that ends TLS, is a decision the operator may have made. The token itself is never printed.
/// </remarks>
[Collection(nameof(CliTestGroup))]
public sealed class TokenInClearTests
{
    private const string Secret = "tk-0123456789abcdef";

    [Theory]
    [InlineData("health")]
    [InlineData("export")]
    [InlineData("eval")]
    public async Task A_token_for_a_plain_http_address_on_another_machine_is_warned_about(string command)
    {
        var result = await Run(command, "http://tracon.invalid:5080/tracon");

        result.StandardError.ShouldContain("plain http", Case.Sensitive);
        result.StandardError.ShouldContain("tracon.invalid", Case.Sensitive);
        result.Combined.ShouldNotContain(Secret, Case.Sensitive);
    }

    [Theory]
    [InlineData("http://localhost:5080/tracon")]
    [InlineData("http://127.0.0.1:5080/tracon")]
    [InlineData("https://tracon.invalid/tracon")]
    public async Task A_token_for_a_loopback_or_an_https_address_is_not_warned_about(string url)
    {
        var result = await Run("health", url);

        result.StandardError.ShouldNotContain("plain http", Case.Sensitive);
    }

    [Fact]
    public async Task A_command_without_a_token_is_not_warned_about()
    {
        var result = await CliRunner.RunAsync("health", "--url", "http://tracon.invalid:5080/tracon");

        result.StandardError.ShouldNotContain("plain http", Case.Sensitive);
    }

    private static Task<CliResult> Run(string command, string url)
        => command switch
        {
            "eval" => CliRunner.RunAsync("eval", "--url", url, "--suite", "any", "--token", Secret, "--timeout", "1"),
            _ => CliRunner.RunAsync(command, "--url", url, "--token", Secret),
        };
}
