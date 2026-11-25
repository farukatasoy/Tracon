using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Tests.Common;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A voice conversation answers to the rules every other way of starting a run answers to: the registered run authorization
/// handler, the session's owner and the tenant's quota are asked when the conversation starts and before every turn.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The handshake asks the session gate before the agent is known, because the agent is named by the <c>start</c> frame
/// that follows. Without these questions a caller who may open a conversation could name any agent in it and run it, and no
/// quota applied to a turn at all. The cases read what the HANDLER saw and what REACHED the model and the speech provider,
/// not what the socket answered: a gate that answers and still lets the work through would pass a test that only reads the
/// answer.
/// </para>
/// </remarks>
public sealed class VoiceRunGateTests
{
    private const string Agent = "voice-agent";
    private const string OtherAgent = "other-agent";
    private const string Provider = "gate-script";

    [Fact]
    public async Task The_handler_is_asked_when_the_conversation_starts_and_before_every_turn_with_who_and_what_it_runs()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler, Model("One.", "Two."), new StubVoiceProvider(), user: "alice");
        await using var client = await ConnectAsync(host, "lesson-7");

        await StartConversationAsync(client);
        await TalkAsync(client);
        await TalkAsync(client);

        var requests = handler.RunRequests;

        requests.Count.ShouldBe(3, "one question at the start and one before each of the two turns");

        foreach (var request in requests)
        {
            request.Access.ShouldBe(RunAccess.Start);
            request.AgentName.ShouldBe(Agent);
            request.SessionId.ShouldBe("lesson-7");
            request.TenantId.ShouldBe("default");
            request.UserId.ShouldBe("alice");
        }
    }

    [Fact]
    public async Task The_handler_is_given_the_tenant_of_the_connection_and_not_the_default_one()
    {
        var handler = new RecordingHandler();

        await using var host = await StartAsync(handler, Model("One."), new StubVoiceProvider(), user: "alice");
        await using var client = await ConnectAsync(host, "lesson-7", tenant: "tenant-b");

        await StartConversationAsync(client);
        await TalkAsync(client);

        handler.RunRequests.Select(static request => request.TenantId).Distinct(StringComparer.Ordinal).ShouldBe(["tenant-b"]);
    }

    [Fact]
    public async Task A_refused_agent_stops_the_start_and_nothing_behind_the_gate_is_reached()
    {
        var handler = new RecordingHandler { OnRun = static _ => RunAuthorizationResult.Deny("Not on your plan.") };
        var model = Model("never");
        var voice = new StubVoiceProvider();
        var resolverCalls = 0;

        await using var host = await StartAsync(
            handler,
            model,
            voice,
            resolve: (_, _) =>
            {
                Interlocked.Increment(ref resolverCalls);

                return ValueTask.FromResult<VoiceConnectionSettings?>(null);
            });
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        (await client.WaitForAsync("error", frames)).Text("message").ShouldBe("Not on your plan.");
        frames.ShouldNotContain(static frame => frame.Type == "ready");
        resolverCalls.ShouldBe(0, "the host's own code is not asked about a connection that is turned away");
        model.Requests.ShouldBeEmpty("no run starts");
        voice.Spoken.ShouldBeEmpty("nothing is said");

        (await WaitForRecordAsync(host)).EndReason.ShouldBe(VoiceSessionEndReason.Error);
    }

    [Fact]
    public async Task A_caller_who_may_open_a_conversation_cannot_run_another_agent_over_it()
    {
        // The case that was measured on a real host: the handshake let the caller in, the `start` frame named any agent, and
        // the agent ran.
        var handler = new RecordingHandler
        {
            OnRun = static request => string.Equals(request.AgentName, Agent, StringComparison.Ordinal)
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny("That agent is not yours."),
        };

        var model = Model("never");

        await using var host = await StartAsync(handler, model, new StubVoiceProvider());
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = OtherAgent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        (await client.WaitForAsync("error", frames)).Text("message").ShouldBe("That agent is not yours.");
        frames.ShouldNotContain(static frame => frame.Type == "ready");
        model.Requests.ShouldBeEmpty();
        handler.RunRequests.ShouldHaveSingleItem().AgentName.ShouldBe(OtherAgent);
    }

    [Fact]
    public async Task A_handler_that_throws_refuses_the_start_with_a_fixed_text()
    {
        var handler = new RecordingHandler { OnRun = static _ => throw new InvalidOperationException("policy-store-secret-detail") };
        var model = Model("never");

        await using var host = await StartAsync(handler, model, new StubVoiceProvider());
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();
        var error = await client.WaitForAsync("error", frames);

        error.Text("message").ShouldBe("The run authorization check failed. Retry the request.");
        error.Text("message")!.ShouldNotContain("policy-store-secret-detail");
        frames.ShouldNotContain(static frame => frame.Type == "ready");
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_turn_refused_after_the_conversation_began_runs_nothing_and_the_conversation_goes_on()
    {
        var allowed = true;
        var handler = new RecordingHandler
        {
            OnRun = _ => Volatile.Read(ref allowed) ? RunAuthorizationResult.Allow() : RunAuthorizationResult.Deny("The plan ended."),
        };

        var model = Model("One.", "Two.");
        var voice = new StubVoiceProvider();

        await using var host = await StartAsync(handler, model, voice);
        await using var client = await ConnectAsync(host);

        await StartConversationAsync(client);
        await TalkAsync(client);

        var heard = voice.ReceivedBytes;

        Volatile.Write(ref allowed, false);

        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();
        var done = await client.WaitForAsync("done", frames);

        frames.Single(static frame => string.Equals(frame.Type, "error", StringComparison.Ordinal)).Text("message").ShouldBe("The plan ended.");
        frames.ShouldNotContain(static frame => frame.Type == "runStarted");
        frames.ShouldNotContain(static frame => frame.Type == "transcript", "the audio of a refused turn is never transcribed");
        voice.ReceivedBytes.ShouldBe(heard, "no audio reached the transcription provider");
        model.Requests.Count.ShouldBe(1, "the refused turn started no run");
        done.Flag("cancelled").ShouldBe(false);
        done.Json!.Value.GetProperty("turn").GetInt32().ShouldBe(1, "the refused turn is not counted");

        // The refusal ended the turn and nothing else: it is not counted, and the next turn is asked again and runs.
        Volatile.Write(ref allowed, true);

        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        (await client.WaitForAsync("done")).Json!.Value.GetProperty("turn").GetInt32().ShouldBe(2);
        model.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_handler_that_throws_before_a_turn_refuses_that_turn_with_a_fixed_text()
    {
        var failing = false;
        var handler = new RecordingHandler
        {
            OnRun = _ => Volatile.Read(ref failing) ? throw new InvalidOperationException("policy-store-secret-detail") : RunAuthorizationResult.Allow(),
        };

        var model = Model("One.");

        await using var host = await StartAsync(handler, model, new StubVoiceProvider());
        await using var client = await ConnectAsync(host);

        await StartConversationAsync(client);

        Volatile.Write(ref failing, true);

        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();

        await client.WaitForAsync("done", frames);

        var error = frames.Single(static frame => string.Equals(frame.Type, "error", StringComparison.Ordinal));

        error.Text("message").ShouldBe("The run authorization check failed. Retry the request.");
        error.Text("message")!.ShouldNotContain("policy-store-secret-detail");
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_spent_quota_refuses_the_next_turn_and_a_new_conversation_but_the_open_one_stays_open()
    {
        var model = Model("One.", "Two.");

        await using var host = await StartAsync(handler: null, model, new StubVoiceProvider());

        await host.Services.GetRequiredService<IQuotaStore>().SaveAsync(
            new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = host.Services.GetRequiredService<IOptions<TraconOptions>>().Value.DefaultTenantId,
                AgentName = Agent,
                Period = QuotaPeriod.Daily,
                MaxRuns = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        await using var client = await ConnectAsync(host, "first");

        await StartConversationAsync(client);
        await TalkAsync(client);

        // The one run the quota allows has run. The turn after it is refused, and the connection is still open.
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();

        await client.WaitForAsync("done", frames);

        frames.Single(static frame => string.Equals(frame.Type, "error", StringComparison.Ordinal)).Text("message").ShouldNotBeNullOrEmpty();
        frames.ShouldNotContain(static frame => frame.Type == "runStarted");
        model.Requests.Count.ShouldBe(1);

        // A conversation that would only start to be refused is refused at its start.
        await using var second = await ConnectAsync(host, "second");

        await second.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var secondFrames = new List<VoiceEvent>();

        await second.WaitForAsync("error", secondFrames);
        secondFrames.ShouldNotContain(static frame => frame.Type == "ready");
    }

    [Fact]
    public async Task An_opening_prompt_is_asked_like_a_turn_and_a_greeting_asks_nothing_more()
    {
        // The start passes and the opening is refused: the conversation is open, the agent said nothing, and it goes on.
        var asked = 0;
        var handler = new RecordingHandler
        {
            OnRun = _ => Interlocked.Increment(ref asked) == 1 ? RunAuthorizationResult.Allow() : RunAuthorizationResult.Deny("No opening for you."),
        };

        var model = Model("never");

        await using var host = await StartAsync(
            handler,
            model,
            new StubVoiceProvider(),
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { OpeningPrompt = "Begin." }));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        await client.WaitForAsync("ready", frames);
        await client.WaitForAsync("done", frames);

        frames.Single(static frame => string.Equals(frame.Type, "error", StringComparison.Ordinal)).Text("message").ShouldBe("No opening for you.");
        frames.ShouldNotContain(static frame => frame.Type == "runStarted");
        model.Requests.ShouldBeEmpty();

        // A greeting is not a run: only the start asked.
        var greetingHandler = new RecordingHandler();

        await using var greetingHost = await StartAsync(
            greetingHandler,
            Model("never"),
            new StubVoiceProvider(),
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { Greeting = "Hello." }));
        await using var greeted = await ConnectAsync(greetingHost);

        await greeted.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await greeted.WaitForAsync("done");

        greetingHandler.RunRequests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Without_a_handler_and_without_a_quota_nothing_changes()
    {
        var model = Model("One.");

        await using var host = await StartAsync(handler: null, model, new StubVoiceProvider());
        await using var client = await ConnectAsync(host);

        await StartConversationAsync(client);
        await TalkAsync(client);

        model.Requests.Count.ShouldBe(1);
    }

    private static FakeModelProvider Model(params string[] answers)
        => new FakeModelProvider(Provider).WithModel(new ModelDescriptor { Name = "gate-1" }).RespondsWith(answers);

    private static async Task StartConversationAsync(VoiceConversationClient client)
    {
        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
    }

    private static async Task TalkAsync(VoiceConversationClient client)
    {
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });
        await client.WaitForAsync("done");
    }

    private static Task<TraconTestHost> StartAsync(
        RecordingHandler? handler,
        FakeModelProvider model,
        StubVoiceProvider voice,
        string? user = null,
        Func<VoiceConnectionRequest, CancellationToken, ValueTask<VoiceConnectionSettings?>>? resolve = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                // The tenant header is read only where tenancy is on; the second tenant of a case is named by it.
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });

                builder
                    .AddModelProvider(model)
                    .AddAgent(Definition(Agent))
                    .AddAgent(Definition(OtherAgent))
                    .UseVoiceConversation(options =>
                    {
                        options.OutputMediaType = "audio/mpeg";
                        options.ResolveConnection = resolve;
                    });
            },
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);

                if (handler is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler));
                }

                if (user is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new FixedAttribution(user)));
                }
            });

    private static AgentDefinition Definition(string name)
        => new()
        {
            Name = name,
            Description = "Talks to the customer.",
            Instructions = "Reply briefly.",
            Model = new ModelBinding { Provider = Provider, Model = "gate-1" },
            Origin = AgentDefinitionOrigin.Code,
        };

    private static async Task<VoiceConversationClient> ConnectAsync(TraconTestHost host, string sessionId = "session-1", string? tenant = null)
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        if (tenant is not null)
        {
            socketClient.ConfigureRequest = request => request.Headers["X-Tracon-Tenant"] = tenant;
        }

        var socket = await socketClient.ConnectAsync(
            new Uri($"http://localhost/tracon/api/voice/sessions/{sessionId}/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }

    private static async Task<VoiceSessionRecord> WaitForRecordAsync(TraconTestHost host)
    {
        var store = host.Services.GetRequiredService<IVoiceSessionStore>();

        var records = await WaitUntil.ValueAsync(
            async () => await store.QueryAsync("default", new VoiceSessionQuery(), TestContext.Current.CancellationToken),
            static found => found.Count > 0,
            "the conversation record to be written");

        return records.Single();
    }

    private sealed class FixedAttribution(string userId) : IRunAttributionContext
    {
        public string? UserId { get; } = userId;

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
