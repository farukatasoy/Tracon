using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the host decides about a voice conversation connection (the connection resolver), how a conversation opens (a greeting
/// or an opening prompt), how the agent ends it, and what its record says.
/// </summary>
/// <remarks>
/// <para>
/// The sockets are real (the test server's WebSocket) and the speech providers are the stub, so the frames, the order they come
/// in and what the stub was asked to say are measured, not assumed. The model is scripted.
/// </para>
/// <para>
/// 🚨 The cases around the resolver read what reached the <em>run</em> and the <em>synthesizer</em>, not what the resolver
/// returned: a resolver whose answer is not used would pass a test that only reads its answer.
/// </para>
/// </remarks>
public sealed class VoiceConversationConnectionTests
{
    private const string Agent = "voice-agent";
    private const string Provider = "voice-script";
    private const string Probe = "probe_context";

    [Fact]
    public async Task With_a_resolver_the_voice_the_client_asked_for_is_ignored_and_the_resolvers_is_spoken()
    {
        var voice = new StubVoiceProvider();
        VoiceConnectionRequest? seen = null;

        await using var host = await StartAsync(
            voice,
            Model().RespondsWith("Fine."),
            resolve: (request, _) =>
            {
                seen = request;

                return ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { VoiceId = "host-voice" });
            });
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, voiceId = "client-voice", inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);

        voice.VoiceIds.ShouldNotBeEmpty();
        voice.VoiceIds.ShouldAllBe(static id => id == "host-voice", "the voice of the front end never reaches the synthesizer");

        // What the front end asked for is an input of the resolver and nothing more.
        var request = seen.ShouldNotBeNull();
        request.ClientVoiceId.ShouldBe("client-voice");
        request.AgentName.ShouldBe(Agent);
        request.SessionId.ShouldBe("session-1");
        request.TenantId.ShouldNotBeNullOrWhiteSpace();
        request.User.ShouldNotBeNull();
    }

    [Fact]
    public async Task Without_a_resolver_the_voice_the_client_asked_for_is_spoken_as_it_always_was()
    {
        var voice = new StubVoiceProvider();

        await using var host = await StartAsync(voice, Model().RespondsWith("Fine."), resolve: null);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, voiceId = "client-voice", inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);

        voice.VoiceIds.ShouldNotBeEmpty();
        voice.VoiceIds.ShouldAllBe(static id => id == "client-voice");
    }

    [Fact]
    public async Task A_resolver_that_names_no_voice_leaves_the_default_voice_and_never_the_clients()
    {
        var voice = new StubVoiceProvider();

        await using var host = await StartAsync(
            voice,
            Model().RespondsWith("Fine."),
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(null));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, voiceId = "client-voice", inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);

        voice.VoiceIds.ShouldNotBeEmpty();
        voice.VoiceIds.ShouldAllBe(static id => id == null, "a null answer is an answer: the front end gets no say");
    }

    [Fact]
    public async Task The_values_the_resolver_returns_reach_the_run_as_the_hosts_and_nothing_the_client_sent_does()
    {
        var probe = new ScopeProbe();

        await using var host = await StartAsync(
            new StubVoiceProvider(),
            Model().CallsTool(Probe, new { }).EchoesLastToolResult(),
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings
            {
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" },
            }),
            probe: probe,
            toolNames: [Probe]);
        await using var client = await ConnectAsync(host);

        // The protocol carries no parameter; a front end that sends one anyway changes nothing.
        await client.SendControlAsync(new
        {
            type = "start",
            agent = Agent,
            inputFormat = "pcm16",
            parameters = new { customer = "Evil" },
        });
        await client.WaitForAsync("ready");
        await TalkAsync(client);

        var read = probe.Reads.ShouldHaveSingleItem();
        read.Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("customer", "Acme");
        read.Parameters.Count.ShouldBe(2, "the declared tone has a default and is recorded too");
        read.Source.ShouldBe(ParameterValueSource.Host);
    }

    [Fact]
    public async Task Without_a_resolver_a_tool_reads_no_parameter_values()
    {
        var probe = new ScopeProbe();

        await using var host = await StartAsync(
            new StubVoiceProvider(),
            Model().CallsTool(Probe, new { }).EchoesLastToolResult(),
            resolve: null,
            probe: probe,
            toolNames: [Probe]);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);

        probe.Reads.ShouldHaveSingleItem().Parameters.ShouldBeNull();
    }

    [Fact]
    public async Task The_resolver_is_asked_once_per_connection_whatever_the_number_of_turns()
    {
        var calls = 0;

        await using var host = await StartAsync(
            new StubVoiceProvider(),
            Model().RespondsWith("One.", "Two."),
            resolve: (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<VoiceConnectionSettings?>(null);
            });
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);
        await TalkAsync(client);

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_resolver_that_throws_ends_the_connection_with_a_fixed_text_and_starts_nothing()
    {
        var voice = new StubVoiceProvider();
        var model = Model().RespondsWith("never");

        await using var host = await StartAsync(
            voice,
            model,
            resolve: static (_, _) => throw new InvalidOperationException("tenant-secret-detail"));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();
        var error = await client.WaitForAsync("error", frames);

        error.Text("message").ShouldBe("The conversation could not be started.");
        error.Text("message")!.ShouldNotContain("tenant-secret-detail");
        frames.ShouldNotContain(static frame => frame.Type == "ready");
        model.Requests.ShouldBeEmpty("no run starts");
        voice.Spoken.ShouldBeEmpty("nothing is said");

        (await WaitForRecordsAsync(host)).EnumerateArray().Single().GetProperty("endReason").GetString().ShouldBe("Error");
    }

    [Fact]
    public async Task A_result_that_holds_both_a_greeting_and_an_opening_prompt_is_refused()
    {
        var model = Model().RespondsWith("never");

        await using var host = await StartAsync(
            new StubVoiceProvider(),
            model,
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings
            {
                Greeting = "Hello.",
                OpeningPrompt = "Begin.",
            }));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        (await client.WaitForAsync("error", frames)).Text("message").ShouldBe("The conversation could not be started.");
        frames.ShouldNotContain(static frame => frame.Type == "ready");
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_parameter_the_agent_does_not_declare_stops_the_start_and_its_value_is_not_in_the_answer()
    {
        var model = Model().RespondsWith("never");

        await using var host = await StartAsync(
            new StubVoiceProvider(),
            model,
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings
            {
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["not_declared"] = "very-private-value" },
            }));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();
        var error = await client.WaitForAsync("error", frames);

        error.Text("message")!.ShouldNotContain("very-private-value");
        frames.ShouldNotContain(static frame => frame.Type == "ready");
        model.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_resolver_is_not_asked_about_an_agent_that_does_not_exist()
    {
        var calls = 0;

        await using var host = await StartAsync(
            new StubVoiceProvider(),
            Model().RespondsWith("never"),
            resolve: (_, _) =>
            {
                Interlocked.Increment(ref calls);

                return ValueTask.FromResult<VoiceConnectionSettings?>(null);
            });
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = "no-such-agent", inputFormat = "pcm16" });

        (await client.WaitForAsync("error")).Text("message").ShouldBe("There is no agent named 'no-such-agent'.");
        calls.ShouldBe(0, "a name nothing carries is a plain 'no such agent', not a failure of the host's code");
    }

    [Fact]
    public async Task A_greeting_is_spoken_right_after_ready_and_the_agent_knows_it_was_said()
    {
        var voice = new StubVoiceProvider();
        var model = Model().RespondsWith("Fine.");

        await using var host = await StartAsync(
            voice,
            model,
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { Greeting = "Welcome to the lesson." }));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        await client.WaitForAsync("ready", frames);

        var done = await client.WaitForAsync("done", frames);

        done.Flag("cancelled").ShouldBe(false);
        done.Json.ShouldNotBeNull().GetProperty("turn").GetInt32().ShouldBe(0, "the greeting is not one of the user's turns");
        frames.Where(static frame => frame.Type is not null).Select(static frame => frame.Type)
            .ShouldBe(["ready", "text", "audioStart", "audioEnd", "done"]);
        frames.ShouldContain(static frame => frame.IsAudio);
        frames.Single(static frame => string.Equals(frame.Type, "text", StringComparison.Ordinal)).Text("delta").ShouldBe("Welcome to the lesson.");
        voice.Spoken.ShouldBe(["Welcome to the lesson."]);
        model.Requests.ShouldBeEmpty("a greeting is a fixed text: no model is called and no run is recorded");

        // The next turn's model reads the history, and the greeting is in it as the assistant's.
        await TalkAsync(client);

        var request = model.Requests.ShouldHaveSingleItem();
        request.Messages.ShouldContain(static message => message.Role == Microsoft.Extensions.AI.ChatRole.Assistant && message.Text == "Welcome to the lesson.");
    }

    [Fact]
    public async Task A_greeting_that_is_not_the_first_word_is_not_said_again_when_the_client_reconnects()
    {
        var voice = new StubVoiceProvider();

        await using var host = await StartAsync(
            voice,
            Model().RespondsWith("Fine."),
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { Greeting = "Welcome." }));

        await using (var first = await ConnectAsync(host))
        {
            await first.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
            await first.WaitForAsync("ready");
            await first.WaitForAsync("done");
            await TalkAsync(first);
        }

        var spokenBefore = voice.Spoken.Count;

        await using var second = await ConnectAsync(host);

        await second.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await second.WaitForAsync("ready");

        // A conversation that is listening answers an empty commit with `idle`; one that was opening would have refused it.
        await second.SendControlAsync(new { type = "commit" });
        await second.WaitForAsync("idle").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        voice.Spoken.Count.ShouldBe(spokenBefore, "nothing was said on the second connection");
    }

    [Fact]
    public async Task An_opening_prompt_makes_the_agent_speak_first_as_a_recorded_run()
    {
        var voice = new StubVoiceProvider();
        var model = Model().RespondsWith("Hello student.");

        await using var host = await StartAsync(
            voice,
            model,
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { OpeningPrompt = "Begin the lesson." }));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });

        var frames = new List<VoiceEvent>();

        await client.WaitForAsync("ready", frames);
        await client.WaitForAsync("done", frames);

        var runStarted = frames.Single(static frame => string.Equals(frame.Type, "runStarted", StringComparison.Ordinal));
        Guid.TryParse(runStarted.Text("runId"), out _).ShouldBeTrue();
        voice.Spoken.ShouldBe(["Hello student."]);

        var request = model.Requests.ShouldHaveSingleItem();
        request.Messages.ShouldContain(static message => message.Role == Microsoft.Extensions.AI.ChatRole.User && message.Text == "Begin the lesson.");
    }

    [Fact]
    public async Task While_the_assistant_opens_a_commit_is_refused_and_a_cancel_cuts_the_opening()
    {
        var voice = new StubVoiceProvider { HoldSynthesis = true };

        await using var host = await StartAsync(
            voice,
            Model().RespondsWith("never"),
            resolve: static (_, _) => ValueTask.FromResult<VoiceConnectionSettings?>(new VoiceConnectionSettings { Greeting = "Welcome." }));
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        // The synthesis holds, so the opening is still in progress: audio is ignored and a commit answers nothing.
        await client.SendAudioAsync(new byte[1600]);
        await client.SendControlAsync(new { type = "commit" });
        await client.SendControlAsync(new { type = "cancel" });

        var frames = new List<VoiceEvent>();
        var done = await client.WaitForAsync("done", frames);

        done.Flag("cancelled").ShouldBe(true);
        frames.ShouldNotContain(static frame => frame.Type == "idle", "the refused commit said nothing");
        frames.ShouldNotContain(static frame => frame.Type == "transcript");
    }

    [Fact]
    public async Task The_end_conversation_tool_ends_the_conversation_after_the_answer_is_spoken()
    {
        var voice = new StubVoiceProvider();

        await using var host = await StartAsync(
            voice,
            Model().CallsTool(EndConversationTool.ToolName, new { }).EchoesLastToolResult(),
            resolve: null,
            toolNames: [EndConversationTool.ToolName]);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        var frames = new List<VoiceEvent>();

        await client.WaitForAsync("done", frames);
        var ended = await client.WaitForAsync("ended", frames);

        ended.Text("reason").ShouldBe("agent");
        voice.Spoken.ShouldNotBeEmpty("the goodbye is spoken before the connection closes");
        frames.FindIndex(static frame => string.Equals(frame.Type, "audioEnd", StringComparison.Ordinal))
            .ShouldBeLessThan(frames.FindIndex(static frame => string.Equals(frame.Type, "ended", StringComparison.Ordinal)));

        (await client.ReceiveAsync()).ShouldBeNull("the server closes the socket after `ended`");

        (await WaitForRecordsAsync(host)).EnumerateArray().Single().GetProperty("endReason").GetString().ShouldBe("Agent");
        host.Services.GetRequiredService<VoiceConversationEndRegistry>().Count.ShouldBe(0, "a closed connection leaves no turn behind");
    }

    [Fact]
    public async Task A_barge_in_drops_the_end_request_and_the_conversation_goes_on()
    {
        var voice = new StubVoiceProvider { HoldSynthesis = true };

        await using var host = await StartAsync(
            voice,
            Model().CallsTool(EndConversationTool.ToolName, new { }).EchoesLastToolResult(),
            resolve: null,
            toolNames: [EndConversationTool.ToolName]);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");

        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });

        // The caption is the echo of the tool's result, so it only exists once the agent has called the tool: the end request
        // is made, and the synthesis holds the goodbye in progress.
        await client.WaitForAsync("text");

        // The agent asked to end, and the user interrupted its goodbye: the conversation is not over.
        await client.SendControlAsync(new { type = "cancel" });

        var frames = new List<VoiceEvent>();

        (await client.WaitForAsync("done", frames)).Flag("cancelled").ShouldBe(true);

        await client.SendControlAsync(new { type = "commit" });
        await client.WaitForAsync("idle", frames).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        frames.ShouldNotContain(static frame => frame.Type == "ended");
    }

    [Fact]
    public async Task The_tool_ends_nothing_in_a_run_that_is_not_a_voice_conversation()
    {
        await using var host = await StartAsync(
            new StubVoiceProvider(),
            Model().CallsTool(EndConversationTool.ToolName, new { }).EchoesLastToolResult(),
            resolve: null,
            toolNames: [EndConversationTool.ToolName]);

        using var response = await host.Client.PostAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new StringContent("""{"message":"bye"}""", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain(EndConversationTool.NotAConversationText);
    }

    [Fact]
    public async Task An_agent_that_does_not_list_the_tool_cannot_end_a_conversation()
    {
        await using var host = await StartAsync(
            new StubVoiceProvider(),
            Model().RespondsWith("Fine."),
            resolve: null,
            toolNames: []);

        var tools = host.Services.GetRequiredService<IToolRegistry>().List();

        tools.ShouldContain(static tool => tool.Name == EndConversationTool.ToolName, "the tool exists");

        var definition = await host.Services.GetRequiredService<IAgentCatalog>().DescribeAsync(Agent, TestContext.Current.CancellationToken);

        definition.ShouldNotBeNull().ToolNames.ShouldNotContain(EndConversationTool.ToolName, StringComparer.Ordinal, "and only an agent that names it can call it");
    }

    [Fact]
    public async Task The_record_names_the_provider_the_model_and_prices_both_directions()
    {
        var voice = new StubVoiceProvider { Transcript = "where is my order", TranscriptionModelId = "stt-1" };

        await using var host = await StartAsync(
            voice,
            Model().RespondsWith("Twelve chars"),
            resolve: null,
            pricing: new FixedPricing());
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);
        await client.SendControlAsync(new { type = "stop" });

        var record = (await WaitForRecordsAsync(host)).EnumerateArray().Single();

        record.GetProperty("provider").GetString().ShouldBe("test-voice");
        record.GetProperty("model").GetString().ShouldBe("stt-1");

        // 12 spoken characters at 0.5 each, 1.5 transcribed seconds at 2 per second; the amounts are the price at write time.
        var cost = record.GetProperty("cost");
        cost.GetProperty("characterCost").GetDecimal().ShouldBe(6.0m);
        cost.GetProperty("durationCost").GetDecimal().ShouldBe(3.0m);
        cost.GetProperty("currency").GetString().ShouldBe("TST");
    }

    [Fact]
    public async Task Without_a_price_the_cost_is_absent_and_never_zero()
    {
        await using var host = await StartAsync(new StubVoiceProvider(), Model().RespondsWith("Fine."), resolve: null);
        await using var client = await ConnectAsync(host);

        await client.SendControlAsync(new { type = "start", agent = Agent, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await TalkAsync(client);
        await client.SendControlAsync(new { type = "stop" });

        var record = (await WaitForRecordsAsync(host)).EnumerateArray().Single();

        record.GetProperty("provider").GetString().ShouldBe("test-voice");
        (record.TryGetProperty("cost", out var cost) && cost.ValueKind != JsonValueKind.Null).ShouldBeFalse("nothing was priced");
    }

    private static FakeModelProvider Model() => new FakeModelProvider(Provider).WithModel(new ModelDescriptor { Name = "voice-1" });

    private static async Task TalkAsync(VoiceConversationClient client)
    {
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });
        await client.WaitForAsync("done");
    }

    private static Task<TraconTestHost> StartAsync(
        StubVoiceProvider voice,
        FakeModelProvider model,
        Func<VoiceConnectionRequest, CancellationToken, ValueTask<VoiceConnectionSettings?>>? resolve,
        ScopeProbe? probe = null,
        string[]? toolNames = null,
        IVoicePricingReader? pricing = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(model)
                    .AddAgent(new AgentDefinition
                    {
                        Name = Agent,
                        Description = "Talks to the customer.",
                        Instructions = "Hello {{customer}}, reply briefly.",
                        Parameters =
                        [
                            new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" },
                            new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
                        ],
                        Model = new ModelBinding { Provider = Provider, Model = "voice-1" },
                        ToolNames = toolNames ?? [],
                        Origin = AgentDefinitionOrigin.Code,
                    })
                    .UseVoiceConversation(options =>
                    {
                        options.OutputMediaType = "audio/mpeg";
                        options.ResolveConnection = resolve;
                    });

                if (probe is not null)
                {
                    builder.AddTool(
                        (Func<string>)(() =>
                        {
                            probe.Read();

                            return "read";
                        }),
                        name: Probe,
                        description: "Reads the run's parameters the way a tool of the host would.");
                }
            },
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);

                if (pricing is not null)
                {
                    services.AddSingleton(pricing);
                }
            });

    private static async Task<VoiceConversationClient> ConnectAsync(TraconTestHost host, string sessionId = "session-1")
    {
        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        var socket = await socketClient.ConnectAsync(
            new Uri($"http://localhost/tracon/api/voice/sessions/{sessionId}/stream"),
            TestContext.Current.CancellationToken);

        return new VoiceConversationClient(socket);
    }

    /// <summary>Polls the conversation list until the record is written; it is written when the socket closes.</summary>
    private static Task<JsonElement> WaitForRecordsAsync(TraconTestHost host)
        => WaitUntil.ValueAsync(
            async () =>
            {
                using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/sessions", UriKind.Relative));

                response.EnsureSuccessStatusCode();

                return await TraconTestHost.ReadJsonAsync(response);
            },
            static body => body.GetArrayLength() > 0,
            "the conversation record to be written");

    /// <summary>What a tool of the run sees in its scope. One per test, so parallel tests do not share it.</summary>
    private sealed class ScopeProbe
    {
        private readonly List<ScopeRead> _reads = [];
        private readonly Lock _gate = new();

        public IReadOnlyList<ScopeRead> Reads
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reads];
                }
            }
        }

        public void Read()
        {
            var scope = TraconRunContext.Current;

            lock (_gate)
            {
                _reads.Add(new ScopeRead(scope?.Parameters, scope?.ParameterSource ?? ParameterValueSource.Client));
            }
        }
    }

    private sealed record ScopeRead(IReadOnlyDictionary<string, string>? Parameters, ParameterValueSource Source);

    /// <summary>A price table of round numbers: 0.5 per synthesized character and 2 per transcribed second.</summary>
    private sealed class FixedPricing : IVoicePricingReader
    {
        public string? Currency => "TST";

        public decimal? ForCharacters(decimal characters) => characters * 0.5m;

        public decimal? ForSynthesis(string providerName, string? modelId, decimal characters) => characters * 0.5m;

        public decimal? ForTranscription(string providerName, string? modelId, TimeSpan duration)
            => (decimal)duration.TotalSeconds * 2m;
    }
}
