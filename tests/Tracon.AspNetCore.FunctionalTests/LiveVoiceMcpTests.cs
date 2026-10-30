using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A live voice session follows a change to the MCP server its agent's tool comes
/// from on its next delegation.
/// </summary>
/// <remarks>
/// <para>
/// The session used to resolve its agent once, when it was created, and run every
/// delegation with that object for the whole connection. An MCP tool object calls
/// the connection it was discovered on, so after the server was edited or deleted
/// mid-session every later delegation called a closed connection; after approval was
/// switched back on, it ran the tool without asking. <c>McpToolCacheTests</c> closed
/// the same defect for the request path; a session is the long-lived holder
/// that path never sees.
/// </para>
/// <para>
/// Each delegation now resolves the agent from the catalog under the session's
/// tenant: a cache hit while nothing changed, a fresh compile against the live
/// connection after a change.
/// </para>
/// </remarks>
public sealed class LiveVoiceMcpTests
{
    private const string ServerName = "voice-srv";
    private const string ToolName = ServerName + "_lookup";
    private const string AgentName = "voice-mcp-agent";
    private const string ProviderName = "voice-mcp-model";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n";

    [Fact]
    public async Task A_delegation_after_the_server_moves_calls_the_new_endpoint()
    {
        await using var first = await LookupMcpServer.StartAsync("voice-from-a");
        await using var second = await LookupMcpServer.StartAsync("voice-from-b");
        await using var provider = await FakeGptLiveServer.StartAsync();
        var model = new Tracon.Testing.FakeModelProvider(ProviderName).EchoesLastToolResult();
        await using var host = await StartAsync(provider, model);

        await SaveServerAsync(host, first.Endpoint, requiresApproval: false);
        await SaveAgentAsync(host);

        using (var created = await host.Client.PostAsJsonAsync(
                   "/tracon/api/voice/live/sessions",
                   new LiveVoiceSessionCreateRequest { SessionId = "voice-mcp-1", Agent = AgentName, Sdp = Sdp },
                   TestContext.Current.CancellationToken))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        }

        await provider.WaitForAttachAsync(LiveVoiceTests.Patience);

        model.CallsTool(ToolName);
        await provider.SendInputTranscriptAsync(" Look the value up", 1000, 1500);
        await provider.SendDelegationAsync("item_1", 1000);

        // 🚨 Wait for what the assertion reads (the spoken text), not for the run row:
        // the run is recorded as finished while its last append is still on the way.
        await WaitForAsync(() => Spoken(provider).Contains("voice-from-a", StringComparison.Ordinal));

        await SaveServerAsync(host, second.Endpoint, requiresApproval: false);

        model.CallsTool(ToolName);
        await provider.SendInputTranscriptAsync(" Look it up again", 2000, 2500);
        await provider.SendDelegationAsync("item_2", 2000);

        // Either outcome of the second delegation ends the wait: the new endpoint's
        // answer, or the closed connection's failure the defect produced.
        await WaitForAsync(() => SecondAnswer(provider) is not null);

        Spoken(provider).ShouldContain("voice-from-b", customMessage: Spoken(provider));
        first.Calls.ShouldBe(1, "only the delegation before the move reaches endpoint A");
        second.Calls.ShouldBe(1);
    }

    private static string Spoken(FakeGptLiveServer provider)
        => string.Join(' ', provider.AppendsOf("session.commentary.append"))
            + " " + string.Join(' ', provider.AppendsOf("session.thinking.append"));

    /// <summary>What the second delegation said, once it said anything that ends it.</summary>
    private static string? SecondAnswer(FakeGptLiveServer provider)
    {
        var spoken = Spoken(provider);

        return new[] { "voice-from-b", "Function failed", "could not be completed" }
            .FirstOrDefault(marker => spoken.Contains(marker, StringComparison.Ordinal));
    }

    private static Task WaitForAsync(Func<bool> condition, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string description = "")
        => WaitUntil.TrueAsync(condition, description, LiveVoiceTests.Patience);

    private static Task<TraconTestHost> StartAsync(FakeGptLiveServer provider, Tracon.Testing.FakeModelProvider model)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(model)
                .UseMcp()
                .UseOpenAI(options =>
                {
                    options.ApiKey = "test-openai-key";
                    options.Endpoint = provider.BaseAddress;
                })
                .UseOpenAILive(options => options.Endpoint = provider.BaseAddress)
                .UseLiveVoice(static options => options.PersistTranscript = true),
            configureServices: static services =>
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true));

    /// <summary>Saves the server and refreshes the discovered tools.</summary>
    private static async Task SaveServerAsync(TraconTestHost host, Uri endpoint, bool requiresApproval)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative),
                   new { endpoint = endpoint.ToString(), requiresApproval }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    }

    private static async Task SaveAgentAsync(TraconTestHost host)
    {
        using var saved = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new AgentDefinitionRequest
            {
                Name = AgentName,
                Instructions = "Look the value up.",
                Model = new ModelBinding { Provider = ProviderName, Model = "lookup" },
                ToolNames = [ToolName],
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
    }
}
