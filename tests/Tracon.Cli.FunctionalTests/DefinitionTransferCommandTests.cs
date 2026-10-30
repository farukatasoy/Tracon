using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Cli.FunctionalTests.Infrastructure;
using Tracon.Testing;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// <c>tracon export</c> and <c>tracon import</c> against two real, listening Tracon hosts: the file leaves one and
/// enters the other, the import is a dry run unless it is told to write, and the exit code says what a pipeline needs.
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class DefinitionTransferCommandTests
{
    private const string SecretMarker = "SwordFish-Secret-Marker-9c41";

    [Fact]
    public async Task Export_writes_the_file_to_output_and_everything_it_says_to_standard_error()
    {
        await using var source = await StartAsync();
        await SeedAsync(source, "triage", "Route the request.");

        using var file = new TempFile();

        var result = await CliRunner.RunAsync("export", "--url", source.BaseAddress.ToString(), "--output", file.Path);

        result.ExitCode.ShouldBe(0, result.Combined);
        result.StandardOutput.ShouldBeEmpty();
        result.StandardError.ShouldContain("1 agent(s)");

        var bundle = JsonNode.Parse(await File.ReadAllTextAsync(file.Path))!;

        bundle["format"]!.GetValue<string>().ShouldBe("tracon.definitions");
        bundle["agents"]![0]!["definition"]!["name"]!.GetValue<string>().ShouldBe("triage");
    }

    // Six letters a console code page other than UTF-8 corrupts first (c-cedilla, g-breve, dotless i, o-umlaut,
    // s-cedilla, u-umlaut). They are written as escapes so the source stays ASCII.
    private const string NonAsciiInstructions = "Non-ASCII instructions: \u00e7 \u011f \u0131 \u00f6 \u015f \u00fc";

    [Fact]
    public async Task Export_to_standard_output_writes_the_bytes_of_the_file_and_nothing_else()
    {
        await using var source = await StartAsync();
        await SeedAsync(source, "triage", NonAsciiInstructions);

        // Console.SetOut cannot see raw bytes written to the process's standard output, and a console code page that is
        // not UTF-8 is exactly what would corrupt this text: so this one case runs the tool as its own process.
        var (exitCode, bytes, error) = await RunProcessAsync("export", "--url", source.BaseAddress.ToString());

        exitCode.ShouldBe(0, error);

        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

        JsonNode.Parse(text)!["agents"]![0]!["definition"]!["instructions"]!.GetValue<string>().ShouldBe(NonAsciiInstructions);
        text.ShouldNotContain("Wrote");
        error.ShouldContain("1 agent(s)");
    }

    [Fact]
    public async Task Import_is_a_dry_run_until_apply_is_given_and_a_second_apply_writes_nothing()
    {
        await using var source = await StartAsync();
        await using var target = await StartAsync();
        await SeedAsync(source, "triage", "Route the request.");

        using var file = new TempFile();

        await ExportAsync(source, file);

        var dry = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path);

        dry.ExitCode.ShouldBe(0, dry.Combined);
        dry.StandardOutput.ShouldContain("Dry run: nothing is written.");
        dry.StandardOutput.ShouldContain("create");
        (await target.Services.GetRequiredService<IAgentDefinitionStore>().GetAsync("triage")).ShouldBeNull();

        var applied = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path, "--apply");

        applied.ExitCode.ShouldBe(0, applied.Combined);
        applied.StandardOutput.ShouldContain("Written: 1 item(s).");
        (await target.Services.GetRequiredService<IAgentDefinitionStore>().GetAsync("triage")).ShouldNotBeNull();

        var again = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path, "--apply");

        again.ExitCode.ShouldBe(0, again.Combined);
        again.StandardOutput.ShouldContain("unchanged");
        again.StandardOutput.ShouldContain("Written: 0 item(s).");
    }

    [Fact]
    public async Task Json_prints_the_report_exactly_as_the_server_sent_it()
    {
        await using var source = await StartAsync();
        await using var target = await StartAsync();
        await SeedAsync(source, "triage", "Route the request.");

        using var file = new TempFile();

        await ExportAsync(source, file);

        var result = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path, "--json");

        result.ExitCode.ShouldBe(0, result.Combined);

        var report = JsonNode.Parse(result.StandardOutput)!;

        report["dryRun"]!.GetValue<bool>().ShouldBeTrue();
        report["items"]!.AsArray().Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_file_with_a_refused_item_exits_3_and_writes_nothing_even_with_apply()
    {
        await using var target = await StartAsync();

        using var file = new TempFile();

        await File.WriteAllTextAsync(file.Path, BundleWithAgent("broken", skillNames: ["absent"]));

        var dry = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path);

        dry.ExitCode.ShouldBe(3, dry.Combined);
        dry.StandardOutput.ShouldContain("unknown_skill");
        dry.StandardOutput.ShouldContain("Nothing was written.");

        var apply = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path, "--apply");

        apply.ExitCode.ShouldBe(3, apply.Combined);
        (await target.Services.GetRequiredService<IAgentDefinitionStore>().GetAsync("broken")).ShouldBeNull();
    }

    [Fact]
    public async Task A_file_the_server_cannot_read_exits_3_and_says_why()
    {
        await using var target = await StartAsync();

        using var file = new TempFile();

        await File.WriteAllTextAsync(file.Path, """{ "format": "tracon.definitions", "formatVersion": 2, "agents": [], "workflows": [], "skills": [], "mcpServers": [] }""");

        var result = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path);

        result.ExitCode.ShouldBe(3, result.Combined);
        result.StandardError.ShouldContain("HTTP 400");
        result.StandardError.ShouldContain("formatVersion");
    }

    [Fact]
    public async Task An_agent_that_needs_a_tool_of_a_new_server_exits_3_and_names_the_two_steps()
    {
        await using var target = await StartAsync();

        using var file = new TempFile();

        await File.WriteAllTextAsync(
            file.Path,
            BundleWithAgent("finder", toolNames: ["lookup-srv_lookup"], mcpServer: ("lookup-srv", "https://mcp.example.test/mcp")));

        var result = await CliRunner.RunAsync("import", "--url", target.BaseAddress.ToString(), "--file", file.Path);

        result.ExitCode.ShouldBe(3, result.Combined);
        result.StandardOutput.ShouldContain("blocked");
        result.StandardOutput.ShouldContain("import_mcp_stage_required");
        result.StandardOutput.ShouldContain("types=mcpServers");
    }

    [Fact]
    public async Task A_server_that_is_not_running_exits_2()
    {
        using var file = new TempFile();

        await File.WriteAllTextAsync(file.Path, BundleWithAgent("a"));

        var result = await CliRunner.RunAsync("import", "--url", "http://127.0.0.1:1/tracon", "--file", file.Path);

        result.ExitCode.ShouldBe(2, result.Combined);

        var export = await CliRunner.RunAsync("export", "--url", "http://127.0.0.1:1/tracon");

        export.ExitCode.ShouldBe(2, export.Combined);
    }

    [Fact]
    public async Task Missing_or_wrong_arguments_exit_1()
    {
        (await CliRunner.RunAsync("import", "--url", "http://localhost:1/tracon")).ExitCode.ShouldBe(1);
        (await CliRunner.RunAsync("import", "--url", "http://localhost:1/tracon", "--file", "/no/such/file.json")).ExitCode.ShouldBe(1);
        (await CliRunner.RunAsync("export")).ExitCode.ShouldBe(1);

        // Naming definitions and kinds in one call is an argument error, not a request the server has to refuse.
        (await CliRunner.RunAsync("export", "--url", "http://localhost:1/tracon", "--agent", "a", "--types", "skills")).ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task The_token_never_appears_in_the_output_of_either_command()
    {
        await using var host = await StartAsync();

        using var file = new TempFile();

        await File.WriteAllTextAsync(file.Path, BundleWithAgent("a"));

        var export = await CliRunner.RunAsync("export", "--url", host.BaseAddress.ToString(), "--token", SecretMarker, "--output", file.Path + ".out");
        var import = await CliRunner.RunAsync("import", "--url", host.BaseAddress.ToString(), "--file", file.Path, "--token", SecretMarker);

        export.Combined.ShouldNotContain(SecretMarker);
        import.Combined.ShouldNotContain(SecretMarker);
    }

    private static Task<RealHttpHost> StartAsync()
        => RealHttpHost.StartAsync(configureTracon: static tracon => tracon.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage()));

    private static async Task SeedAsync(RealHttpHost host, string name, string instructions)
        => _ = await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(new AgentDefinition
        {
            Name = name,
            Instructions = instructions,
            Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
        });

    private static async Task ExportAsync(RealHttpHost host, TempFile file)
    {
        var result = await CliRunner.RunAsync("export", "--url", host.BaseAddress.ToString(), "--output", file.Path);

        result.ExitCode.ShouldBe(0, result.Combined);
    }

    private static string BundleWithAgent(
        string name,
        string[]? skillNames = null,
        string[]? toolNames = null,
        (string Name, string Endpoint)? mcpServer = null)
    {
        var agent = new JsonObject
        {
            ["name"] = name,
            ["instructions"] = "Do it.",
            ["model"] = new JsonObject { ["provider"] = "echo", ["model"] = "echo-1" },
            ["skillNames"] = new JsonArray([.. (skillNames ?? []).Select(static skill => (JsonNode?)JsonValue.Create(skill))]),
            ["toolNames"] = new JsonArray([.. (toolNames ?? []).Select(static tool => (JsonNode?)JsonValue.Create(tool))]),
        };

        var servers = new JsonArray();

        if (mcpServer is { } server)
        {
            servers.Add(new JsonObject
            {
                ["name"] = server.Name,
                ["definition"] = new JsonObject { ["endpoint"] = server.Endpoint, ["requiresApproval"] = true },
                ["headerNames"] = new JsonArray(),
            });
        }

        return new JsonObject
        {
            ["format"] = "tracon.definitions",
            ["formatVersion"] = 1,
            ["exportedAt"] = "2026-10-29T12:00:00Z",
            ["agents"] = new JsonArray(new JsonObject { ["sourceVersion"] = 1, ["definition"] = agent }),
            ["workflows"] = new JsonArray(),
            ["skills"] = new JsonArray(),
            ["mcpServers"] = servers,
        }.ToJsonString();
    }

    private static async Task<(int ExitCode, byte[] Output, string Error)> RunProcessAsync(params string[] arguments)
    {
        var assembly = typeof(Program).Assembly.Location;

        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(assembly);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = await process.StandardError.ReadToEndAsync();

        await copy;
        await process.WaitForExitAsync();

        return (process.ExitCode, output.ToArray(), error);
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tracon-cli-{Guid.NewGuid():N}.json");

        public void Dispose()
        {
            File.Delete(Path);
            File.Delete(Path + ".out");
        }
    }
}
