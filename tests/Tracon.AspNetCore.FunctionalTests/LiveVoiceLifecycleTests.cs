using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What happens to a live session's transcript, its delegations and its record over
/// the session's whole life.
/// </summary>
/// <remarks>
/// These are the paths a unit test cannot reach: the durable history write, the
/// content guard on the way out to the provider, the delegation ceiling, and the
/// end reasons. Each one is a behaviour that is easy to delete without any other
/// test noticing.
/// </remarks>
public sealed class LiveVoiceLifecycleTests
{
    private const string Agent = "code-agent";
    private const string Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\n";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task The_transcript_is_written_to_the_session_history_when_persistence_is_on()
    {
        // 🚨 Without this the whole durable-history path is untested: emptying
        // FlushHistoryAsync would leave every other live voice test green.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.PersistTranscript = true);

        var voiceSessionId = await OpenAsync(host, provider, "session-history-on");

        await provider.SendInputTranscriptAsync(" Where is order 442", 600, 1200);
        await provider.SendOutputTranscriptAsync(" Checking that now.", 1400, 1800);

        await CloseAsync(host, voiceSessionId);

        var messages = await HistoryOfAsync(host, "session-history-on");

        messages.ShouldContain(text => text.Contains("Where is order 442", StringComparison.Ordinal));
        messages.ShouldContain(text => text.Contains("Checking that now.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_transcript_is_NOT_written_when_persistence_is_off()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.PersistTranscript = false);

        var voiceSessionId = await OpenAsync(host, provider, "session-history-off");

        await provider.SendInputTranscriptAsync(" Where is order 442", 600, 1200);
        await provider.SendOutputTranscriptAsync(" Checking that now.", 1400, 1800);

        await CloseAsync(host, voiceSessionId);

        var messages = await HistoryOfAsync(host, "session-history-off");

        messages.ShouldNotContain(text => text.Contains("Where is order 442", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_append_passes_through_the_content_guard_before_it_leaves_the_process()
    {
        // 🚨 The append is the one path on which Tracon's own text reaches the
        // third party. The chat-client decorator that guards model-bound text never
        // sees it, so the guard is called explicitly — and that call is what this
        // test exists to keep.
        var guard = new RecordingContentGuard();

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureServices: services => services.AddSingleton<IContentGuard>(guard));

        var voiceSessionId = await OpenAsync(host, provider, "session-guard");

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await WaitForAsync(() => provider.AppendsOf("session.commentary.append").Count > 0);

        guard.Inspected.ShouldNotBeEmpty();

        await CloseAsync(host, voiceSessionId);
    }

    [Fact]
    public async Task A_blocked_append_is_never_sent_to_the_provider()
    {
        var guard = new RecordingContentGuard { Block = true };

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureServices: services => services.AddSingleton<IContentGuard>(guard));

        var voiceSessionId = await OpenAsync(host, provider, "session-guard-block");

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await WaitForAsync(() => guard.Inspected.Count > 0);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        provider.AppendsOf("session.commentary.append").ShouldBeEmpty();

        await CloseAsync(host, voiceSessionId);
    }

    [Fact]
    public async Task A_delegation_over_the_ceiling_opens_NO_run_and_is_told_the_session_is_busy()
    {
        // The provider decides when to delegate. Without the ceiling it would decide
        // how many agent runs Tracon starts, which is a denial-of-service axis
        // pointing straight at the consumer's own model spend.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options =>
            {
                options.MaxConcurrentDelegations = 1;
                options.DelegationTimeout = TimeSpan.FromSeconds(30);
            },
            configureServices: services => services.AddSingleton<IAgentDecorator>(new SlowAgentDecorator()));

        var voiceSessionId = await OpenAsync(host, provider, "session-busy");

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_first", 3800);

        // The first delegation is still running; the second must be refused.
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await provider.SendDelegationAsync("item_second", 3800);

        await WaitForAsync(() => provider.AppendsOf("session.thinking.append")
            .Any(text => text.Contains("previous request", StringComparison.Ordinal)));

        await CloseAsync(host, voiceSessionId);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var recorded = await runs.QueryRunsAsync(
            new RunQuery { Take = 10 },
            TestContext.Current.CancellationToken);

        // One delegation, one run — the refused one opened nothing.
        recorded.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Closing_a_session_cancels_a_delegation_that_is_still_running()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.DelegationTimeout = TimeSpan.FromMinutes(5),
            configureServices: services => services.AddSingleton<IAgentDecorator>(new SlowAgentDecorator()));

        var voiceSessionId = await OpenAsync(host, provider, "session-cancel");

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await Task.Delay(300, TestContext.Current.CancellationToken);

        // The close must return rather than block on the running delegation.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        using var close = await host.Client.DeleteAsync(
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
            cancellation.Token);

        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var records = await host.Services
            .GetRequiredService<IVoiceSessionStore>()
            .QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken);

        records.ShouldHaveSingleItem().EndedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_session_nothing_ever_connected_to_stays_pending()
    {
        // 🚨 Attaching the sideband does NOT make a session active: the provider
        // answers the attach even when no browser is on the other end. Only an event
        // that needs media proves the peer arrived — and without that distinction
        // VoiceSessionEndReason.Abandoned could never be produced.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        var voiceSessionId = await OpenAsync(host, provider, "session-silent");

        using var status = await host.Client.GetAsync(
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
            TestContext.Current.CancellationToken);

        var body = await TraconTestHost.ReadJsonAsync(status);

        body.GetProperty("state").GetString().ShouldBe("pending");

        await CloseAsync(host, voiceSessionId);
    }

    [Fact]
    public async Task A_provider_close_on_a_session_that_never_carried_media_is_ABANDONED()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        await OpenAsync(host, provider, "session-abandoned");

        // This is what the real provider sends when the media peer never arrives.
        await provider.SendClosedAsync("connection_lost", 15m);

        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        await WaitForAsync(async () =>
        {
            var records = await store.QueryAsync(
                "default",
                new VoiceSessionQuery(),
                TestContext.Current.CancellationToken);

            return records.Count == 1 && records[0].EndReason is not null;
        });

        var record = (await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        // Abandoned, not Error: nothing failed, nobody showed up.
        record.EndReason.ShouldBe(VoiceSessionEndReason.Abandoned);
    }

    [Fact]
    public async Task A_provider_close_AFTER_media_flowed_is_a_PROVIDER_close()
    {
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        await OpenAsync(host, provider, "session-provider-close");

        await provider.SendInputTranscriptAsync(" Hello", 600, 800);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await provider.SendClosedAsync("provider_shutdown", 42m);

        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        await WaitForAsync(async () =>
        {
            var records = await store.QueryAsync(
                "default",
                new VoiceSessionQuery(),
                TestContext.Current.CancellationToken);

            return records.Count == 1 && records[0].EndReason is not null;
        });

        var record = (await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        record.EndReason.ShouldBe(VoiceSessionEndReason.Provider);
        record.LiveSeconds.ShouldBe(42m);
    }

    [Fact]
    public async Task A_sideband_that_dies_without_a_handshake_is_recorded_as_an_ERROR()
    {
        // 🚨 Not as a client close. A socket that simply ends looks exactly like a
        // finished conversation, and an operator investigating a provider outage
        // would find a record saying the caller hung up.
        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(provider);

        await OpenAsync(host, provider, "session-dead-socket");

        await provider.SendInputTranscriptAsync(" Hello", 600, 800);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await provider.KillSidebandAsync();

        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        await WaitForAsync(async () =>
        {
            var records = await store.QueryAsync(
                "default",
                new VoiceSessionQuery(),
                TestContext.Current.CancellationToken);

            return records.Count == 1 && records[0].EndReason is not null;
        });

        (await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem()
            .EndReason.ShouldBe(VoiceSessionEndReason.Error);
    }

    // --- Helpers ---

    private static async Task<Guid> OpenAsync(
        TraconTestHost host,
        FakeGptLiveServer provider,
        string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest { SessionId = sessionId, Agent = Agent, Sdp = Sdp },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await provider.WaitForAttachAsync(Patience);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("voiceSessionId").GetGuid();
    }

    private static async Task CloseAsync(TraconTestHost host, Guid voiceSessionId)
    {
        using var close = await host.Client.DeleteAsync(
            $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
            TestContext.Current.CancellationToken);

        close.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<IReadOnlyList<string>> HistoryOfAsync(TraconTestHost host, string sessionId)
    {
        var sessions = host.Services.GetRequiredService<ISessionStore>();
        var record = await sessions.GetAsync(sessionId, TestContext.Current.CancellationToken);

        return record is null ? [] : [record.State.GetRawText()];
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The condition never became true.");
    }

    private static Task WaitForAsync(Func<bool> condition)
        => WaitForAsync(() => Task.FromResult(condition()));

    /// <summary>A guard that records what it saw and can refuse everything.</summary>
    private sealed class RecordingContentGuard : IContentGuard
    {
        private readonly Lock _gate = new();

        public string Name => "recording";

        public List<string> Inspected { get; } = [];

        public bool Block { get; init; }

        public ValueTask<ContentGuardResult> InspectAsync(
            ContentGuardContext context,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Inspected.Add(context.Text);
            }

            return ValueTask.FromResult(Block
                ? ContentGuardResult.Block("test-rule", "Blocked by the test guard.")
                : ContentGuardResult.Allow);
        }
    }

    /// <summary>Wraps the agent so a delegated run takes long enough to overlap.</summary>
    private sealed class SlowAgentDecorator : IAgentDecorator
    {
        public int Order => 0;

        public Microsoft.Agents.AI.AIAgent Decorate(
            Microsoft.Agents.AI.AIAgent agent,
            AgentDescriptor descriptor)
            => new SlowAgent(agent);

        private sealed class SlowAgent(Microsoft.Agents.AI.AIAgent inner)
            : Microsoft.Agents.AI.DelegatingAIAgent(inner)
        {
            protected override async IAsyncEnumerable<Microsoft.Agents.AI.AgentResponseUpdate> RunCoreStreamingAsync(
                IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
                Microsoft.Agents.AI.AgentSession? session = null,
                Microsoft.Agents.AI.AgentRunOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                await foreach (var update in base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return update;
                }
            }
        }
    }
}
