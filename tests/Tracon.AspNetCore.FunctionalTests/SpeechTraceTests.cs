using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Speech a run produces or transcribes is a span of the run.
/// </summary>
/// <remarks>
/// <para>
/// The adapter handed the consumer's Microsoft.Extensions.AI speech client to the voice tools as it was, so a slow
/// synthesis read in the trace as one opaque tool span. The client is now inside the library's OpenTelemetry ring,
/// built once when the adapter is built (the adapter is a singleton per registered name).
/// </para>
/// <para>
/// The real <c>OpenAIClient</c> runs against <see cref="ScriptedOpenAIAudioServer"/>, the way the voice tool tests
/// drive it, and the voice tools run as tools of an agent: the span counts only when
/// <c>GET /api/runs/{id}/trace</c> returns it.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class SpeechTraceTests
{
    private const string AgentName = "speech-trace-agent";
    private const string ProviderName = "speech-trace-model";

    // The library names every non-chat call "generate_content" and tells them apart by the output type.
    private const string GenerateContent = "generate_content";

    [Fact]
    public async Task The_speak_tool_writes_a_synthesis_span_into_the_run_trace()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server, new FakeModelProvider(ProviderName)
            .CallsTool("speak", new { text = "hello there" })
            .EchoesLastToolResult());

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName);

        RunTraceReader.Operations(trace, GenerateContent)
            .ShouldHaveSingleItem().Attributes["gen_ai.output.type"].ShouldBe("audio");
    }

    [Fact]
    public async Task The_transcribe_tool_writes_a_transcription_span_into_the_run_trace()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();

        // The attachment must exist before the agent names it, so the tool call is built after the host is up.
        var model = new FakeModelProvider(ProviderName);
        await using var host = await StartAsync(server, model);

        var descriptor = await host.Services.GetRequiredService<IAttachmentStore>().SaveAsync(
            new AttachmentContent
            {
                TenantId = "default",
                FileName = "question.mp3",
                MediaType = "audio/mpeg",
                Data = ScriptedOpenAIAudioServer.Mp3,
            },
            TestContext.Current.CancellationToken);

        model.CallsTool("transcribe", new { attachmentId = descriptor.Id.ToString() }).EchoesLastToolResult();

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName);

        RunTraceReader.Operations(trace, GenerateContent)
            .ShouldHaveSingleItem().Attributes["gen_ai.output.type"].ShouldBe("text");
    }

    private static Task<TraconTestHost> StartAsync(ScriptedOpenAIAudioServer server, FakeModelProvider model)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseOpenAI(options =>
                {
                    options.ApiKey = "scripted-server-key";
                    options.Endpoint = server.BaseAddress;
                })
                .UseOpenAISpeech(options =>
                {
                    options.SynthesisModel = "tts-test";
                    options.TranscriptionModel = "stt-test";
                    options.UseAsDefault = true;
                })
                .UseVoice(static _ => { })
                .AddModelProvider(model)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Use the voice tool.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "speech-1" },
                    ToolNames = ["speak", "transcribe"],
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Observability.SuccessSampleRatio = 1));
}
