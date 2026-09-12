using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 147 (F-195): the real-time voice conversation endpoint asks the
/// installation's own <see cref="IRunAuthorizationHandler"/> before it upgrades
/// the socket, with <see cref="SessionAccess.Voice"/>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 139 left voice out of scope on purpose, and its handover note said a
/// later phase must approach K-283's case CAREFULLY. That is what these tests
/// lock down. K-283 established that an INVISIBLE session counts as ABSENT:
/// the voice endpoint accepts a session id it has never seen, and the first
/// turn opens it. A gate that rejected unknown sessions would silently break
/// every first conversation, so the handler is asked and the DEFAULT answer
/// still opens the socket.
/// </para>
/// <para>
/// The rejection is the handshake itself: a WebSocket has no 403 body, so a
/// denial is written before the upgrade, with the SAME 404 an unreachable
/// session already produced.
/// </para>
/// </remarks>
public sealed class VoiceAuthorizationTests
{
    private const string Agent = "code-agent";

    [Fact]
    public async Task Nothing_changes_when_no_handler_is_registered()
    {
        await using var host = await StartAsync(handler: null);

        using var socket = await ConnectAsync(host, "session-1");

        socket.State.ShouldBe(WebSocketState.Open);
    }

    [Fact]
    public async Task Allowed_caller_opens_the_conversation()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        await using var host = await StartAsync(handler);

        using var socket = await ConnectAsync(host, "session-1");

        socket.State.ShouldBe(WebSocketState.Open);

        var request = handler.SessionRequests.ShouldHaveSingleItem();
        request.Access.ShouldBe(SessionAccess.Voice);
        request.SessionId.ShouldBe("session-1");
        request.TenantId.ShouldBe("default");
    }

    [Fact]
    public async Task Denied_caller_cannot_open_another_users_conversation()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnSession = static request => request.Access == SessionAccess.Voice
                ? RunAuthorizationResult.Deny("not your conversation")
                : RunAuthorizationResult.Allow(),
        };

        await using var host = await StartAsync(handler);
        await SeedSessionAsync(host, "session-of-a");

        // TestServer surfaces a refused handshake as InvalidOperationException,
        // the same shape a wrong token produces (see VoiceConversationTests).
        var connect = async () => await ConnectAsync(host, "session-of-a");

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Throwing_handler_refuses_the_handshake_fail_closed()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnSession = static _ => throw new InvalidOperationException("the consumer's own policy store is down"),
        };

        await using var host = await StartAsync(handler);

        var connect = async () => await ConnectAsync(host, "session-1");

        await connect.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_unknown_session_still_opens_when_the_handler_allows_it()
    {
        // 🚨 K-283's case, locked down: a session that does not exist yet is
        // NOT an error here — the first turn opens it. If a later change made
        // the gate reject unknown sessions, every first conversation in every
        // installation would stop working, and no other test would notice.
        var handler = new ConfigurableRunAuthorizationHandler();
        await using var host = await StartAsync(handler);

        var sessions = host.Services.GetRequiredService<ISessionStore>();
        (await sessions.GetAsync("never-seen-before", TestContext.Current.CancellationToken)).ShouldBeNull();

        using var socket = await ConnectAsync(host, "never-seen-before");

        socket.State.ShouldBe(WebSocketState.Open);

        // The handler was still ASKED: only the consumer can say whether this
        // caller may open a conversation under this id.
        handler.SessionRequests.ShouldHaveSingleItem().SessionId.ShouldBe("never-seen-before");
    }

    [Fact]
    public async Task A_denial_is_refused_with_404_not_401_or_403()
    {
        var handler = new ConfigurableRunAuthorizationHandler
        {
            OnSession = static _ => RunAuthorizationResult.Deny("no"),
        };

        await using var host = await StartAsync(handler);

        var connect = async () => await ConnectAsync(host, "session-1");

        // 🚨 The status code carried by the refused handshake is 404 - the SAME
        // one an unreachable session gets, written by the same helper. Never
        // 403, which would confirm this conversation exists; never 401, which
        // would send a correctly authenticated caller off to refresh a token
        // that was never the problem.
        var error = await connect.ShouldThrowAsync<InvalidOperationException>();

        error.Message.ShouldContain("404");
    }

    // --- Helpers ---

    private static async Task<WebSocket> ConnectAsync(TraconTestHost host, string sessionId)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        return await socketClient.ConnectAsync(
            new Uri($"http://localhost/tracon/api/voice/sessions/{sessionId}/stream"),
            TestContext.Current.CancellationToken);
    }

    private static async Task SeedSessionAsync(TraconTestHost host, string sessionId)
    {
        var sessions = host.Services.GetRequiredService<ISessionStore>();

        await sessions.SaveAsync(
            new SessionRecord
            {
                Id = sessionId,
                AgentName = Agent,
                State = JsonDocument.Parse("{}").RootElement,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);
    }

    private static Task<TraconTestHost> StartAsync(ConfigurableRunAuthorizationHandler? handler)
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

                if (handler is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler));
                }
            });
    }

    private sealed class ConfigurableRunAuthorizationHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();

        public Func<SessionAuthorizationRequest, RunAuthorizationResult>? OnSession { get; set; }

        public List<SessionAuthorizationRequest> SessionRequests { get; } = [];

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                SessionRequests.Add(request);
            }

            return ValueTask.FromResult(OnSession?.Invoke(request) ?? RunAuthorizationResult.Allow());
        }
    }
}
