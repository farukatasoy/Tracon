using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The conversation socket judges an API key by the scope the endpoint declares,
/// exactly as the HTTP endpoints do.
/// </summary>
/// <remarks>
/// 🚨 The socket authenticates its key by hand, because the credential travels in
/// the subprotocol and the endpoint filter never sees it. That path used to check
/// the key and nothing else: a key that could only read agents opened a
/// conversation that runs them. The HTTP twin of this surface needs the
/// <c>RunsWrite</c> scope.
/// </remarks>
public sealed class VoiceConversationScopeTests
{
    private const string Agent = "code-agent";

    [Fact]
    public async Task Key_with_the_runs_write_scope_opens_the_conversation()
    {
        await using var host = await StartAsync();
        var key = await CreateKeyAsync(host, ApiKeyScope.RunsWrite);

        using var socket = await ConnectAsync(host, key);

        socket.State.ShouldBe(WebSocketState.Open);
    }

    [Theory]
    [InlineData(ApiKeyScope.AgentsRead)]
    [InlineData(ApiKeyScope.RunsRead)]
    [InlineData(ApiKeyScope.PlatformRead)]
    public async Task Key_without_the_runs_write_scope_is_refused_before_the_upgrade(ApiKeyScope scope)
    {
        await using var host = await StartAsync();
        var key = await CreateKeyAsync(host, scope);

        var connect = async () => await ConnectAsync(host, key);

        var error = await connect.ShouldThrowAsync<InvalidOperationException>();

        error.Message.ShouldContain("403");
    }

    [Fact]
    public async Task Unknown_key_is_still_refused_as_unauthenticated()
    {
        await using var host = await StartAsync();

        var connect = async () => await ConnectAsync(host, "not-a-key");

        var error = await connect.ShouldThrowAsync<InvalidOperationException>();

        error.Message.ShouldContain("401");
    }

    private static Task<TraconTestHost> StartAsync()
    {
        var provider = new StubVoiceProvider();

        return TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            });
    }

    private static async Task<string> CreateKeyAsync(TraconTestHost host, ApiKeyScope scope)
    {
        var store = host.Services.GetRequiredService<IApiKeyStore>();

        var created = await store.CreateAsync(
            new ApiKeyDraft { TenantId = "default", Name = "voice", Scopes = [scope] },
            TestContext.Current.CancellationToken);

        return created.PlaintextKey;
    }

    private static async Task<WebSocket> ConnectAsync(TraconTestHost host, string token)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);
        socketClient.SubProtocols.Add(VoiceConversationProtocol.TokenSubProtocolPrefix + token);

        return await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);
    }
}
