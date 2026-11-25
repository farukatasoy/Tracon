using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host that has its own sign-in (a JWT it issued) can have a browser carry that token to the conversation socket in a
/// second subprotocol of its own, and not in the address.
/// </summary>
/// <remarks>
/// <para>
/// A browser cannot set an <c>Authorization</c> header on a WebSocket handshake. Tracon's own token travels as
/// <c>tracon.token.&lt;token&gt;</c> and is checked against Tracon's credentials; the host's token travels under a prefix the
/// host chooses, and the host's authentication reads it from the <c>Sec-WebSocket-Protocol</c> header. The host's sign-in is
/// what the endpoint's authorization policy asks, so Tracon reads no part of that token, and its own checks (the origin of
/// the page, the run gate) still apply to the socket.
/// </para>
/// <para>
/// 🚨 The cases read what the HOST's handler saw and what the SERVER SELECTED and WROTE back: a token that is echoed in the
/// selected subprotocol, a frame or a log line is a token a proxy has logged.
/// </para>
/// </remarks>
public sealed class VoiceHostCredentialTests
{
    private const string Agent = "voice-agent";
    private const string Policy = "HostAccess";
    private const string HostPrefix = "host.bearer.";
    private const string TokenStem = "canary-host-jwt";
    private const string HostToken = TokenStem + "-a4f19c07e2";
    private const string StreamPath = "http://localhost/tracon/api/voice/sessions/session-1/stream";

    [Fact]
    public async Task A_host_token_in_its_own_subprotocol_opens_the_conversation_and_is_written_back_nowhere()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        var seen = new System.Text.StringBuilder();

        using (var socket = await ConnectAsync(host, HostPrefix + HostToken))
        {
            // The server selects the protocol of the conversation, and never the one that carries the secret.
            socket.SubProtocol.ShouldBe(VoiceConversationProtocol.SubProtocol);
            seen.AppendLine("selected subprotocol: " + socket.SubProtocol);

            await using var client = new VoiceConversationClient(socket);

            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

            var frames = new List<VoiceEvent>();

            await client.WaitForAsync("ready", frames);
            await client.SendAudioAsync(new byte[3200]);
            await client.SendControlAsync(new { type = "commit" });
            await client.WaitForAsync("done", frames);

            foreach (var frame in frames)
            {
                seen.AppendLine(frame.Json?.ToString() ?? "<audio>");
            }
        }

        // The identity the host's own sign-in established is the identity every question of the conversation was asked about.
        handler.RunRequests.ShouldNotBeEmpty();
        handler.RunRequests.Select(static request => request.UserId).Distinct(StringComparer.Ordinal).ShouldBe(["alice"]);

        SecretHits.Find(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frames and the selected subprotocol"] = seen.ToString(),
                ["log lines"] = host.Logs.AllText,
            },
            [new("host token", TokenStem)]).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a-token-the-host-does-not-know")]
    public async Task A_missing_or_unknown_host_token_is_refused_by_the_host_before_the_socket_opens(string? token)
    {
        await using var host = await StartAsync(new RecordingHandler());

        var connect = async () => await ConnectAsync(host, token is null ? null : HostPrefix + token);

        (await connect.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("401");
    }

    [Fact]
    public async Task A_value_in_the_prefix_of_Tracons_own_token_is_judged_by_Tracon_even_when_the_host_accepts_the_socket()
    {
        // The host's own token is valid and the host's sign-in lets the socket through: the only thing left to refuse it is
        // Tracon, which reads its own prefix and finds a value that is none of its credentials.
        await using var host = await StartAsync(new RecordingHandler());

        var connect = async () =>
        {
            var socketClient = host.CreateWebSocketClient();
            socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);
            socketClient.SubProtocols.Add(HostPrefix + HostToken);
            socketClient.SubProtocols.Add(VoiceConversationProtocol.TokenSubProtocolPrefix + "not-a-credential-of-tracon");

            return await socketClient.ConnectAsync(new Uri(StreamPath), TestContext.Current.CancellationToken);
        };

        (await connect.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("401");
    }

    [Fact]
    public async Task The_host_token_under_the_prefix_of_Tracons_own_token_is_not_the_hosts_to_carry()
    {
        // The host's sign-in does not find its own prefix, so it authenticates nobody and its policy refuses. The prefix of
        // Tracon's token is the one place the host's token cannot travel, which is why the host names its own.
        await using var host = await StartAsync(new RecordingHandler());

        var connect = async () => await ConnectAsync(host, VoiceConversationProtocol.TokenSubProtocolPrefix + HostToken);

        (await connect.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("401");
    }

    [Fact]
    public async Task Two_sockets_with_two_host_identities_are_each_asked_about_their_own_caller()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler);

        using var aliceSocket = await ConnectAsync(host, HostPrefix + HostToken, session: "alice-session");
        using var bobSocket = await ConnectAsync(host, HostPrefix + HostToken + "-bob", session: "bob-session");

        await using var alice = new VoiceConversationClient(aliceSocket);
        await using var bob = new VoiceConversationClient(bobSocket);

        foreach (var client in new[] { alice, bob })
        {
            await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
            await client.WaitForAsync("ready");
        }

        foreach (var client in new[] { alice, bob })
        {
            await client.SendAudioAsync(new byte[3200]);
            await client.SendControlAsync(new { type = "commit" });
            await client.WaitForAsync("done");
        }

        foreach (var request in handler.RunRequests)
        {
            request.UserId.ShouldBe(
                string.Equals(request.SessionId, "alice-session", StringComparison.Ordinal) ? "alice" : "bob",
                $"the caller asked about for {request.SessionId}");
        }

        handler.RunRequests.Count.ShouldBe(4, "a start and a turn for each");
    }

    [Fact]
    public async Task On_a_real_socket_the_server_selects_the_protocol_and_the_host_token_opens_it_or_is_refused()
    {
        await using var host = await KestrelVoiceHost.StartAsync(
            configureServices: ConfigureHostSignIn,
            configureEndpoints: static options => options.RequireAuthorization(Policy));

        var (socket, client) = await host.OpenAsync(extraSubProtocol: HostPrefix + HostToken);

        await using (client)
        {
            // What a real client reads from the response of the handshake: the protocol, and never the token's subprotocol.
            socket.SubProtocol.ShouldBe(VoiceConversationProtocol.SubProtocol);

            await client.SendControlAsync(new { type = "start", agent = KestrelVoiceHost.Agent, inputFormat = "pcm16" });
            await client.WaitForAsync("ready");
        }

        var refused = async () => await host.OpenAsync(extraSubProtocol: HostPrefix + "not-a-token-the-host-issued");

        (await refused.ShouldThrowAsync<WebSocketException>()).Message.ShouldContain("401");
    }

    [Fact]
    public async Task A_socket_the_hosts_sign_in_authenticates_is_still_judged_by_the_page_that_opens_it()
    {
        // No Tracon credential is presented, so the identity is the host's, and a page of another site can open a socket with a
        // token it holds only if it holds one; a cookie-like ambient identity is the case this guards, and the origin rule is
        // the same for both because Tracon cannot tell them apart.
        await using var host = await StartAsync(new RecordingHandler());

        var foreign = async () => await ConnectAsync(host, HostPrefix + HostToken, origin: "https://attacker.example");

        (await foreign.ShouldThrowAsync<InvalidOperationException>()).Message.ShouldContain("403");

        using var own = await ConnectAsync(host, HostPrefix + HostToken, origin: "http://localhost");

        own.State.ShouldBe(WebSocketState.Open);
    }

    [Fact]
    public async Task The_host_token_does_not_replace_the_questions_a_run_start_asks()
    {
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny("Not on your plan.") };

        await using var host = await StartAsync(handler);

        using var socket = await ConnectAsync(host, HostPrefix + HostToken);
        await using var client = new VoiceConversationClient(socket);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        (await client.WaitForAsync("error")).Text("message").ShouldBe("Not on your plan.");
    }

    private static Task<TraconTestHost> StartAsync(RecordingHandler handler)
    {
        var voice = new StubVoiceProvider();

        return TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition(Agent))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureEndpoints: static options => options.RequireAuthorization(Policy),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
                services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler));
                ConfigureHostSignIn(services);
            });
    }

    private static void ConfigureHostSignIn(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext, ClaimsAttribution>());
        services.AddHttpContextAccessor();

        // The host's own sign-in: the scheme reads the token from the subprotocol header, the way a JwtBearer
        // `OnMessageReceived` handler would, and builds the identity from it.
        services
            .AddAuthentication(HostSignIn.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HostSignIn>(HostSignIn.SchemeName, configureOptions: null);

        services
            .AddAuthorizationBuilder()
            .AddPolicy(Policy, static policy => policy.AddAuthenticationSchemes(HostSignIn.SchemeName).RequireAuthenticatedUser());
    }

    private static async Task<WebSocket> ConnectAsync(TraconTestHost host, string? hostSubProtocol, string? origin = null, string session = "session-1")
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        if (hostSubProtocol is not null)
        {
            socketClient.SubProtocols.Add(hostSubProtocol);
        }

        if (origin is not null)
        {
            socketClient.ConfigureRequest = request => request.Headers.Origin = origin;
        }

        return await socketClient.ConnectAsync(
            new Uri($"http://localhost/tracon/api/voice/sessions/{session}/stream"),
            TestContext.Current.CancellationToken);
    }

    /// <summary>The host's sign-in: reads <c>host.bearer.&lt;token&gt;</c> from the subprotocol header.</summary>
    private sealed class HostSignIn(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HostSignIn";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? token = null;

            foreach (var header in Request.Headers.SecWebSocketProtocol)
            {
                foreach (var entry in (header ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (entry.StartsWith(HostPrefix, StringComparison.Ordinal))
                    {
                        token = entry[HostPrefix.Length..];
                    }
                }
            }

            if (token is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            // The host issued two tokens: Alice's, and Bob's, which is Alice's with a suffix.
            var user = string.Equals(token, HostToken, StringComparison.Ordinal) ? "alice"
                : string.Equals(token, HostToken + "-bob", StringComparison.Ordinal) ? "bob"
                : null;

            if (user is null)
            {
                return Task.FromResult(AuthenticateResult.Fail("The token is not one the host issued."));
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>Names the caller after the claim the host's sign-in set.</summary>
    private sealed class ClaimsAttribution(IHttpContextAccessor accessor) : IRunAttributionContext
    {
        public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class RecordingHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();
        private readonly List<RunAuthorizationRequest> _runRequests = [];

        public Func<RunAuthorizationRequest, RunAuthorizationResult>? OnRun { get; set; }

        public IReadOnlyList<RunAuthorizationRequest> RunRequests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _runRequests];
                }
            }
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _runRequests.Add(request);
            }

            return ValueTask.FromResult(OnRun?.Invoke(request) ?? RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());
    }
}
