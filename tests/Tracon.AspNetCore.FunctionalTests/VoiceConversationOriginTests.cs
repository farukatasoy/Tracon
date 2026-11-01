using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A conversation socket that an authorization policy authenticates is still judged
/// by the page that opens it.
/// </summary>
public sealed class VoiceConversationOriginTests
{
    private const string Policy = "TraconAccess";
    private const string ConsoleOrigin = "https://console.example";

    [Fact]
    public async Task Socket_opened_by_a_page_of_another_site_is_refused()
    {
        await using var host = await StartAsync();

        var connect = async () => await ConnectAsync(host, "https://attacker.example");

        var error = await connect.ShouldThrowAsync<InvalidOperationException>();

        error.Message.ShouldContain("403");
    }

    [Fact]
    public async Task Socket_opened_by_a_page_of_the_same_origin_opens()
    {
        await using var host = await StartAsync();

        using var socket = await ConnectAsync(host, "http://localhost");

        socket.State.ShouldBe(WebSocketState.Open);
    }

    [Fact]
    public async Task Socket_opened_by_a_page_of_an_allowed_origin_opens()
    {
        await using var host = await StartAsync(static options => options.AllowedOrigins.Add(ConsoleOrigin));

        using var socket = await ConnectAsync(host, ConsoleOrigin);

        socket.State.ShouldBe(WebSocketState.Open);
    }

    [Fact]
    public async Task Socket_with_no_origin_opens_for_a_client_that_is_not_a_browser()
    {
        await using var host = await StartAsync();

        using var socket = await ConnectAsync(host, origin: null);

        socket.State.ShouldBe(WebSocketState.Open);
    }

    private static Task<TraconTestHost> StartAsync(Action<TraconEndpointOptions>? configure = null)
    {
        var provider = new StubVoiceProvider();

        return TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition("code-agent"))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureEndpoints: options =>
            {
                options.RequireAuthorization(Policy);
                configure?.Invoke(options);
            },
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
                TestAuthenticationHandler.Add(services)
                    .AddAuthorizationBuilder()
                    .AddPolicy(Policy, static policy => policy.RequireAuthenticatedUser());
            });
    }

    private static async Task<WebSocket> ConnectAsync(TraconTestHost host, string? origin)
    {
        var client = host.CreateWebSocketClient();
        client.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);
        client.ConfigureRequest = request =>
        {
            if (origin is not null)
            {
                request.Headers.Origin = origin;
            }
        };

        return await client.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);
    }
}
