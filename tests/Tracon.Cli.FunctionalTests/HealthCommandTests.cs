using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// <c>tracon health</c> against a real, listening Tracon host
/// (docs/arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md, section 83.5, manual cases 5-8): the
/// client's first consumer.
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class HealthCommandTests
{
    [Fact]
    public async Task Reads_health_from_a_running_server_with_the_default_prefix()
    {
        await using var host = await RealHttpHost.StartAsync();

        var result = await CliRunner.RunAsync("health", "--url", host.BaseAddress.ToString());

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Reads_health_through_a_custom_MapTracon_prefix()
    {
        // 83.3's trap: a client generated straight from the document (which
        // carries NO prefix) would 404 the moment the app mounts anywhere
        // other than the default "/tracon". This proves the stripped
        // prefix moved to TraconClientOptions.BaseAddress correctly.
        await using var host = await RealHttpHost.StartAsync(prefix: "control");

        var result = await CliRunner.RunAsync("health", "--url", host.BaseAddress.ToString());

        result.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Json_flag_produces_parseable_JSON()
    {
        await using var host = await RealHttpHost.StartAsync();

        var result = await CliRunner.RunAsync("health", "--url", host.BaseAddress.ToString(), "--json");

        result.ExitCode.ShouldBe(0);
        Should.NotThrow(() => System.Text.Json.JsonDocument.Parse(result.StandardOutput));
    }

    [Fact]
    public async Task An_invalid_bearer_token_becomes_a_readable_401_not_an_exception()
    {
        await using var host = await RealHttpHost.StartAsync();

        var result = await CliRunner.RunAsync("health", "--url", host.BaseAddress.ToString(), "--token", "FIX-TOKEN-02");

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldContain("401");
    }

    [Fact]
    public async Task A_server_that_is_not_running_fails_fast_instead_of_hanging()
    {
        // Port 1 is a real, closed, privileged port: the connection is
        // refused immediately rather than time out, which keeps the test fast
        // while still exercising the "server unreachable" branch.
        var result = await CliRunner.RunAsync("health", "--url", "http://127.0.0.1:1/tracon/");

        result.ExitCode.ShouldNotBe(0);
    }

    [Fact]
    public async Task A_relative_url_is_rejected_as_a_usage_error()
    {
        var result = await CliRunner.RunAsync("health", "--url", "not-a-url");

        result.ExitCode.ShouldBe(1);
        result.Combined.ShouldContain("--url");
    }
}
