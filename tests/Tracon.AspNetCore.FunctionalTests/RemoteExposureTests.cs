using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An anonymous request is admitted only where the zero-configuration default
/// promised it: a browser-free caller on the same machine.
/// </summary>
/// <remarks>
/// <para>
/// Three holes shared one cause — "no credential" was treated as "local
/// operator" without proving the request was local:
/// </para>
/// <list type="number">
/// <item><description>
/// <c>AllowRemoteAccess = true</c> with no <c>AuthToken</c> and no policy
/// admitted every remote request as Admin.
/// </description></item>
/// <item><description>
/// A reverse proxy on the same machine (TCP loopback or a Unix socket) made
/// every internet request look local.
/// </description></item>
/// <item><description>
/// A browser page on another site could reach the local API: through DNS
/// rebinding (a foreign <c>Host</c>) or a cross-site request (a foreign
/// <c>Origin</c>), including a cross-site WebSocket.
/// </description></item>
/// </list>
/// </remarks>
public sealed class RemoteExposureTests
{
    private const string RemoteAddress = "203.0.113.5";
    private const string AgentsPath = "/tracon/api/agents";

    private static Task<TraconTestHost> StartAsync(Action<TraconEndpointOptions>? configureEndpoints = null)
        => TraconTestHost.StartAsync(configureEndpoints: configureEndpoints);

    private static HttpRequestMessage Get(string uri, string? remote = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, UriKind.RelativeOrAbsolute));

        if (remote is not null)
        {
            request.Headers.Add(TraconTestHost.RemoteIpHeader, remote);
        }

        return request;
    }

    // ---- Remote access without an authentication method --------------------------

    [Fact]
    public async Task Remote_request_without_a_credential_is_rejected_when_no_authentication_is_configured()
    {
        await using var host = await StartAsync(static options => options.AllowRemoteAccess = true);

        using var request = Get(AgentsPath, remote: RemoteAddress);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain(nameof(TraconEndpointOptions.AuthToken));
        body.ShouldContain(nameof(TraconEndpointOptions.RequireAuthorization));
    }

    [Fact]
    public async Task Remote_request_with_an_api_key_passes_when_keys_are_the_only_method()
    {
        await using var host = await StartAsync(static options => options.AllowRemoteAccess = true);

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "remote-reader", "AgentsRead");

        using var request = Get(AgentsPath, remote: RemoteAddress);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remote_request_is_left_to_the_authorization_policy_when_one_is_configured()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options =>
            {
                options.AllowRemoteAccess = true;
                options.RequireAuthorization("TraconAccess");
            },
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy("TraconAccess", static policy => policy.RequireAuthenticatedUser()));

        using var request = Get(AgentsPath, remote: RemoteAddress);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Loopback_request_without_a_credential_keeps_working_with_remote_access_on()
    {
        await using var host = await StartAsync(static options => options.AllowRemoteAccess = true);

        using var request = Get(AgentsPath);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The meta endpoint never promised loopback; the UI reads it to pick a sign-in method.</summary>
    [Fact]
    public async Task Meta_endpoint_stays_reachable_from_a_remote_address()
    {
        await using var host = await StartAsync(static options => options.AllowRemoteAccess = true);

        using var request = Get("/tracon/api/meta", remote: RemoteAddress);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Reverse proxy on the same machine ---------------------------------------

    public static TheoryData<string, string> ForwardedHeaders => new()
    {
        { "X-Forwarded-For", RemoteAddress },
        { "Forwarded", $"for={RemoteAddress};proto=https" },
        { "X-Real-IP", RemoteAddress },
    };

    [Theory]
    [MemberData(nameof(ForwardedHeaders))]
    public async Task Loopback_connection_that_carries_a_forwarded_header_counts_as_remote(string header, string value)
    {
        await using var host = await StartAsync();

        using var request = Get(AgentsPath);
        request.Headers.TryAddWithoutValidation(header, value);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ForwardedHeaders");
    }

    [Theory]
    [MemberData(nameof(ForwardedHeaders))]
    public async Task Proxied_request_without_a_credential_is_rejected_with_remote_access_on(string header, string value)
    {
        await using var host = await StartAsync(static options => options.AllowRemoteAccess = true);

        using var request = Get(AgentsPath);
        request.Headers.TryAddWithoutValidation(header, value);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Browser pages on another site -------------------------------------------

    [Theory]
    [InlineData("http://localhost/tracon/api/agents")]
    [InlineData("http://localhost:5080/tracon/api/agents")]
    [InlineData("http://127.0.0.1/tracon/api/agents")]
    [InlineData("http://127.0.0.2:5080/tracon/api/agents")]
    [InlineData("http://[::1]:5080/tracon/api/agents")]
    [InlineData("http://app.localhost/tracon/api/agents")]
    public async Task Anonymous_loopback_request_to_a_loopback_host_passes(string uri)
    {
        await using var host = await StartAsync();

        using var request = Get(uri);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// DNS rebinding: the attacker's name resolves to 127.0.0.1, so the socket is
    /// loopback, but the browser still sends the attacker's name as the host.
    /// </summary>
    [Theory]
    [InlineData("http://attacker.example/tracon/api/agents")]
    [InlineData("http://127.0.0.1.nip.io/tracon/api/agents")]
    [InlineData("http://0.0.0.0/tracon/api/agents")]
    public async Task Anonymous_loopback_request_to_a_foreign_host_is_rejected(string uri)
    {
        await using var host = await StartAsync();

        using var request = Get(uri);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("null")]
    public async Task Anonymous_loopback_request_from_a_foreign_origin_is_rejected(string origin)
    {
        await using var host = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/retention/run", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Origin", origin);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:5080")]
    public async Task Anonymous_loopback_request_from_a_loopback_origin_passes(string origin)
    {
        await using var host = await StartAsync();

        using var request = Get(AgentsPath);
        request.Headers.TryAddWithoutValidation("Origin", origin);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A page on another site cannot present the token, so the host and origin
    /// rules guard only the anonymous grant.
    /// </summary>
    [Fact]
    public async Task Request_with_a_valid_token_is_not_judged_by_host_or_origin()
    {
        await using var host = await StartAsync(static options => options.AuthToken = "exposure-static-token-value");

        using var request = Get("http://tracon.internal.example/tracon/api/agents");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "exposure-static-token-value");
        request.Headers.TryAddWithoutValidation("Origin", "https://console.internal.example");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- The voice WebSocket -----------------------------------------------------

    private static Task<TraconTestHost> StartVoiceAsync(bool allowRemoteAccess = false)
    {
        var provider = new StubVoiceProvider();

        return TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition("code-agent"))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureEndpoints: options => options.AllowRemoteAccess = allowRemoteAccess,
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            });
    }

    private static Task<System.Net.WebSockets.WebSocket> ConnectVoiceAsync(
        TraconTestHost host,
        string? origin = null,
        string? remote = null)
    {
        var client = host.CreateWebSocketClient();
        client.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);
        client.ConfigureRequest = request =>
        {
            if (origin is not null)
            {
                request.Headers.Origin = origin;
            }

            if (remote is not null)
            {
                request.Headers[TraconTestHost.RemoteIpHeader] = remote;
            }
        };

        return client.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A WebSocket is not bound by CORS: a page on another site can open one to
    /// the local API directly. Only the origin tells the two apart.
    /// </summary>
    [Fact]
    public async Task Cross_site_voice_socket_is_rejected()
    {
        await using var host = await StartVoiceAsync();

        var connect = () => ConnectVoiceAsync(host, origin: "https://attacker.example");

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Same_machine_voice_socket_still_opens()
    {
        await using var host = await StartVoiceAsync();

        using var socket = await ConnectVoiceAsync(host, origin: "http://localhost:5080");

        socket.State.ShouldBe(System.Net.WebSockets.WebSocketState.Open);
    }

    [Fact]
    public async Task Remote_voice_socket_without_a_credential_is_rejected_when_no_authentication_is_configured()
    {
        await using var host = await StartVoiceAsync(allowRemoteAccess: true);

        var connect = () => ConnectVoiceAsync(host, remote: RemoteAddress);

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }
}
