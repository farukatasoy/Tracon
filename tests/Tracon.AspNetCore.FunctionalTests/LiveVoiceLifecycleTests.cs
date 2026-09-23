using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

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

    /// <summary>How long a wait may take before it is reported as a hang (see <see cref="LiveVoiceTests.Patience"/>).</summary>
    private static readonly TimeSpan Patience = LiveVoiceTests.Patience;

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

        // The close returns as soon as the session is torn down; the history write
        // that follows it is the server's own work, not the caller's. Reading the
        // store once raced that write - measured twice in a full-solution run,
        // while the assembly on its own passed all 1046 times. Wait for the write
        // the way every other timing-dependent assertion in this file does.
        //
        // 🚨 Wait for BOTH strings the assertions below read. Waiting only for
        // the output transcript let the wait return before the INPUT transcript
        // had landed, and the first assertion then failed - measured again in the
        // Phase 166 closing run. A wait narrower than the assertion it guards
        // turns a passing test into a scheduled one.
        await WaitForAsync(async () =>
        {
            var history = await HistoryOfAsync(host, "session-history-on");

            return history.Any(text => text.Contains("Checking that now.", StringComparison.Ordinal))
                && history.Any(text => text.Contains("Where is order 442", StringComparison.Ordinal));
        });

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

        // Phase 184: a fixed 300 ms after the inspection stood here. The runner
        // logs the refusal and returns before anything is sent, so the line
        // marks the moment the decision is final.
        await WaitForAsync(() => host.Logs.AllText.Contains(
            "A content guard blocked an append on live voice session",
            StringComparison.Ordinal));

        guard.Inspected.ShouldNotBeEmpty();
        provider.AppendsOf("session.commentary.append").ShouldBeEmpty();

        await CloseAsync(host, voiceSessionId);
    }

    [Fact]
    public async Task A_delegation_over_the_ceiling_opens_NO_run_and_is_told_the_session_is_busy()
    {
        // The provider decides when to delegate. Without the ceiling it would decide
        // how many agent runs Tracon starts, which is a denial-of-service axis
        // pointing straight at the consumer's own model spend.
        var slow = new SlowAgentDecorator();

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options =>
            {
                options.MaxConcurrentDelegations = 1;
                options.DelegationTimeout = TimeSpan.FromSeconds(30);
            },
            configureServices: services => services.AddSingleton<IAgentDecorator>(slow));

        var voiceSessionId = await OpenAsync(host, provider, "session-busy");

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_first", 3800);

        // The first delegation is still running; the second must be refused.
        await slow.Entered.WaitAsync(Patience, TestContext.Current.CancellationToken);
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
        var slow = new SlowAgentDecorator();

        await using var provider = await FakeGptLiveServer.StartAsync();
        await using var host = await LiveVoiceTests.StartAsync(
            provider,
            configureLive: options => options.DelegationTimeout = TimeSpan.FromMinutes(5),
            configureServices: services => services.AddSingleton<IAgentDecorator>(slow));

        var voiceSessionId = await OpenAsync(host, provider, "session-cancel");

        await provider.SendInputTranscriptAsync(" Look up order 442", 3800, 4200);
        await provider.SendDelegationAsync("item_1", 3800);

        await slow.Entered.WaitAsync(Patience, TestContext.Current.CancellationToken);

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

        var voiceSessionId = await OpenAsync(host, provider, "session-provider-close");

        await provider.SendInputTranscriptAsync(" Hello", 600, 800);
        await WaitUntilActiveAsync(host, voiceSessionId);
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

        var voiceSessionId = await OpenAsync(host, provider, "session-dead-socket");

        await provider.SendInputTranscriptAsync(" Hello", 600, 800);
        await WaitUntilActiveAsync(host, voiceSessionId);
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

    private static Task WaitForAsync(
        Func<Task<bool>> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "")
        => WaitUntil.TrueAsync(condition, description, Patience);

    private static Task WaitForAsync(
        Func<bool> condition,
        [CallerArgumentExpression(nameof(condition))] string description = "")
        => WaitUntil.TrueAsync(condition, description, Patience);

    /// <summary>
    /// Waits until the session has seen media. Phase 184: a fixed 200 ms after
    /// the transcript stood here - the end reason depends on whether the host
    /// had already marked the session active when the provider closed it.
    /// </summary>
    private static Task WaitUntilActiveAsync(TraconTestHost host, Guid voiceSessionId)
        => WaitForAsync(
            async () =>
            {
                using var status = await host.Client.GetAsync(
                    $"/tracon/api/voice/live/sessions/{voiceSessionId:D}",
                    TestContext.Current.CancellationToken);

                return string.Equals(
                    (await TraconTestHost.ReadJsonAsync(status)).GetProperty("state").GetString(),
                    "active",
                    StringComparison.Ordinal);
            },
            $"live voice session {voiceSessionId} to become active");

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

    /// <summary>Wraps the agent so a delegated run stays running until it is cancelled.</summary>
    /// <remarks>
    /// Phase 184: the run used to sleep two seconds and each test slept 300 ms
    /// and hoped the delegation had started by then. <see cref="Entered"/> says
    /// when it has; the run then holds until the session cancels it.
    /// </remarks>
    private sealed class SlowAgentDecorator : IAgentDecorator
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once a delegated run has started.</summary>
        public Task Entered => _entered.Task;

        public int Order => 0;

        public Microsoft.Agents.AI.AIAgent Decorate(
            Microsoft.Agents.AI.AIAgent agent,
            AgentDescriptor descriptor)
            => new SlowAgent(agent, _entered);

        private sealed class SlowAgent(Microsoft.Agents.AI.AIAgent inner, TaskCompletionSource entered)
            : Microsoft.Agents.AI.DelegatingAIAgent(inner)
        {
            protected override async IAsyncEnumerable<Microsoft.Agents.AI.AgentResponseUpdate> RunCoreStreamingAsync(
                IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
                Microsoft.Agents.AI.AgentSession? session = null,
                Microsoft.Agents.AI.AgentRunOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated

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
