using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The output budget over a <strong>real</strong> MCP tool — an
/// <see cref="McpClientTool"/> returned by the SDK's own client, talking the
/// real protocol to an in-process server.
/// </summary>
/// <remarks>
/// 🚨 These tests exist because the unit-level ones could not see the defect
/// they were written for (HATA-S1-026). They wrapped
/// <c>AIFunctionFactory.Create(() =&gt; new string('a', 10_000))</c> — a
/// <see langword="string"/>-returning stand-in — and proved only that the
/// wrapper was installed. A real MCP tool answers with
/// <see cref="AIContent"/> blocks instead, which the trimming layer skipped
/// outright, so a 200-byte limit let 8 KB through. The shape of an MCP
/// result is the whole defect; it must come from the SDK, not from a fake.
/// </remarks>
public sealed class McpToolResultTruncationTests
{
    [Fact]
    public async Task A_real_mcp_tool_result_over_the_limit_is_truncated()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconMcpServer());

        await using var client = await ConnectAsync(host);

        var tool = (await client.ListToolsAsync()).ShouldHaveSingleItem();
        var bounded = new TruncatingAIFunction(tool, maxOutputBytes: 200);

        // The fake model echoes the user message, so the agent's answer — and
        // with it the MCP tool result — is as long as this.
        var result = await bounded.InvokeAsync(Message(new string('a', 8000)));

        var text = result.ShouldBeOfType<string>();
        Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(200);

        var envelope = JsonDocument.Parse(text).RootElement;
        envelope.GetProperty("truncated").GetBoolean().ShouldBeTrue();
        envelope.GetProperty("omittedBytes").GetInt32().ShouldBeGreaterThan(7000);
    }

    [Fact]
    public async Task A_real_mcp_tool_result_within_the_limit_reaches_the_model_intact()
    {
        // Measuring a result must not cost it: one that fits is handed on as
        // the tool produced it, blocks and all. (The multi-block result that
        // used to be replaced by {"error":"tool_result_unsupported"} outright
        // is pinned in TruncatingAIFunctionTests — Tracon's own MCP server
        // answers with a single block and cannot reach that case.)
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddAgent(TestData.Definition())
                .UseMcpServer(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapTraconMcpServer());

        await using var client = await ConnectAsync(host);

        var tool = (await client.ListToolsAsync()).ShouldHaveSingleItem();
        var bounded = new TruncatingAIFunction(tool, maxOutputBytes: 4096);

        var result = await bounded.InvokeAsync(Message("ping-1234"));

        result.ShouldNotBe(ToolResultText.UnsupportedResultText);
        Describe(result).ShouldContain("ping-1234", Case.Sensitive);
    }

    private static AIFunctionArguments Message(string message)
        => new(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["message"] = message },
            StringComparer.Ordinal);

    /// <summary>The text a provider adapter sends for this tool result.</summary>
    private static string Describe(object? result)
        => result as string
           ?? JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));

    private static async Task<McpClient> ConnectAsync(TraconTestHost host)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(host.Client.BaseAddress!, "/tracon/mcp") },
            host.Client,
            ownsHttpClient: false);

        return await McpClient.CreateAsync(
            transport,
            // F-190's rationale applies here too: the SDK's 5s production
            // default can be missed by this in-memory server under the full
            // package run's CPU contention.
            new McpClientOptions { DiscoverProbeTimeout = TimeSpan.FromSeconds(30) });
    }
}
