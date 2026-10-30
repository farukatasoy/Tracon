using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A voice conversation follows a change to the MCP server its agent's tool comes
/// from on its next turn (the class scan of the live voice case).
/// </summary>
/// <remarks>
/// The connection resolved its agent once, at <c>start</c>, and ran every turn
/// with that object. An MCP tool object calls the connection it was discovered on,
/// so after the server moved mid-conversation every later turn called a closed
/// connection and the model heard "Error: Function failed.". Each turn now
/// resolves the agent named at <c>start</c> again.
/// </remarks>
public sealed class VoiceConversationMcpTests
{
    private const string ServerName = "talk-srv";
    private const string ToolName = ServerName + "_lookup";
    private const string AgentName = "talk-mcp-agent";
    private const string ProviderName = "talk-mcp-model";

    [Fact]
    public async Task A_turn_after_the_server_moves_calls_the_new_endpoint()
    {
        await using var first = await LookupMcpServer.StartAsync("talk-from-a");
        await using var second = await LookupMcpServer.StartAsync("talk-from-b");
        var model = new Tracon.Testing.FakeModelProvider(ProviderName).EchoesLastToolResult();
        await using var host = await StartAsync(model);

        await SaveServerAsync(host, first.Endpoint);
        await SaveAgentAsync(host);

        await using var client = await ConnectAsync(host);
        await client.SendControlAsync(new { type = "start", agent = AgentName, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        (await TurnAsync(client, model)).ShouldContain("talk-from-a");

        await SaveServerAsync(host, second.Endpoint);

        var answer = await TurnAsync(client, model);
        answer.ShouldContain("talk-from-b", customMessage: answer);
        first.Calls.ShouldBe(1, "only the turn before the move reaches endpoint A");
        second.Calls.ShouldBe(1);
    }

    /// <summary>Speaks one utterance and returns the agent's streamed text.</summary>
    private static async Task<string> TurnAsync(VoiceConversationClient client, Tracon.Testing.FakeModelProvider model)
    {
        model.CallsTool(ToolName);

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();
        await client.WaitForAsync(VoiceConversationProtocol.ServerDone, frames);

        return string.Concat(frames
            .Where(static frame => string.Equals(frame.Type, VoiceConversationProtocol.ServerText, StringComparison.Ordinal))
            .Select(static frame => frame.Text("delta")));
    }

    private static Task<TraconTestHost> StartAsync(Tracon.Testing.FakeModelProvider model)
    {
        var voice = new StubVoiceProvider();

        return TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(model)
                .UseMcp()
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
            });
    }

    private static async Task<VoiceConversationClient> ConnectAsync(TraconTestHost host)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        var socket = await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/talk-1/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }

    /// <summary>Saves the server and refreshes the discovered tools.</summary>
    private static async Task SaveServerAsync(TraconTestHost host, Uri endpoint)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
                   new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative),
                   new { endpoint = endpoint.ToString(), requiresApproval = false }))
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
