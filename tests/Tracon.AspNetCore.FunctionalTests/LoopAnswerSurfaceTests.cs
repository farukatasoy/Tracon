using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every streaming surface that hands an agent's answer to a program, or speaks
/// it, gets a harness loop's final iteration only. The class scan of the
/// workflow step defect (<see cref="WorkflowLoopStepOutputTests"/>).
/// </summary>
/// <remarks>
/// The native agent stream is not here: it is documented to stream every
/// iteration as it happens.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class LoopAnswerSurfaceTests
{
    private const string ProviderName = "looping";
    private const string ModelName = "loop-model";
    private const string AgentName = "looper";
    private const string Draft = "first draft";
    private const string Final = "final answer ALL DONE";

    [Fact]
    public async Task The_chat_completions_stream_carries_only_the_final_iteration()
    {
        var provider = LoopProvider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = AgentName, stream = true, messages = new[] { new { role = "user", content = "hello" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryOnlyTheFinalIteration(await response.Content.ReadAsStringAsync(), provider);
    }

    [Fact]
    public async Task The_responses_stream_carries_only_the_final_iteration()
    {
        var provider = LoopProvider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new { model = AgentName, stream = true, input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryOnlyTheFinalIteration(await response.Content.ReadAsStringAsync(), provider);
    }

    [Theory]
    [InlineData("SendMessage")]
    [InlineData("SendStreamingMessage")]
    public async Task An_a2a_caller_gets_only_the_final_iteration(string method)
    {
        var provider = LoopProvider();
        await using var host = await StartAsync(
            provider,
            configureTracon: static builder => builder.UseA2A(static options => options.ExposedAgents.Add(AgentName)),
            configureAfterMap: static app => app.MapTraconA2A());

        var message = System.Text.Json.JsonSerializer.SerializeToElement(
            new A2A.SendMessageRequest
            {
                Message = new A2A.Message
                {
                    Role = A2A.Role.User,
                    Parts = [A2A.Part.FromText("hello")],
                    MessageId = Guid.NewGuid().ToString(),
                },
            },
            A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{AgentName}")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method, @params = message }),
        };

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryOnlyTheFinalIteration(await response.Content.ReadAsStringAsync(), provider);
    }

    [Fact]
    public async Task A_voice_conversation_speaks_only_the_final_iteration()
    {
        var provider = LoopProvider();
        var voice = new StubVoiceProvider { Transcript = "write the episode" };

        await using var host = await StartAsync(
            provider,
            configureTracon: static builder => builder.UseVoiceConversation(
                static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
            });

        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        var socket = await socketClient.ConnectAsync(
            new Uri("http://localhost/tracon/api/voice/sessions/session-1/stream"),
            TestContext.Current.CancellationToken);

        await using var client = new VoiceConversationClient(socket);

        await client.SendControlAsync(new { type = "start", agent = AgentName, inputFormat = "pcm16" });
        await client.WaitForAsync("ready");
        await client.SendAudioAsync(new byte[3200]);
        await client.SendControlAsync(new { type = "commit" });
        await client.WaitForAsync("done");

        provider.Requests.Count.ShouldBe(2, "the loop ran a second iteration after the draft");

        var spoken = string.Join(' ', voice.Spoken);
        spoken.ShouldContain("final answer");
        spoken.ShouldNotContain(Draft);
    }

    [Fact]
    public async Task A_live_voice_delegation_speaks_only_the_final_iteration()
    {
        var provider = LoopProvider();
        await using var live = await FakeGptLiveServer.StartAsync();
        await using var host = await StartAsync(
            provider,
            configureTracon: builder => builder
                .UseOpenAI(options =>
                {
                    options.ApiKey = "test-openai-key";
                    options.Endpoint = live.BaseAddress;
                })
                .UseOpenAILive(options => options.Endpoint = live.BaseAddress)
                .UseLiveVoice(static options => options.PersistTranscript = true),
            configureServices: static services => services.Configure<TraconEgressOptions>(
                static options => options.AllowPrivateNetworkTargets = true));

        using var created = await host.Client.PostAsJsonAsync(
            "/tracon/api/voice/live/sessions",
            new LiveVoiceSessionCreateRequest
            {
                SessionId = "session-1",
                Agent = AgentName,
                Sdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nt=0 0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\n",
            },
            TestContext.Current.CancellationToken);

        created.StatusCode.ShouldBe(HttpStatusCode.OK);

        await live.WaitForAttachAsync(LiveVoiceTests.Patience);
        await live.SendInputTranscriptAsync(" Write the episode", 3800, 4200);
        await live.SendDelegationAsync("item_1", 3800);

        await WaitUntil.TrueAsync(
            () => string.Join(' ', live.AppendsOf("session.commentary.append")).Contains("final answer", StringComparison.Ordinal),
            "the final answer to be spoken",
            LiveVoiceTests.Patience);

        provider.Requests.Count.ShouldBe(2, "the loop ran a second iteration after the draft");
        string.Join(' ', live.AppendsOf("session.commentary.append")).ShouldNotContain(Draft);
    }

    private static void ShouldCarryOnlyTheFinalIteration(string body, FakeModelProvider provider)
    {
        provider.Requests.Count.ShouldBe(2, "the loop ran a second iteration after the draft");
        body.ShouldContain("final answer");
        body.ShouldNotContain(Draft);
    }

    private static FakeModelProvider LoopProvider()
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, static cfg => cfg.RespondsWith(Draft, Final));

        return provider;
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null,
        Action<Microsoft.AspNetCore.Builder.WebApplication>? configureAfterMap = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Writes until the draft is done.",
                    Instructions = "Write.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Harness = new HarnessSettings
                    {
                        Loop = new LoopSettings
                        {
                            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
                            MaxIterations = 3,
                        },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });

                configureTracon?.Invoke(builder);
            },
            configureServices: configureServices,
            configureAfterMap: configureAfterMap);
}
